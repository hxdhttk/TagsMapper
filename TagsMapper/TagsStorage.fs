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

type FullTag = string * string
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

type IDictionary<'k, 'v> with
    member this.Append(other: IDictionary<'k, 'v>) =
        other
        |> Seq.iter (fun kv -> this.Add(kv.Key, kv.Value))

let private flip (a, b) = (b, a)

let private testPath path = Directory.Exists(path)

let private listTakeRange low high (lst: List<'a>) =
    if lst.Count < (high - low) then
        seq { yield! lst }
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
        let archives = Directory.EnumerateDirectories(localPath, "*.cbz", SearchOption.AllDirectories) |> ResizeArray
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

    let fileNameToTitlePairs =
        fileNames
        |> Seq.map (fun fileName ->
            let regexMatch = fileNameRegex.Match fileName
            fileName, regexMatch.Groups.[1].Value)

    let titleToFileNamePairs =
        fileNameToTitlePairs
        |> Seq.map flip

    let previousData = loadPreviousData()

    let fileNameToTitleMap = previousData.FileNameToTitleMap
    let titleToFileNameMap = previousData.TitleToFileNameMap

    fileNameToTitleMap.Append(fileNameToTitlePairs |> dict)
    titleToFileNameMap.Append(titleToFileNamePairs |> dict)

    let titles = fileNameToTitlePairs |> Seq.map snd

    let parallelOptions = ParallelOptions()
    parallelOptions.MaxDegreeOfParallelism <- 2

    let titleToFullTagsMap = previousData.TitleToFullTagsMap
    let titleToTagsMap = previousData.TitleToTagsMap

    use wrapper = new HttpWrapper(config)
    use random = new ThreadLocal<Random>(fun () -> Random())

    Parallel.ForEach(titles, parallelOptions, fun title ->
        let waitInterval = 5000 + random.Value.Next(0, 15000)
        Thread.Sleep(waitInterval)

        let tagsSearchResult = retryTask 5 (wrapper.GetTags title)
        match tagsSearchResult with
        | Some (Some task) ->
            let rawTagsOpt = retryTask 5 task
            match rawTagsOpt with
            | Some rawTags ->
                let tags = rawTags |> Seq.map snd |> Seq.toArray

                Monitor.Enter(titleToFullTagsMap)
                Monitor.Enter(titleToTagsMap)

                titleToFullTagsMap.Add(title, HashSet(rawTags))
                titleToTagsMap.Add(title, HashSet(tags))

                Monitor.Exit(titleToTagsMap)
                Monitor.Exit(titleToFullTagsMap)
            
            | None -> ()
        
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