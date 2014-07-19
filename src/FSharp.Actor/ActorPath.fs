﻿namespace FSharp.Actor

open System
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open FSharp.Actor

type private actorPathComponent =
    | Transport of string
    | Host of string
    | Port of int
    | PathComponent of string[]

type actorPath = {
    Transport : string option
    Host : string option
    Port : int option
    HostType : UriHostNameType option
    Path : string[]
}
    with
        override x.ToString() =
            match x.Port with
            | Some(port) when port > -1 -> 
                sprintf "%s://%s:%d/%s" (defaultArg x.Transport "*") (defaultArg x.Host "*") port  (String.Join("/", x.Path))
            | _ -> 
                sprintf "%s://%s/%s" (defaultArg x.Transport "*") (defaultArg x.Host "*") (String.Join("/", x.Path))

        member x.IsAbsolute
                with get() =  
                    x.Transport.IsSome
                    && x.Host.IsSome

        static member internal Empty = 
            { 
                Transport = None
                Host = None 
                Port = None
                HostType = None 
                Path = [||] 
            }    
        static member internal Create(path:string, ?transport, ?system, ?host, ?port, ?hostType) = 
            { 
                Transport = Option.stringIsNoneIfBlank transport
                Host = Option.stringIsNoneIfBlank host 
                Port = port
                HostType = hostType 
                Path = path.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries); 
            }
        static member internal OfUri(uri:Uri) = 
            if uri.IsAbsoluteUri
            then actorPath.Create(uri.LocalPath, uri.Scheme, uri.UserInfo, uri.Host, uri.Port, uri.HostNameType)
            else actorPath.Create(uri.ToString())

        static member internal OfString(str:string) =
            let buildComponents (comp:string) = 
                if comp.EndsWith(":")
                then  [| Transport(comp.TrimEnd(':')) |]
                else
                    match comp.Split(':') with
                    | [| host; port |] -> [| Host(host); Port(Int32.Parse(port)) |]
                    | a -> [|a |> PathComponent|]

            let buildPath state comp =
                match comp with
                | Transport(trsn) when trsn <> "*" && (not trsn.IsEmpty) -> { state with Transport = (Some trsn) }
                | Host(host) when host <> "*" && (not host.IsEmpty) -> 
                    let hostType = Uri.CheckHostName(host)
                    { state with Host = (Some host); HostType = (Some hostType) }
                | Port(port) -> { state with Port = (Some port) }
                | PathComponent(path) -> { state with Path = Array.append state.Path path}
                | _ -> state
                 
            str.Split([|"/"|], StringSplitOptions.RemoveEmptyEntries)
            |> Array.collect buildComponents
            |> Array.fold buildPath actorPath.Empty

module ActorPath = 

    let private ipAddress family =
        if NetworkInterface.GetIsNetworkAvailable()
        then 
            let host = Dns.GetHostEntry(Environment.MachineName)
            host.AddressList
            |> Seq.tryFind (fun add -> add.AddressFamily = family)
        else None

    let ofString (str:string) = actorPath.OfString(str)

    let deadLetter = ofString "/deadletter"
                   
    let components (path:actorPath) = 
        (path.Path |> Array.toList)
        |> List.choose (function
            | "*" -> Some Trie.Wildcard
            | "/" -> None
            | a -> Some (Trie.Key(a.Trim('/')))
        )

    let toNetAddress (path:actorPath) = 
        match path.HostType, path.Host, path.Port with
        | Some(UriHostNameType.IPv4), Some(host), Some(port) -> NetAddress <| new IPEndPoint(IPAddress.Parse(host), port)
        | Some(UriHostNameType.Dns), Some(host), Some(port) -> 
            match Dns.GetHostEntry(host) with
            | null -> failwithf "Unable to resolve host for path %A" path
            | address -> 
                match address.AddressList |> Seq.tryFind (fun a -> a.AddressFamily = AddressFamily.InterNetwork) with
                | Some(ip) -> NetAddress <| new IPEndPoint(ip, port)
                | None -> failwithf "Unable to find ipV4 address for %s" host
        | a -> failwithf "A host name type of %A is not currently supported" a
    
    let rebase (basePath:actorPath) (path:actorPath) =
        if basePath.IsAbsolute
        then
            let basePort = basePath.Port
            { path with
                Transport = basePath.Transport
                Host = basePath.Host
                Port = basePort
                HostType = basePath.HostType
            }
        else path