module Config

open System.IO

open Newtonsoft.Json

[<CLIMutable>]
type Config = {
    LocalPath: string
    SiteUrl: string
    Cookie: string
}

let private configPath = "Config.json"

let loadConfig () =
    let configStr = File.ReadAllText configPath
    JsonConvert.DeserializeObject<Config> configStr

