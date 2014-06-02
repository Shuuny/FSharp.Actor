namespace FSharp.Actor.Remoting

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open Nessos.FsPickler

type ActorProtocol = 
    | Message of target:actorPath * sender:actorPath * payload:obj

type ITransport =
    abstract Scheme : string with get
    abstract BasePath : actorPath with get
    abstract Post : actorPath * actorPath * obj -> unit
    abstract Start : CancellationToken -> unit

type TCPTransport(config:TcpConfig<ActorProtocol>) = 
    let scheme = "actor.tcp"
    let basePath = ActorPath.ofString (sprintf "%s://%s/" scheme (config.ListenerEndpoint.ToString()))
    let mutable isStarted = false

    let listener =
        lazy
            let l = new TcpListener(config.ListenerEndpoint)
            l.Start(config.Backlog)
            l

    let rec messageHandler() = async {
            let! client = listener.Value.AcceptTcpClientAsync() |> Async.AwaitTask
            use client = client
            use stream = client.GetStream()
            let! (message:byte[]) = stream.ReadBytesAsync()
            match config.Deserialiser message with
            | Message(target, sender, payload) ->
                (ActorSelection.ofPath target).Post(payload, sender)
            return! messageHandler()
        }

    let publishAsync (NetAddress endpoint) (payload:ActorProtocol) = async {
            use client = new TcpClient()
            client.Connect(endpoint)
            use stream  = client.GetStream()
            do! stream.WriteBytesAsync(config.Serialiser payload)
            do! stream.FlushAsync().ContinueWith(ignore) |> Async.AwaitTask
        }
    
    interface ITransport with
        member x.Scheme with get() = scheme
        member x.BasePath with get() = basePath
        member x.Post(target, sender, payload) = 
            publishAsync (ActorPath.toNetAddress target) (Message(target, sender, payload)) 
            |> Async.StartImmediate

        member x.Start(ct) =
            if not isStarted
            then 
                Async.Start(messageHandler(), ct)

type RemoteActor(path:actorPath, transport:ITransport) =
    override x.ToString() = path.ToString()

    interface IActor with
        member x.Path with get() = path
        member x.Post(msg, sender) =
            transport.Post(path, ActorPath.rebase transport.BasePath sender, msg)
        member x.Dispose() = ()

type Beacon = Beacon of IPEndPoint

type ActorSystemProtocol =
    | NewActors of systemName:string * actorPath list

type RemotingEvents = 
    | NewRemoteSystem of endpoint:IPEndPoint
    | NewRemoteActor of path:actorPath

type RemoteableActorSystem(?name:string, ?port:int, ?transports:seq<ITransport>, ?eventStream:IEventStream, ?onError, ?token) as sys =
    inherit ActorSystem(?name = name, ?eventStream = eventStream, ?onError = onError) 
    let endpoint = new IPEndPoint(Net.getIPAddress(), defaultArg port (Net.getFirstFreePort()))
    let token = defaultArg token Async.DefaultCancellationToken
    
    let udp = new UDP<Beacon>(UdpConfig.Default(heartbeat = (5000, (fun _ -> Beacon endpoint))))
    let tcp = new TCP<ActorSystemProtocol>(TcpConfig.Default(endpoint))

    let listeningClients : Set<NetAddress> ref = ref Set.empty

    let transports = 
        defaultArg transports Seq.empty
        |> Seq.map (fun t -> t.Start(token); t.Scheme, t)
        |> Map.ofSeq

    let resolveTransport (path:actorPath) =
        path.Transport |> Option.bind (fun s -> Map.tryFind s transports) 

    do
        tcp.Recieved |> Event.add (fun (sender, msg) ->
            match msg with
            | NewActors(name, remotes) -> 
                remotes |> List.iter (fun path -> 
                    match resolveTransport path with
                    | Some(transport) -> 
                        let a = new RemoteActor(path, transport) :> IActor
                        sys.EventStream.Publish(NewRemoteActor(a.Path))
                        ActorRegistry.register(ActorRef a)
                    | None -> printfn "Unable to resolve transport for %A" path)
        )

        tcp.Start(token)

        udp.MessageRecieved 
        |> Event.add (fun (_,b) ->
                        match b with
                        | Beacon ep->
                            listeningClients := Set.add (NetAddress ep) !listeningClients
                            sys.EventStream.Publish(NewRemoteSystem(ep))
                            tcp.Publish(ep, NewActors(sys.Name, ActorRegistry.map ActorRef.path)))

        udp.Start(token)
    
    override x.ReportEvents() = 
       base.ReportEvents()
       x.EventStream.Subscribe(fun (evnt:RemotingEvents) -> x.Logger.DebugFormat(fun fmt -> fmt "RemoteEvent: %A" evnt))

    member x.SpawnActor(transport:string, actor:ActorConfiguration<_>) = 
        let actor = x.SpawnActor(actor)
        match Map.tryFind transport transports with
        | Some(transport) -> 
            let actorPath = ActorRef.path actor
            let remotePath = ActorPath.rebase transport.BasePath actorPath
            printfn "Publishing remote path %A %A %A" remotePath transport.BasePath actorPath
            for client in !listeningClients do
                tcp.Publish(client.Endpoint, NewActors(sys.Name, [remotePath]))
        | _ -> failwithf "Unknown transport %s" transport
            

[<AutoOpen>]
module ActorSystemExtensions =

    type ActorSystem with
        static member CreateRemoteable(?port, ?transports, ?name, ?eventStream, ?onError) = 
            let system = new RemoteableActorSystem(?name = name, ?port=port, ?transports=transports, ?eventStream = eventStream, ?onError = onError)
            ActorSystem.TryAddSystem(system) :?> RemoteableActorSystem
