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
    |> JsonConvert.SerializeObject
    |> fun data -> File.WriteAllText("Data.json", data)
    printfn "Current index: %d" high

let searchByTitle term =
    Config.loadConfig()
    |> TagsStorage.searchByTitle term
    |> printfn "%A"

let searchByTag term =
    Config.loadConfig()
    |> TagsStorage.searchByTag term
    |> printfn "%A"

[<EntryPoint>]
let main argv =
    
    match parseOption argv with
    | LoadArchivePaths -> loadArchivePaths()
    | Build (low, high) -> build low high
    | Update -> ()
    | Search (Title, term) -> searchByTitle term
    | Search (Tag, term) -> searchByTag term
    | _ -> ()

    0
