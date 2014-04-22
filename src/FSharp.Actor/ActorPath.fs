namespace FSharp.Actor

open System
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets

type actorPath = ActorPath of Uri
    with
        override x.ToString() = 
            let (ActorPath path) = x
            path.ToString()

module ActorPath = 
    
    type Locality = 
        | Local
        | Remote
        | Unknown

    let inline private hostName() = Environment.MachineName

    let private ipAddress family =
        if NetworkInterface.GetIsNetworkAvailable()
        then 
            let host = Dns.GetHostEntry(hostName())
            host.AddressList
            |> Seq.tryFind (fun add -> add.AddressFamily = family)
        else None

    let deadLetter : actorPath = ActorPath(new Uri("/deadletter", UriKind.Relative))

    let locality (ActorPath path)= 
        if path.IsAbsoluteUri
        then
            match path.HostNameType with
            | UriHostNameType.Dns when hostName() = path.Host -> Local
            | UriHostNameType.IPv4 when path.Host = ((ipAddress AddressFamily.InterNetwork).ToString()) -> Local
            | UriHostNameType.IPv6 when path.Host = ((ipAddress AddressFamily.InterNetworkV6).ToString()) -> Local
            | _ -> Remote
        else Unknown

    let ofString (str:string) =
        match Uri.TryCreate("/" + str.TrimStart('/'), UriKind.RelativeOrAbsolute) with
        | true, uri -> 
            if uri.IsAbsoluteUri
            then ActorPath uri
            else ActorPath (new Uri(new Uri(sprintf "actor://%s" (hostName())),uri))
        | false, _ -> failwithf "%s is not a valid actor path" str

    let name (ActorPath path) = path.LocalPath

    let internal setSystem system (ActorPath path) =
        if path.IsAbsoluteUri
        then
            (new Uri(path.Scheme + "://"
                        + system + "@"
                        + path.Host + (if path.Port <> -1 then (":" + string(path.Port)) else "") + "/"
                        + path.LocalPath.TrimStart('/'))) |> ActorPath
        else failwithf "Cannot set the system on a relative path"
               
    let transport (ActorPath path) = 
        if path.IsAbsoluteUri 
        then Some path.Scheme
        else None
    
    let system (ActorPath path) = 
        if path.IsAbsoluteUri 
            && not(String.IsNullOrEmpty(path.UserInfo)) 
            && not(String.IsNullOrWhiteSpace(path.UserInfo))
        then Some path.UserInfo
        else None

    let toString (ActorPath path) = path.AbsoluteUri

    let components (ActorPath path) = path.Segments |> Array.toList

    let toIPEndpoint (ActorPath path) = 
        match path.HostNameType with
        | UriHostNameType.IPv4-> new IPEndPoint(IPAddress.Parse(path.Host), path.Port)
        | UriHostNameType.Dns -> 
            match Dns.GetHostEntry(path.Host) with
            | null -> failwithf "Unable to resolve host for path %A" path
            | host -> 
                match host.AddressList |> Seq.tryFind (fun a -> a.AddressFamily = AddressFamily.InterNetwork) with
                | Some(ip) -> new IPEndPoint(ip, path.Port)
                | None -> failwithf "Unable to find ipV4 address for %s" path.Host
        | a -> failwithf "A host name type of %A is not currently supported" a
    
    let rebase (ActorPath basePath) (ActorPath path) = 
        let relativePart =
            if path.IsAbsoluteUri
            then path.LocalPath
            else path.ToString()
        if basePath.IsAbsoluteUri
        then
            match Uri.TryCreate(basePath, relativePart) with
            | true, uri -> ActorPath uri
            | false, _ -> failwithf "could not rebase actor path"
        else 
            ofString (basePath.ToString().TrimEnd('/') + "/" + (relativePart.TrimStart('/')))