#load "FSharp.Actor.fsx"

open FSharp.Actor
open FSharp.Actor.Remoting

let actorTransports = 
    [
        (new TCPTransport(TcpConfig.Default(6666)) :> ITransport)
    ]

let node1 = ActorSystem.CreateRemoteable(8081, actorTransports, name = "node1")
node1.ReportEvents()

let actor = 
    actor {
        path "ping"
        messageHandler (fun ctx -> 
            let log = ctx.Logger
            let rec loop () = async {
                let! msg = ctx.Receive()
                log.InfoFormat (fun fmt -> fmt "%s received from %A" msg.Message msg.Sender)
                return! loop()
            }
            loop()
        )
    }

node1.SpawnActor("actor.tcp", actor)

let localActor = !!"ping"
localActor <-- "Hello Local"

type Message =
    | SendToPing of string
    | KeepLocal of string

let dispatcher = !!"dispatcher"
dispatcher <-- SendToPing("Been to a remote node")