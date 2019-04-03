// Learn more about F# at http://fsharp.org

open System.IO

open Newtonsoft.Json

type Operation =
    | LoadArchivePaths
    | Build of int * int
    | Update
    | Search of SearchMode * string 
    | Unknown

and SearchMode =
    | Title
    | Tag
    | BadMode

let private parseSearchMode searchMode =
    match searchMode with
    | "title" -> Title
    | "tag" -> Tag
    | _ -> BadMode

let private parseOption arg =
    match arg with
    | [| "load" |] -> LoadArchivePaths
    | [| "build"; low; high |] -> Build (int low, int high)
    | [| "update" |] -> Update
    | [| "search"; searchMode; term |] ->
        match parseSearchMode searchMode with
        | Title -> Search (Title, term)
        | Tag -> Search (Tag, term)
        | BadMode -> Search (BadMode, term)
    | _ -> Unknown

let loadArchivePaths () =
    Config.loadConfig()
    |> TagsStorage.loadArchivePaths
    |> Seq.iter (printfn "%s")

let build low high =
    Config.loadConfig ()
    |> TagsStorage.build low high
    printfn "Current index: %d" high

let update () =
    Config.loadConfig()
    |> TagsStorage.update
    |> JsonConvert.SerializeObject
    |> fun data -> File.WriteAllText("Data.json", data)

let searchByTitle term =
    TagsStorage.searchByTitle term
    |> fun res -> JsonConvert.SerializeObject(res, Formatting.Indented)
    |> printfn "%s"

let searchByTag term =
    TagsStorage.searchByTag term
    |> fun res -> JsonConvert.SerializeObject(res, Formatting.Indented)
    |> printfn "%s"

[<EntryPoint>]
let main argv =
    
    match parseOption argv with
    | LoadArchivePaths -> loadArchivePaths ()
    | Build (low, high) -> build low high
    | Update -> update ()
    | Search (Title, term) -> searchByTitle term
    | Search (Tag, term) -> searchByTag term
    | _ -> ()

    0
