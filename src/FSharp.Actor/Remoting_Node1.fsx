#load "FSharp.Actor.fsx"

open System.Net
open FSharp.Actor
open FSharp.Actor.Remoting


Remoting.enable (TcpConfig.Default(IPEndPoint.Create(6666)), 
                 UdpConfig.Default(), 
                 [new TCPTransport(TcpConfig.Default(IPEndPoint.Create(7001)))],
                  Async.DefaultCancellationToken)

ActorHost.reportEvents(fun (evnt:ActorEvents) -> printfn "Event: %A" evnt)
ActorHost.start()

let actor = 
    actor {
        path "ping"
        messageHandler (fun ctx -> 
            let log = ctx.Logger
            let rec loop () = async {
                let! msg = ctx.Receive()
                log.Info (sprintf "%s received from %A" msg.Message msg.Sender)
                return! loop()
            }
            loop()
        )
    } |> Actor.spawn

let localActor = !!"ping"
localActor <-- "Hello Local"


let dispatcher = !!"dispatcher"
dispatcher <-- SendToPing("Been to a remote node")