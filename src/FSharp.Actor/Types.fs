namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif

type ActorPath = string

type IMailbox<'a> = 
    inherit IDisposable
    abstract Post : 'a -> unit
    abstract Scan : int * ('a -> Async<'b> option) -> Async<'b>
    abstract Receive : int -> Async<'a>

type ILogger = 
    abstract Debug : string * obj[] * exn option -> unit
    abstract Info : string * obj[]  * exn option -> unit
    abstract Warning : string * obj[] * exn option -> unit
    abstract Error : string * obj[] * exn option -> unit

type Event = {
    Payload : obj
    PayloadType : string
}

type IEventStream = 
    inherit IDisposable
    abstract Publish : 'a -> unit
    abstract Publish : string * 'a -> unit
    abstract Subscribe<'a> : ('a -> unit) -> unit
    abstract Subscribe : string * (Event -> unit) -> unit
    abstract Unsubscribe<'a> : unit -> unit
    abstract Unsubscribe : string -> unit

type ActorRef = 
    | ActorRef of IActor
    | Null

and Message<'a> = {
    Sender : ActorRef
    Target : ActorRef
    Message : 'a
}

and IActor = 
    inherit IDisposable
    abstract Name : ActorPath with get
    abstract Post : obj * ActorRef -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Name : ActorPath with get
    abstract Post : 'a * ActorRef -> unit

type SupervisorMessage = 
    | Errored of exn

type ActorEvents = 
    | ActorStarted of ActorRef
    | ActorShutdown of ActorRef
    | ActorRestart of ActorRef
    | ActorErrored of ActorRef * exn
    | ActorAddedChild of ActorRef * ActorRef
    | ActorRemovedChild of ActorRef * ActorRef

type MessageEvents = 
    | Undeliverable of obj * Type * Type * ActorRef option 

type ActorStatus = 
    | Running 
    | Errored of exn
    | Stopped

type SystemMessage =
    | Shutdown
    | Restart
    | Link of ActorRef
    | Unlink of ActorRef
    | SetSupervisor of ActorRef

type ActorContext<'a> = {
    Logger : ILogger
    Children : ActorRef list
    Mailbox : IMailbox<Message<'a>>
    Self : ActorRef
}
with 
    member x.Receive(?timeout) = 
        async { return! x.Mailbox.Receive(defaultArg timeout Timeout.Infinite) }
    member x.Scan(f, ?timeout) = 
        async { return! x.Mailbox.Scan(defaultArg timeout Timeout.Infinite, f) }

type ActorConfiguration<'a> = {
    Path : ActorPath
    EventStream : IEventStream option
    Supervisor : ActorRef
    Behaviour : (ActorContext<'a> -> Async<unit>)
}

type ErrorContext = {
    Error : exn
    Children : ActorRef list
}





