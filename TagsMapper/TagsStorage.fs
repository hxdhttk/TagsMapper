﻿module TagsStorage

open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

open Config
open HttpWrapper

type FullTag = string * string
type Tag = string

[<CLIMutable>]
type TagsStorage = {
    ArchivePaths: HashSet<string>
    
    FileNameToTitleMap: Dictionary<string, string>
    TitleToFileNameMap: Dictionary<string, string>
    
    TitleToFullTagsMap: Dictionary<string, HashSet<FullTag>>
    TitleToTagsMap: Dictionary<string, HashSet<Tag>>
    
    FullTagToTitlesMap: Dictionary<FullTag, HashSet<string>>
    TagToTitlesMap: Dictionary<Tag, HashSet<string>>
}

let private flip (a, b) = (b, a)

let private toFullTagString (tagClass, tag) = sprintf "%s%s" tagClass tag

let build (config: Config) =
    
    let localPath = config.LocalPath

    let archives = Directory.EnumerateFiles(localPath, "*.cbz", SearchOption.AllDirectories) |> Seq.toArray

    let archivePaths = HashSet<string>(archives);

    let fileNames =
        archives
        |> Array.map (fun archive -> 
            let fileInfo = FileInfo(archive)
            fileInfo.Name)

    let fileNameRegex = @"\[.+\] (.+).cbz" |> Regex

    let fileNameToTitlePairs =
        fileNames
        |> Array.map (fun fileName ->
            let regexMatch = fileNameRegex.Match fileName
            fileName, regexMatch.Groups.[1].Value)

    let titleToFileNamePairs =
        fileNameToTitlePairs
        |> Seq.map flip

    let fileNameToTitleMap = Dictionary<string, string>(fileNameToTitlePairs |> dict)
    let titleToFileNameMap = Dictionary<string, string>(titleToFileNamePairs |> dict)

    let titles = fileNameToTitlePairs |> Array.map snd

    let parallelOptions = ParallelOptions()
    parallelOptions.MaxDegreeOfParallelism <- 2

    let titleToFullTagsMap = Dictionary<string, HashSet<FullTag>>()
    let titleToTagsMap = Dictionary<string, HashSet<Tag>>()

    use wrapper = new HttpWrapper(config)
    Parallel.ForEach(titles, parallelOptions, fun title ->
        let tagsSearchResult = wrapper.GetTags title |> Async.AwaitTask |> Async.RunSynchronously
        match tagsSearchResult with
        | Some task ->
            let rawTags = task |> Async.AwaitTask |> Async.RunSynchronously
            let tags = rawTags |> Seq.map snd |> Seq.toArray

            Monitor.Enter(titleToFullTagsMap)
            Monitor.Enter(titleToTagsMap)

            titleToFullTagsMap.Add(title, HashSet(rawTags))
            titleToTagsMap.Add(title, HashSet(tags))

            Monitor.Exit(titleToTagsMap)
            Monitor.Exit(titleToFullTagsMap)

        | None -> ()
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