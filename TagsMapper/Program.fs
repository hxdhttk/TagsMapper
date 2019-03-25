// Learn more about F# at http://fsharp.org

open System.IO

open Newtonsoft.Json

type Operation =
    | LoadArchivePaths
    | Build of int * int
    | Update
    | Search
    | Unknown

let private parseOption arg =
    match arg with
    | [| "load" |] -> LoadArchivePaths
    | [| "build"; low; high |] -> Build (int low, int high)
    | [| "update" |] -> Update
    | [| "search" |] -> Search
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

[<EntryPoint>]
let main argv =
    
    if argv.Length <> 1 then 
        ()
    else
        match parseOption argv with
        | LoadArchivePaths -> loadArchivePaths()
        | Build (low, high) -> build low high
        | Update -> ()
        | Search -> ()
        | Unknown -> ()

    0
