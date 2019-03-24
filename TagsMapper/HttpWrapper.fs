module HttpWrapper

open System
open System.Net.Http
open System.Threading
open System.Web

open FSharp.Control.Tasks
open HtmlAgilityPack

open Config

type HttpWrapper(config: Config) =

    let siteUrl = config.SiteUrl
    let cookie = config.Cookie

    let httpClient = new ThreadLocal<HttpClient>(fun () -> new HttpClient())

    let getSearchResultPage title =
        task {
            let client = httpClient.Value
            
            let searchUrl = sprintf "%s/?f_search=%s" siteUrl title |> HttpUtility.HtmlEncode
            use searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl)
            searchRequest.Headers.Add("Cookie", cookie)
            
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

            use galleryRequest = new HttpRequestMessage(HttpMethod.Get, url)
            galleryRequest.Headers.Add("Cookie", cookie)

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

            let tags =
                tagClassNodes
                |> Seq.map (fun tagClassNode ->
                    let tagClass = tagClassNode.SelectSingleNode(@"td[@class='tc']").InnerText
                    let tagNodes = tagClassNode.SelectSingleNode(@"td[2]").SelectNodes(@"div/a")
                    tagNodes
                    |> Seq.map (fun tagNode -> tagClass, tagNode.InnerText))
                |> Seq.concat

            return tags
        }

    member __.GetTags title =
        task {
            let! entryUrl = getFirstEntryUrl title
            return Option.map getGalleryTags entryUrl
        }

    interface IDisposable with
        
        member __.Dispose() = 
            httpClient.Values |> Seq.iter (fun v -> v.Dispose())
            httpClient.Dispose()