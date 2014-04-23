namespace FSharp.Actor

open FSharp.Actor

type actorSelection = ActorSelection of actorRef list

module ActorSelection =

    let select (str:string) =
        let path = ActorPath.ofString str
        match ActorPath.system path with
        | Some(sys) ->
            match ActorSystem.TryGetSystem(sys) with
            | Some(sys) -> sys.Resolve(path)
            | None -> []
        | None -> 
            ActorSystem.Systems
            |> Seq.collect (fun x -> x.Resolve(path))
            |> Seq.toList
        |> ActorSelection


type actorSelection with
    static member (-->) (msg,ActorSelection(target)) = Seq.iter (fun t -> post t msg) target
    static member (<--) (ActorSelection(target),msg)  = Seq.iter (fun t -> post t msg) target
   
[<AutoOpen>]
module ActorSelectionOperations = 

    let inline (!!) str = ActorSelection.select str
            