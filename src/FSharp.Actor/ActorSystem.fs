namespace FSharp.Actor

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open FSharp.Actor

type IActorSystem =
    abstract Name : string with get
    abstract SpawnActor : ActorConfiguration<_> -> actorRef
    abstract Resolve : actorPath -> actorRef list

type ActorSystem internal(?name, ?eventStream, ?onError, ?logger) = 
   
    let name = defaultArg name (Guid.NewGuid().ToString())
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(name, log)
    let eventStream =  defaultArg eventStream (new DefaultEventStream(log) :> IEventStream)
    let onError = defaultArg onError (fun err -> err.Sender <-- Shutdown)
    static let registeredSystems = new ConcurrentDictionary<string, IActorSystem>()

    do
        eventStream.Subscribe(fun (evnt:ActorEvents) -> logger.DebugFormat(fun fmt -> fmt "ActorEvent: %A" evnt))

    let systemSupervisor = 
        actor {
           path ("/supervisors/" + (name.TrimStart('/')))
           supervisorStrategy onError
           raiseEventsOn eventStream
        } |> Actor.create

    member x.Logger with get() = logger
    member x.EventStream with get() = eventStream
    
    static member TryAddSystem(system:IActorSystem) = 
        if not <| registeredSystems.TryAdd(system.Name, system)
        then failwithf "Failed to create actor system a system with the same name already exists"
        else system

    static member TryGetSystem(systemName:string) = 
        match registeredSystems.TryGetValue(systemName) with
        | true, sys -> Some sys
        | false, _ -> None

    static member Systems
        with get() = registeredSystems.Values

    static member Create(?name, ?eventStream, ?onError) = 
        let system = ActorSystem(?name = name, ?eventStream = eventStream, ?onError = onError)
        ActorSystem.TryAddSystem(system)

    interface IActorSystem with
        member x.Name with get() = name

        member x.SpawnActor(actor:ActorConfiguration<_>) =
           let actor = Actor.create { actor with Path = ActorPath.setSystem name actor.Path; EventStream = Some eventStream }
           ActorRegistry.register actor
           actor <-- SetParent(systemSupervisor)
           actor

        member x.Resolve(path) =
            ActorRegistry.resolve (ActorPath.setSystem name path)

type ActorSystemProtocol = 
    | Query of actorPath
    | QueryResponse of actorPath list

type RemoteActorSystem(name:string, listeningEndpoint:IPEndPoint, endpoint:IPEndPoint, ?cancellationToken, ?logger) = 
    
    let channel = new TCP<ActorSystemProtocol>(TcpConfig.Default(listeningEndpoint))

    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(name, log)

    interface IActorSystem with
        member x.Name with get() = name
        member x.SpawnActor(cfg) = raise(NotImplementedException("yet!"))
        member x.Resolve(path:actorPath) = 
            match channel.TryPostAndReply(endpoint, Query(path)) with
            | Some(QueryResponse(reply)) -> 
                reply
                |> List.map (fun p ->
                    let transport = ActorHost.resolveTransport p.Transport.Value
                    ActorRef(new RemoteActor(p, transport))
                )
            | _ -> []
        
