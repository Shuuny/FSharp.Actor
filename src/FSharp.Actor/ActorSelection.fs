namespace FSharp.Actor

open FSharp.Actor

type actorSelection = 
    | ActorSelection of actorRef list
    with
       member x.Post(msg) =
            let (ActorSelection(target)) = x 
            Seq.iter (fun t -> post t msg) target

module ActorSelection =
    
    let ofPath (path:actorPath) =
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

    let ofString (str:string) =
        ofPath <| ActorPath.ofString str
  
type actorSelection with
    static member (-->) (msg, ActorSelection(targets)) = Seq.iter (fun x -> post x msg) targets 
    static member (<--) (ActorSelection(targets), msg) = Seq.iter (fun x -> post x msg) targets

[<AutoOpen>]
module ActorSelectionOperators =
   let inline (!!) (path:string) = ActorSelection.ofString path           