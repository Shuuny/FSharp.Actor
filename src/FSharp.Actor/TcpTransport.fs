namespace FSharp.Actor.Remoting

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open Nessos.FsPickler


type TCPTransport(config:TcpConfig<ActorProtocol>, handler:(ActorProtocol -> Async<unit>), ?logger) = 
    let scheme = "actor.tcp"
    let basePath = ActorPath.ofString (sprintf "%s://%s/" scheme (config.ListenerEndpoint.ToString()))
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(sprintf "actor.tcp://%A" config.ListenerEndpoint, log)
    let mutable isStarted = false

    let listener =
        lazy
            let l = new TcpListener(config.ListenerEndpoint)
            l.Start(config.Backlog)
            l

    let rec messageHandler() = 
        async {
            try
                let! client = listener.Value.AcceptTcpClientAsync() |> Async.AwaitTask
                use client = client
                use stream = client.GetStream()
                let! (message:byte[]) = stream.ReadBytesAsync()
                do! handler (config.Deserialiser message)
            with e -> 
                logger.Error("Error handling remote message", exn = e) 
                 
            return! messageHandler()
        }

    let publishAsync (NetAddress endpoint) (payload:ActorProtocol) = async {
            use client = new TcpClient()
            client.Connect(endpoint)
            use stream  = client.GetStream()
            do! stream.WriteBytesAsync(config.Serialiser payload)
            do! stream.FlushAsync().ContinueWith(ignore) |> Async.AwaitTask
        }
    
    interface ITransport with
        member x.Scheme with get() = scheme
        member x.BasePath with get() = basePath
        member x.Post(target, sender, payload) = 
            publishAsync (ActorPath.toNetAddress target) (Message(target, sender, payload)) 
            |> Async.StartImmediate

        member x.Start(ct) =
            if not isStarted
            then 
                Async.Start(messageHandler(), ct)