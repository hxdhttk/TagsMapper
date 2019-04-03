module HttpWrapper

open System
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open System.Threading

open FSharp.Control.Tasks
open HtmlAgilityPack

open Config

type HttpWrapper(config: Config) =

    let siteUrl = config.SiteUrl
    let cookie = config.Cookie
    let proxy = config.Proxy

    let isProxyValid = (isNull proxy |> not) && (Uri.IsWellFormedUriString(proxy, UriKind.Absolute))    

    let modifyRequest (request: HttpRequestMessage) =
        let host =
            let regex = @"https://(.+)" |> Regex
            let regexMatch = regex.Match siteUrl
            regexMatch.Groups.[1].Value

        let referer = sprintf "%s/" siteUrl

        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3")
        request.Headers.Add("Accept-Encoding", "utf-8")
        request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,ja;q=0.8,en;q=0.7,zh-TW;q=0.6")
        request.Headers.Add("Connection", "keep-alive")
        request.Headers.Add("Host", host)
        request.Headers.Add("Referer", referer)
        request.Headers.Add("Upgrade-Insecure-Requests", "1")
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/73.0.3683.86 Safari/537.36")
        request.Headers.Add("Cookie", cookie)

        request

    let generateHttpClient () =
        if isProxyValid then
            let handler = new HttpClientHandler()
            handler.Proxy <- WebProxy(proxy)
            handler.UseProxy <- true
            new HttpClient(handler)
        else
            new HttpClient()
        

    let httpClient = new ThreadLocal<HttpClient>(Func<HttpClient>(generateHttpClient), true)

    let getSearchResultPage title =
        task {
            let client = httpClient.Value
            
            let searchUrl = sprintf "%s/?f_search=%s" siteUrl title
            use searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl) |> modifyRequest
            
            let! searchResponse = client.SendAsync(searchRequest)
            return! searchResponse.Content.ReadAsStringAsync()
        }

    let getFirstEntryUrl title =
        task {
            let! pageContent = getSearchResultPage title

            let pageDoc = HtmlDocument()
            pageDoc.LoadHtml(pageContent)

            let searchResultEntries = pageDoc.DocumentNode.SelectNodes(@"//td[@class='gl3c glname']/div[1]/a")
            let firstEntry = 
                searchResultEntries
                |> fun entries -> if isNull entries then None else Seq.tryHead entries
                |> Option.map (fun node -> node.Attributes.["href"].Value)

            return firstEntry
        }

    let getGalleryPage (url: string) =
        task {
            let client = httpClient.Value

            use galleryRequest = new HttpRequestMessage(HttpMethod.Get, url) |> modifyRequest

            let! galleryResponse = client.SendAsync(galleryRequest)
            return! galleryResponse.Content.ReadAsStringAsync()
        }

    let getGalleryTags (url: string) =
        task {
            let! pageContent = getGalleryPage url

            let pageDoc = HtmlDocument()
            pageDoc.LoadHtml(pageContent)

            let tagClassNodes = 
                pageDoc.DocumentNode.SelectNodes(@"//td[@class='tc']")
                |> Seq.map (fun node -> node.ParentNode)

            return 
                if isNull tagClassNodes then None
                else
                    let tags =
                        tagClassNodes
                        |> Seq.map (fun tagClassNode ->
                            let tagClass = tagClassNode.SelectSingleNode(@"td[@class='tc']").InnerText
                            let tagNodes = tagClassNode.SelectSingleNode(@"td[2]").SelectNodes(@"div/a")
                            tagNodes
                            |> Seq.map (fun tagNode -> tagClass, tagNode.InnerText))
                        |> Seq.concat
                    Some tags
        }

    member __.GetTags title =
        task {
            let! entryUrl = getFirstEntryUrl title
            match entryUrl with
            | None -> return None
            | Some url ->
                let! galleryTags = getGalleryTags url
                match galleryTags with
                | None -> return None
                | Some tags -> return Some tags
        }

    interface IDisposable with
        
        member __.Dispose() = 
            httpClient.Values |> Seq.iter (fun v -> v.Dispose())
            httpClient.Dispose()