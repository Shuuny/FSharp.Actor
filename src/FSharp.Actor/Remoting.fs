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
    abstract BasePath : actorPath with get
    abstract Post : actorPath * actorPath * obj -> unit
    abstract Start : CancellationToken -> unit

type TCPTransport(config:TcpConfig<ActorProtocol>) = 
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
                (ActorSelection.ofPath target) <-- payload  
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
        member x.BasePath with get() = ActorPath.ofString (sprintf "actor.tcp://%s/" (config.ListenerEndpoint.ToString()))
        member x.Post(target, sender, payload) = 
            publishAsync (ActorPath.toNetAddress target) (Message(target, sender, payload)) 
            |> Async.StartImmediate

        member x.Start(ct) =
            if not isStarted
            then 
                Async.Start(messageHandler(), ct)

type RemoteActor(path:actorPath, transport:ITransport) =
    interface IActor with
        member x.Path with get() = path
        member x.Post(msg, sender) =
            transport.Post(path, ActorPath.rebase transport.BasePath (ActorRef.path sender), msg)
        member x.Dispose() = ()

type Beacon = Beacon of IPEndPoint

type RegistryProtocol =
    | NewActors of nodeName:string * actorPath list

type DiscoverableRegistry(name:string, ?port:int, ?localRegistry : IActorRegistry, ?token) = 
    let endpoint = new IPEndPoint(Net.getIPAddress(), defaultArg port (Net.getFirstFreePort()))
    let token = defaultArg token Async.DefaultCancellationToken
    
    let udp = new UDP<Beacon>(UdpConfig.Default(heartbeat = (5000, (fun _ -> Beacon endpoint))))
    let tcp = new TCP<RegistryProtocol>(TcpConfig.Default(endpoint))
    let listeningClients : Set<NetAddress> ref = ref Set.empty 

    let registry = defaultArg localRegistry (new LocalActorRegistry() :> IActorRegistry)
    let transport = new TCPTransport(TcpConfig.Default(new IPEndPoint(Net.getIPAddress(), 6667)))

    let resolveTransport (trnsport:string) = 
        transport

    do
        tcp.Recieved |> Event.add (fun (sender, msg) ->
            match msg with
            | NewActors(name, remotes) -> 
                remotes |> List.iter (fun path -> 
                    match (ActorPath.transport path) |> Option.map resolveTransport with
                    | Some(transport) -> 
                        let a = new RemoteActor(path, transport)
                        registry.Register(ActorRef a)
                    | None -> ())
        )

        tcp.Start(token)

        udp.MessageRecieved 
        |> Event.add (fun (_,b) ->
                        match b with
                        | Beacon ep->
                            listeningClients := Set.add (NetAddress ep) !listeningClients
                            tcp.Publish(ep, NewActors(name, registry.All |> List.map ActorRef.path))
        )

        udp.Start(token)
    
    interface IActorRegistry with
        member x.Resolve(path) = registry.Resolve(path)
        member x.Register(ref) = 
            !listeningClients |> Set.toList |> List.iter (fun x -> tcp.Publish(x.Endpoint, NewActors(name,[ActorRef.path ref])))
            registry.Register(ref)
        member x.UnRegister(ref) = registry.UnRegister(ref)
        member x.All with get() = registry.All

