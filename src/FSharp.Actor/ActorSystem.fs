namespace FSharp.Actor

open System
open System.Collections.Concurrent
open FSharp.Actor


type ActorSystem internal(?name, ?registry, ?eventStream, ?onError) = 

    let name = defaultArg name (Guid.NewGuid().ToString())
    let eventStream =  defaultArg eventStream (new DefaultEventStream() :> IEventStream)
    let registry = defaultArg registry (new LocalActorRegistry() :> IActorRegistry)
    let onError = defaultArg onError (fun err -> err.Sender <-- Shutdown)

    static let registeredSystems = new ConcurrentDictionary<string, ActorSystem>()
    let systemSupervisor = 
        actor {
           path ("/supervisors/" + (name.TrimStart('/')))
           supervisorStrategy onError
           raiseEventsOn eventStream
        } |> Actor.create
    
    member internal x.Name with get() = name
    
    member x.SpawnActor(actor:ActorConfiguration<_>) =
       let actor = Actor.create { actor with Path = ActorPath.setSystem name actor.Path; EventStream = Some eventStream }
       actor <-- SetParent(systemSupervisor)
       actor

    member x.Resolve(path:ActorPath) = registry.Resolve path
    
    static member Create(?name, ?registry, ?eventStream, ?onError) = 
        let system = ActorSystem(?name = name, ?registry = registry, ?eventStream = eventStream, ?onError = onError)
        if not <| registeredSystems.TryAdd(system.Name, system)
        then failwithf "Failed to create actor system a system with the same name already exists"
        else system 

    static member internal TryGetSystem(systemName:string) = 
        match registeredSystems.TryGetValue(systemName) with
        | true, sys -> Some sys
        | false, _ -> None

    static member internal Systems
        with get() = registeredSystems.Values
        
