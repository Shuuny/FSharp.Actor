namespace FSharp.Actor

open System
open System.Threading
open System.Runtime.Remoting.Messaging

#if INTERACTIVE
open FSharp.Actor
#endif


[<AutoOpen>]
module Pervasives = 
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
 
    let resolve path = Registry.resolve path

    let register (actor:IActor) = actor |> Registry.register

    let inline (!!) path = resolve path
    let inline (<--) target msg = Seq.iter (fun t -> post t msg) target
    let inline (-->) msg target = Seq.iter (fun t -> post t msg) target