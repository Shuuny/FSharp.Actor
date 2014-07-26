namespace FSharp.Actor

open System

type ActorHostConfiguration = {
    mutable Name : string
    mutable Transports : Map<string,ITransport>
    mutable Registry : ActorRegistry
    mutable EventStream : IEventStream
    mutable Logger : Log.Logger
    mutable OnError : (ErrorContext -> unit)
    mutable CancellationToken : Threading.CancellationToken
}

module ActorHost = 
    
    let mutable configuration =
        {
            Name = Guid.NewGuid().ToString()
            Logger = Log.Logger("ActorHost", Log.defaultFor Log.Debug) 
            Transports = Map.empty; 
            Registry = new InMemoryActorRegistry()
            EventStream = new DefaultEventStream(Log.defaultFor Log.Debug)
            CancellationToken = Async.DefaultCancellationToken
            OnError = (fun ctx -> ctx.Sender <-- Restart)
        }

    let registerActor (actor : actorRef) = 
        configuration.Registry.Register actor
        actor

    let supervisor = 
        (new Actor<_>(actor {
                            path ("/supervisors/" + (configuration.Name.TrimStart('/')))
                            supervisorStrategy configuration.OnError
                            raiseEventsOn configuration.EventStream
                         }) :> IActor)
        |> ActorRef
        |> registerActor

    let configure f  : Unit = 
        f configuration

    let resolveActor path =
        configuration.Registry.Resolve path

    let resolveTransport transport = 
        configuration.Transports.TryFind transport

    let reportEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)

    let start() = 
        Map.iter (fun _ (t:ITransport) -> t.Start(configuration.CancellationToken)) configuration.Transports
        configuration.Logger.Debug(sprintf "ActorHost started : ProcessId %d" (Diagnostics.Process.GetCurrentProcess().Id))

module Actor = 
    
    let link (actor:actorRef) (supervisor:actorRef) = 
        actor <-- SetParent(supervisor)

    let unlink (actor:actorRef) = 
        actor <-- SetParent(Null);

    let create (config:ActorConfiguration<'a>) =
        let config = { config with EventStream = Some(defaultArg config.EventStream (ActorHost.configuration.EventStream)) } 
        let actor = ActorRef(new Actor<_>(config) :> IActor)
        actor <-- SetParent(ActorHost.supervisor)
        actor

    let spawn (config:ActorConfiguration<'a>) = 
        create config
        |> ActorHost.registerActor