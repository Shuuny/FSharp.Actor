namespace FSharp.Actor

open System

type ActorHostConfiguration = {
    mutable Transports : Map<string,ITransport>
    mutable Registry : ActorRegistry
    mutable EventStream : IEventStream
    mutable Logger : Log.Logger
}

module ActorHost = 
    
    let mutable private configuration = 
        
        {
            Logger = Log.Logger("ActorHost", Log.defaultFor Log.Debug) 
            Transports = Map.empty; 
            Registry = new InMemoryActorRegistry()
            EventStream = new DefaultEventStream(Log.defaultFor Log.Debug)
        }

    let configure f  : Unit = 
        f configuration

    let registerActor (actor : actorRef) = 
        configuration.Registry.Register actor

    let resolveActor path =
        configuration.Registry.Resolve path

    let resolveTransport transport = 
        configuration.Transports.TryFind transport

    let reportEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)

module Actor = 
    
    let create (config:ActorConfiguration<'a>) = 
        ActorRef(new Actor<_>(config) :> IActor)

    let link (actor:actorRef) (supervisor:actorRef) = 
        actor <-- SetParent(supervisor)

    let unlink (actor:actorRef) = 
        actor <-- SetParent(Null);

    let spawn (config:ActorConfiguration<'a>) = 
        create config
        |> ActorHost.registerActor