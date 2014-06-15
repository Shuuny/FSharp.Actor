#load "FSharp.Actor.fsx"

open FSharp.Actor
open FSharp.Actor.Remoting

let actorTransports = 
    [
        (new TCPTransport(TcpConfig.Default(6667)) :> ITransport)
    ]

let node2 = ActorSystem.CreateRemoteable(8080, actorTransports, name = "node2")
node2.ReportEvents()
   
type Message =
    | SendToPing of string
    | KeepLocal of string

let node2Actor =
    actor {
        path "dispatcher"
        messageHandler (fun ctx -> 
            let log = ctx.Logger
           
            let rec loop () = async {
                let! msg = ctx.Receive()
                printfn "Message: %A" msg
                match msg.Message with
                | SendToPing msg ->
                    let remoteActor = !!"ping" 
                    printfn "Sending to %A %s" remoteActor msg
                    remoteActor <-- msg
                | KeepLocal msg -> printfn "Recieved %s" msg 
                return! loop()
            }
            loop())
    }
    
node2.SpawnActor("actor.tcp", node2Actor)
!!"dispatcher" <-- SendToPing "Hello, from node 2"

let p = !!"ping"
