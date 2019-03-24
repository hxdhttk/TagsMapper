// Learn more about F# at http://fsharp.org

open System.IO

open Newtonsoft.Json

type Operation =
    | Build
    | Update
    | Search
    | Unknown

let private parseOption arg =
    match arg with
    | "build" -> Build
    | "update" -> Update
    | "search" -> Search
    | _ -> Unknown

let build () =
    Config.loadConfig ()
    |> TagsStorage.build
    |> JsonConvert.SerializeObject
    |> fun data -> File.WriteAllText("Data.json", data)

[<EntryPoint>]
let main argv =
    
    if argv.Length <> 1 then 
        ()
    else
        match parseOption argv.[0] with
        | Build -> build()
        | Update -> ()
        | Search -> ()
        | Unknown -> ()

    0
