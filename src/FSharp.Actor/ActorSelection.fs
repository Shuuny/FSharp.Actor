namespace FSharp.Actor

open System
open FSharp.Actor

type actorSelection = 
    | ActorSelection of actorRef list
    with
       member x.Post(msg) =
            let (ActorSelection(target)) = x 
            List.iter (fun t -> post t msg) target
       member x.Post(msg, sender) =
            let (ActorSelection(target)) = x 
            List.iter (fun (t:actorRef) -> postWithSender t sender msg) target

module ActorSelection =
            
    let ofPath (path:actorPath) =
        match path.System with
        | Some(sys) -> ActorHost.resolveSystem sys 
        | None -> ActorHost.systems() 
        |> Seq.collect (fun x -> x.Resolve(path))
        |> Seq.toList
        |> ActorSelection

    let ofString (str:string) =
        ofPath <| ActorPath.ofString str
  
type actorSelection with
    static member (-->) (msg, ActorSelection(targets)) = Seq.iter (fun x -> post x msg) targets 
    static member (<--) (ActorSelection(targets), msg) = Seq.iter (fun x -> post x msg) targets

[<AutoOpen>]
module ActorSelectionOperators =
   let inline (!!) (path:string) = ActorSelection.ofString path           