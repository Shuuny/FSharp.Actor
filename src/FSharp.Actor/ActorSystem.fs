namespace FSharp.Actor

open System
open System.Collections.Concurrent
open FSharp.Actor

type ActorSystem internal(?name, ?eventStream, ?onError, ?logger) = 
   
    let name = defaultArg name (Guid.NewGuid().ToString())
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(name, log)
    let eventStream =  defaultArg eventStream (new DefaultEventStream(log) :> IEventStream)
    let onError = defaultArg onError (fun err -> err.Sender <-- Shutdown)

    static let registeredSystems = new ConcurrentDictionary<string, ActorSystem>()

    let systemSupervisor = 
        actor {
           path ("/supervisors/" + (name.TrimStart('/')))
           supervisorStrategy onError
           raiseEventsOn eventStream
        } |> Actor.create
    
    member x.Name with get() = name
    member x.Logger with get() = logger
    member x.EventStream with get() = eventStream

    abstract ReportEvents : unit -> unit
    default x.ReportEvents() =
        eventStream.Subscribe(fun (evnt:ActorEvents) -> logger.DebugFormat(fun fmt -> fmt "ActorEvent: %A" evnt))
    
    member x.SpawnActor(actor:ActorConfiguration<_>) =
       let actor = Actor.create { actor with Path = ActorPath.setSystem name actor.Path; EventStream = Some eventStream }
       ActorRegistry.register actor
       actor <-- SetParent(systemSupervisor)
       actor

    member x.Resolve(path) =
        ActorRegistry.resolve (ActorPath.setSystem name path)
    
    static member Create(?name, ?eventStream, ?onError) = 
        let system = ActorSystem(?name = name, ?eventStream = eventStream, ?onError = onError)
        ActorSystem.TryAddSystem(system) 
    
    static member internal TryAddSystem(system:ActorSystem) = 
        if not <| registeredSystems.TryAdd(system.Name, system)
        then failwithf "Failed to create actor system a system with the same name already exists"
        else system

    static member internal TryGetSystem(systemName:string) = 
        match registeredSystems.TryGetValue(systemName) with
        | true, sys -> Some sys
        | false, _ -> None

    static member internal Systems
        with get() = registeredSystems.Values
        
