namespace FSharp.Actor

open System
open FSharp.Actor

type ActorSystemConfiguration = {
    Name : string
    Registry : IActorRegistry
    EventStream : IEventStream
}
with
    static member Default(?name, ?registry, ?eventStream) =
        let name = defaultArg name (Guid.NewGuid().ToString())
            
        {
            Name = name
            Registry = defaultArg registry (new LocalActorRegistry())
            EventStream = defaultArg eventStream (new DefaultEventStream())
        }

type ActorSystem(?config) = 
     
     let config = defaultArg config (ActorSystemConfiguration.Default())

     let systemSupervisor = 
         actor {
            path ("/" + config.Name)
            supervisorBehaviour (fun err -> err.Sender <-- Shutdown)
            raiseEventsOn config.EventStream
         }

     member x.actorOf(config:ActorConfiguration<_>) = 
        Actor.create 
