﻿namespace FSharp.Actor

open System
open System.Threading

[<AutoOpen>]
module Types = 
    
    type ActorPath = Uri

    type ILogger = 
        abstract Debug : string * exn option -> unit
        abstract Info : string * exn option -> unit
        abstract Warning : string * exn option -> unit
        abstract Error : string * exn option -> unit
    
    type ISerialiser =
        abstract Serialise : obj -> byte[]
        abstract Deserialise : byte[] -> obj
  
    type ActorStatus = 
        | Running
        | Shutdown of string
        | Disposed
        | Errored of exn
        | Restarting
        with
            member x.IsShutdownState() = 
                match x with
                | Shutdown(_) -> true
                | Disposed -> true
                | _ -> false

    type IReplyChannel<'a> =
        abstract Reply : 'a -> unit
    
    type SupervisorMessage = 
        | ActorErrored of exn * IActor

    
    and SystemMessage = 
        | Shutdown of string
        | Restart of string

    and ActorMessage<'a> = 
        | Message of 'a * IActor option

    and IActor =
         inherit IDisposable
         abstract Id : string with get
         abstract Path : ActorPath with get
         abstract Post : obj * IActor option -> unit
         abstract PostSystemMessage : SystemMessage * IActor option -> unit
         abstract Link : IActor -> unit
         abstract UnLink : IActor -> unit
         abstract Watch : IActor -> unit
         abstract UnWatch : unit -> unit
         abstract Status : ActorStatus with get
         abstract Children : seq<IActor> with get
         abstract QueueLength : int with get
         abstract Start : unit -> unit
         [<CLIEvent>] abstract PreStart : IEvent<IActor> with get
         [<CLIEvent>] abstract PreRestart :  IEvent<IActor> with get
         [<CLIEvent>] abstract PreStop :  IEvent<IActor> with get
         [<CLIEvent>] abstract OnStopped :  IEvent<IActor> with get
         [<CLIEvent>] abstract OnStarted :  IEvent<IActor> with get
         [<CLIEvent>] abstract OnRestarted :  IEvent<IActor> with get
    
    type IActor<'a> = 
        inherit IActor
        abstract Post : 'a * IActor option -> unit
        abstract Post : 'a -> unit
        abstract PostAndTryReply : (IReplyChannel<'b> -> 'a) * int option * IActor option -> 'b option
        abstract PostAndTryReply : (IReplyChannel<'b> -> 'a) * IActor option -> 'b option
        abstract PostAndTryAsyncReply : (IReplyChannel<'b> -> 'a) * int option * IActor option -> Async<'b option>
        abstract PostAndTryAsyncReply : (IReplyChannel<'b> -> 'a) * IActor option -> Async<'b option>
        abstract Receive : unit -> Async<'a * IActor option>
        abstract Receive : int option -> Async<'a * IActor option>
    
    type IRemoteActor<'a> =
        inherit IActor
        abstract Post : 'a * IActor option -> unit
        abstract Post : 'a -> unit

    type IMailbox<'a> = 
         inherit IDisposable
         abstract Receive : int option * CancellationToken -> Async<'a>
         abstract Post : 'a -> unit
         abstract Length : int with get
         abstract IsEmpty : bool with get
         abstract Restart : unit -> unit
    
    type ITransport =
        abstract Scheme : string with get
        abstract CreateRemoteActor : ActorPath -> IActor
        abstract Send : ActorPath * 'a * IActor option -> unit
        abstract SendSystemMessage : ActorPath * SystemMessage * IActor option -> unit

    type IEventStore = 
        abstract Store : string * 'a -> unit
        abstract GetLatest : string -> 'a option
        abstract Replay : string -> Async<seq<'a>>
        abstract ReplayFrom : string * DateTimeOffset -> Async<seq<'a>>