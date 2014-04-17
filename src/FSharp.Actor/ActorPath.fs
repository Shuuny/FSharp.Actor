namespace FSharp.Actor

open System
open System.Net
open System.Net.Sockets

module ActorPath = 
    
    let deadLetter : ActorPath = ActorPath(new Uri("/system/deadletter", UriKind.Relative))

    let name (ActorPath path) = path.PathAndQuery
               
    let transport (ActorPath path) = path.Scheme

    let toString (ActorPath path) = path.AbsoluteUri

    let components (ActorPath path) = path.Segments |> Array.toList

    let toIPEndpoint (ActorPath path) = 
        match Net.IPAddress.TryParse(path.Host) with
        | true, ip -> new IPEndPoint(ip, path.Port)
        | _ -> 
            match Dns.GetHostEntry(path.Host) with
            | null -> failwithf "Invalid Address %A expected address of form {IPAddress/hostname}:{Port} eg 127.0.0.1:8080" path
            | host -> 
                match host.AddressList |> Seq.tryFind (fun a -> a.AddressFamily = AddressFamily.InterNetwork) with
                | Some(ip) -> new IPEndPoint(ip, path.Port)
                | None -> failwithf "Unable to find ipV4 address for %s" path.Host

    let ofString (str:string) =
        match Uri.TryCreate(str, UriKind.RelativeOrAbsolute) with
        | true, uri -> ActorPath uri
        | false, _ -> failwithf "%s is not a valid actor path" str
    
    let ofRef = function
        | ActorRef(a) -> a.Path
        | Null -> deadLetter 

    let rebase (ActorPath basePath) (ActorPath path) = 
        let relativePart =
            if path.IsAbsoluteUri
            then path.PathAndQuery
            else path.ToString()
        if basePath.IsAbsoluteUri
        then
            match Uri.TryCreate(basePath, relativePart) with
            | true, uri -> ActorPath uri
            | false, _ -> failwithf "could not rebase actor path"
        else 
            ActorPath(new Uri(basePath.ToString().TrimEnd('/') + relativePart, UriKind.Relative))