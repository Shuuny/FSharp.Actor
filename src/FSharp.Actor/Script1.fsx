#load "FSharp.Actor.fsx"

open FSharp.Actor

let simpleActor = 
    actor {
        path "counter"
        messageHandler (fun ctx ->
            let rec loop (count:int) = async {
                let! msg = ctx.Receive()
                return! loop (msg.Message + count)
            }
            loop 0
        )
    } |> Actor.create