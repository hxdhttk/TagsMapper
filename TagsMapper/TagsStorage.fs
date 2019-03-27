module TagsStorage

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

open Newtonsoft.Json

open Config
open HttpWrapper

type FullTag = string
type Tag = string

[<CLIMutable>]
type TagsStorage = {
    ArchivePaths: List<string>
    
    FileNameToTitleMap: Dictionary<string, string>
    TitleToFileNameMap: Dictionary<string, string>
    
    TitleToFullTagsMap: Dictionary<string, HashSet<FullTag>>
    TitleToTagsMap: Dictionary<string, HashSet<Tag>>
    
    FullTagToTitlesMap: Dictionary<FullTag, HashSet<string>>
    TagToTitlesMap: Dictionary<Tag, HashSet<string>>
}

type private IDictionary<'k, 'v> with
    member this.Append(other: IDictionary<'k, 'v>) =
        other
        |> Seq.iter (fun kv -> this.Add(kv.Key, kv.Value))

    member this.AddOrSet(key, value) =
        if this.ContainsKey(key) then
            this.[key] <- value
        else
            this.Add(key, value)

let private flip (a, b) = (b, a)

let private testPath path = File.Exists(path)

let private rawTagToFullTag (a, b) =
    sprintf "%s%s" a b

let private listTakeRange low high (lst: List<'a>) =
    if lst.Count - 1 < low then
        Seq.empty
    elif (lst.Count - low) < (high - low + 1) then
        lst |> Seq.skip low
    else
        lst |> Seq.skip low |> Seq.take (high - low + 1)

let rec private retryTask count tsk =
    if count = 0 then
        None
    else
        try
            tsk |> Async.AwaitTask |> Async.RunSynchronously |> Some
        with
        | _ -> retryTask (count - 1) tsk

let loadArchivePaths (config: Config) =
    let dataPath = "ArchivePaths.json"
    if testPath dataPath then
        dataPath |> File.ReadAllText |> JsonConvert.DeserializeObject<List<string>>
    else
        let localPath = config.LocalPath
        let archives = Directory.EnumerateFiles(localPath, "*.cbz", SearchOption.AllDirectories) |> ResizeArray
        File.WriteAllText(dataPath, JsonConvert.SerializeObject(archives))
        archives

let loadPreviousData () =
    let dataPath = "Data.json"
    if testPath dataPath then
        dataPath |> File.ReadAllText |> JsonConvert.DeserializeObject<TagsStorage>
    else
        {
            ArchivePaths = List<string>()
    
            FileNameToTitleMap = Dictionary<string, string>()
            TitleToFileNameMap = Dictionary<string, string>()
    
            TitleToFullTagsMap = Dictionary<string, HashSet<FullTag>>()
            TitleToTagsMap = Dictionary<string, HashSet<Tag>>()
    
            FullTagToTitlesMap = Dictionary<FullTag, HashSet<string>>()
            TagToTitlesMap = Dictionary<Tag, HashSet<string>>()
        }

let build low high (config: Config) =
    
    let archivePaths = loadArchivePaths config;

    let archives = archivePaths |> listTakeRange low high

    let fileNames =
        archives
        |> Seq.map (fun archive -> 
            let fileInfo = FileInfo(archive)
            fileInfo.Name)

    let fileNameRegex = @"\[.+\] (.+).cbz" |> Regex    

    let previousData = loadPreviousData()

    let fileNameToTitleMap = previousData.FileNameToTitleMap
    let titleToFileNameMap = previousData.TitleToFileNameMap

    let fileNameToTitlePairs =
        seq {
            for fileName in fileNames do
                let regexMatch = fileNameRegex.Match fileName
                let title = regexMatch.Groups.[1].Value
                if not (titleToFileNameMap.ContainsKey(title)) then
                    yield fileName, title
        } |> Seq.cache

    let titleToFileNamePairs =
        fileNameToTitlePairs
        |> Seq.map flip

    fileNameToTitleMap.Append(fileNameToTitlePairs |> dict)
    titleToFileNameMap.Append(titleToFileNamePairs |> dict)

    let titles = fileNameToTitlePairs |> Seq.map snd

    let parallelOptions = ParallelOptions()
    parallelOptions.MaxDegreeOfParallelism <- 1

    let titleToFullTagsMap = previousData.TitleToFullTagsMap
    let titleToTagsMap = previousData.TitleToTagsMap

    use wrapper = new HttpWrapper(config)
    use random = new ThreadLocal<Random>(fun () -> Random())

    Parallel.ForEach(titles, parallelOptions, fun title ->
        let waitInterval = 5000 + random.Value.Next(0, 15000)
        Thread.Sleep(waitInterval)

        let rawTagsOpt = retryTask 5 (wrapper.GetTags title)
        match rawTagsOpt with
        | Some (Some rawTags) ->
            let tags = rawTags |> Seq.map snd |> Seq.toArray

            Monitor.Enter(titleToFullTagsMap)
            Monitor.Enter(titleToTagsMap)

            titleToFullTagsMap.AddOrSet(title, HashSet(rawTags |> Seq.map rawTagToFullTag))
            titleToTagsMap.AddOrSet(title, HashSet(tags))

            Monitor.Exit(titleToTagsMap)
            Monitor.Exit(titleToFullTagsMap)
        
        | _ -> ()
    ) |> ignore

    let fullTagToTitlesMap =
        titleToFullTagsMap
        |> Seq.map (fun kv ->
            kv.Value |> Seq.map (fun fullTag -> (kv.Key, fullTag)))
        |> Seq.concat
        |> Seq.groupBy (fun (_, fullTag) -> fullTag)
        |> Seq.map (fun (fullTag, grouping) -> 
            fullTag, (grouping |> Seq.map fst |> HashSet))
        |> dict
        |> Dictionary

    let tagToTitlesMap =
        titleToTagsMap
        |> Seq.map (fun kv ->
            kv.Value |> Seq.map (fun tag -> (kv.Key, tag)))
        |> Seq.concat
        |> Seq.groupBy (fun (_, tag) -> tag)
        |> Seq.map (fun (tag, grouping) -> 
            tag, (grouping |> Seq.map fst |> HashSet))
        |> dict
        |> Dictionary

    {
        ArchivePaths = archivePaths

        FileNameToTitleMap = fileNameToTitleMap
        TitleToFileNameMap = titleToFileNameMap

        TitleToFullTagsMap = titleToFullTagsMap
        TitleToTagsMap = titleToTagsMap

        FullTagToTitlesMap = fullTagToTitlesMap
        TagToTitlesMap = tagToTitlesMap
    }