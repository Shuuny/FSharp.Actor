namespace FSharp.Actor

open System

type actorRef = 
    | ActorRef of IActor
    | Null

and IActor = 
    inherit IDisposable
    abstract Path : actorPath with get
    abstract Post : obj * actorRef -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Path : actorPath with get
    abstract Post : 'a * actorRef -> unit

[<AutoOpen>]
module ActorRef = 
    
    open System.Runtime.Remoting.Messaging

    let path = function
        | ActorRef(a) -> a.Path
        | Null -> ActorPath.deadLetter

    let internal getActorContext() = 
        match CallContext.LogicalGetData("actor") with
        | null -> None
        | :? actorRef as a -> Some a
        | _ -> failwith "Unexpected type representing actorContext" 

    let sender() = 
        match getActorContext() with
        | None -> Null
        | Some ref -> ref
 
    let post (target:actorRef) (msg:'a) = 
        match target with
        | ActorRef(actor) -> 
            actor.Post(msg,sender())
        | Null -> ()


    let inline (<-!) target msg = Seq.iter (fun t -> post t msg) target
    let inline (!->) msg target = Seq.iter (fun t -> post t msg) target
    let inline (-->) msg target = post target msg
    let inline (<--) target msg  = post target msg 







