namespace FSharp.Actor

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open FSharp.Actor
open System.Threading

type IActorSystem =
    abstract Name : string with get
    abstract SpawnActor : ActorConfiguration<_> -> actorRef
    abstract Resolve : actorPath -> actorRef list

type ActorSystem internal(?name, ?eventStream, ?onError, ?logger, ?registry) = 
       
    let name = defaultArg name (Guid.NewGuid().ToString())
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(name, log)
    let eventStream =  defaultArg eventStream (new DefaultEventStream(log) :> IEventStream)
    let registry = defaultArg registry (new InMemoryActorRegistry() :> ActorRegistry)
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

    static member Create(?name, ?eventStream, ?onError) = 
        let system = ActorSystem(?name = name, ?eventStream = eventStream, ?onError = onError)
        system :> IActorSystem

    interface IActorSystem with
        member x.Name with get() = name

        member x.SpawnActor(actor:ActorConfiguration<_>) =
           let actor = Actor.create { actor with Path = ActorPath.setSystem name actor.Path; EventStream = Some eventStream }
           registry.Register actor
           actor <-- SetParent(systemSupervisor)
           actor

        member x.Resolve(path) =
            registry.Resolve (ActorPath.setSystem name path)

type ActorSystemProtocol = 
    | Query of actorPath
    | QueryResponse of actorPath list

type RemoteActorSystem(name:string, endpoint:IPEndPoint, transportResolver, ?cancellationToken, ?logger) = 
    
    let listeningEndpoint = IPEndPoint(Net.getIPAddress(), Net.getFirstFreePort())
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
                    let transport = transportResolver p.Transport.Value
                    ActorRef(new RemoteActor(p, transport))
                )
            | _ -> []

type ActorSystemRegistry = IRegistry<string, IActorSystem>

type InMemoryActorSystemRegistry() =
    let syncObj = new ReaderWriterLockSlim()
    let systems = new ConcurrentDictionary<string,IActorSystem>()
    interface IRegistry<string, IActorSystem> with
        member x.All with get() = systems.Values |> Seq.toList
        member x.Resolve(path) = 
            match systems.TryGetValue(path) with
            | true, sys -> [sys]
            | _, _ -> []

        member x.Register(actorSystem) =
            let name = actorSystem.Name
            systems.AddOrUpdate(name, actorSystem,(fun _ _ -> actorSystem)) |> ignore

        member x.UnRegister(actorSystem) =
            systems.TryRemove(actorSystem.Name) |> ignore        
