namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Runtime.Remoting.Messaging
open FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif
     
[<AutoOpen>]
module ActorOperations = 

    let sender() = 
        let sender = CallContext.LogicalGetData("actor")
        match sender with
        | null -> Null
        | :? IActor as actor -> ActorRef(actor)
        | _ -> failwithf "Unknown sender ref %A" sender
 
    let post (target:ActorRef) (msg:'a) = 
        match target with
        | ActorRef(actor) -> 
            actor.Post(msg,sender())
        | Null -> ()

    let inline (<-!) target msg = Seq.iter (fun t -> post t msg) target
    let inline (!->) msg target = Seq.iter (fun t -> post t msg) target
    let inline (-->) msg target = post msg target
    let inline (<--) msg target = post msg target

[<AutoOpen>]
module ActorConfiguration = 
    
    let internal emptyBehaviour ctx = 
        let rec loop() =
             async { return! loop() }
        loop()

    type ActorConfigurationBuilder internal() = 
        member x.Zero() = { 
            Path = ActorPath.ofString (Guid.NewGuid().ToString()); 
            EventStream = None
            SupervisorBehaviour = (fun x -> x.Sender <-- Shutdown);
            Parent = Null;
            Children = []; 
            Behaviour = emptyBehaviour;
            Mailbox = None  }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorConfiguration<'a>, b:ActorConfiguration<_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorConfiguration<'a>, name) = 
            {ctx with Path = ActorPath.ofString name }
        [<CustomOperation("mailbox", MaintainsVariableSpace = true)>]
        member x.Mailbox(ctx:ActorConfiguration<'a>, mailbox) = 
            {ctx with Mailbox = mailbox }
        [<CustomOperation("messageHandler", MaintainsVariableSpace = true)>]
        member x.MsgHandler(ctx:ActorConfiguration<'a>, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("parent", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorConfiguration<'a>, sup) = 
            { ctx with Parent = sup }
        [<CustomOperation("children", MaintainsVariableSpace = true)>]
        member x.Children(ctx:ActorConfiguration<'a>, children) = 
            { ctx with Children = children }
        [<CustomOperation("supervisorBehaviour", MaintainsVariableSpace = true)>]
        member x.SupervisorBehaviour(ctx:ActorConfiguration<'a>, supervisorBehaviour) = 
            { ctx with SupervisorBehaviour = supervisorBehaviour }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member x.RaiseEventsOn(ctx:ActorConfiguration<'a>, es) = 
            { ctx with EventStream = Some es }

    let actor = new ActorConfigurationBuilder()
                
type Actor<'a>(defn:ActorConfiguration<'a>) as self = 
    let mailbox = defaultArg defn.Mailbox (new DefaultMailbox<Message<'a>>() :> IMailbox<_>)
    let logger = Logger.create (defn.Path.ToString())
    let systemMailbox = new DefaultMailbox<SystemMessage>() :> IMailbox<_>
    let mutable cts = new CancellationTokenSource()
    let mutable messageHandlerCancel = new CancellationTokenSource()
    let mutable defn = defn
    let mutable ctx = { Self = ActorRef(self); Mailbox = mailbox; Logger = logger; Children = defn.Children; }
    let mutable status = ActorStatus.Stopped

    let publishEvent event = 
        Option.iter (fun (es:IEventStream) -> es.Publish(event)) defn.EventStream

    let setStatus stats = 
        status <- stats

    let shutdown() = 
        async {
            messageHandlerCancel.Cancel()
            publishEvent(ActorEvents.ActorShutdown(ctx.Self))
            match status with
            | ActorStatus.Errored(err) -> logger.Debug("shutdown due to Error: {1}",[|err|], None)
            | _ -> logger.Debug("{0} shutdown",[|self|], None)
            setStatus ActorStatus.Stopped
            return ()
        }

    let handleError (err:exn) =
        async {
            setStatus(ActorStatus.Errored(err))
            publishEvent(ActorEvents.ActorErrored(ctx.Self, err))
            match defn.Parent with
            | ActorRef(actor) -> 
                actor.Post(Errored({ Error = err; Sender = ctx.Self; Children = ctx.Children }),ctx.Self)
                return ()
            | Null -> return! shutdown() 
        }

    let rec messageHandler() =
        setStatus ActorStatus.Running
        async {
            try
                do! defn.Behaviour ctx
            with e -> 
                do! handleError e
        }

    let rec restart includeChildren =
        async { 
            publishEvent(ActorEvents.ActorRestart(ctx.Self))
            do messageHandlerCancel.Cancel()

            if includeChildren
            then ctx.Children <-! RestartTree

            match status with
            | ActorStatus.Errored(err) -> logger.Debug("restarted due to Error: {1}",[|err|], None)
            | _ -> logger.Debug("restarted",[||], None)
            do start()
            return! systemMessageHandler()
        }

    and systemMessageHandler() = 
        async {
            let! sysMsg = systemMailbox.Receive(Timeout.Infinite)
            match sysMsg with
            | Shutdown -> return! shutdown()
            | Restart -> return! restart false
            | RestartTree -> return! restart true
            | Errored(errContext) -> 
                defn.SupervisorBehaviour(errContext) 
                return! systemMessageHandler()
            | Link(ref) -> 
                ctx <- { ctx with Children = (ref :: ctx.Children) }
                return! systemMessageHandler()
            | Unlink(ref) -> 
                ctx <- { ctx with Children = (List.filter ((<>) ref) ctx.Children) }
                return! systemMessageHandler()
            | SetParent(ref) ->
               match ref, defn.Parent with
               | Null, Null -> ()
               | ActorRef(a), ActorRef(a') when a' = a -> ()
               | Null, _ -> 
                    defn.Parent <-- Unlink(ctx.Self)
                    defn <- { defn with Parent =  ref }
               | _, Null -> 
                    defn.Parent <-- Link(ctx.Self)
                    defn <- { defn with Parent =  ref }
               | ActorRef(a), ActorRef(a') ->
                    defn.Parent <-- Unlink(ctx.Self)
                    ref <-- Link(ctx.Self)
                    defn <- { defn with Parent =  ref }
               return! systemMessageHandler()
        }

    and start() = 
        if messageHandlerCancel <> null
        then
            messageHandlerCancel.Dispose()
            messageHandlerCancel <- null
        messageHandlerCancel <- new CancellationTokenSource()
        Async.Start(async {
                        CallContext.LogicalSetData("actor", self :> IActor)
                        publishEvent(ActorEvents.ActorStarted(ctx.Self))
                        do! messageHandler()
                    }, messageHandlerCancel.Token)

    do 
        Async.Start(systemMessageHandler(), cts.Token)
        start()
   
    override x.ToString() = ActorPath.toString defn.Path

    interface IActor with
        member x.Path with get() = defn.Path
        member x.Post(msg, sender) =
               match msg with
               | :? SystemMessage as msg -> systemMailbox.Post(msg)
               | msg -> mailbox.Post({Target = ActorRef(x); Sender = sender; Message = unbox<'a> msg})

    interface IActor<'a> with
        member x.Path with get() = defn.Path
        member x.Post(msg:'a, sender) =
             mailbox.Post({Target = ActorRef(x); Sender = sender; Message = msg}) 

    interface IDisposable with  
        member x.Dispose() =
            messageHandlerCancel.Dispose()
            cts.Dispose()

module Actor = 
    
    let create (config:ActorConfiguration<'a>) = 
        ActorRef(new Actor<_>(config) :> IActor)

    let link (actor:ActorRef) (supervisor:ActorRef) = 
        actor <-- SetParent(supervisor)

    let unlink (actor:ActorRef) = 
        actor <-- SetParent(Null);
