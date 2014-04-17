namespace FSharp.Actor.Fracture

open System
open System.Net
open FSharp.Actor
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open Nessos.FsPickler
open Fracture
open Fracture.Common

type FractureMessage = {
    Sender : ActorPath
    Target : ActorPath
    Body : obj
}

type FractureTransport(listenPort:int) = 
    let serialiser = new FsPickler()
    let log = Logger.create "fracture-transport"
    let basePath = 
        ActorPath.ofString (sprintf "actor.fracture://%s:%d" (Net.IPAddress.ToString()) listenPort)

    let tryUnpackFractureMessage (msg) =
        try
            Choice1Of2 (serialiser.UnPickle<FractureMessage> msg)
        with e ->
            Choice2Of2 e
        
    let onReceived(msg:byte[], server:TcpServer, socketDescriptor:SocketDescriptor) =
        if msg <> null then
            match tryUnpackFractureMessage msg with
            | Choice1Of2(message) ->
                (resolve message.Target) <-- message.Body
            | Choice2Of2(e) ->
                log.Warning("Error deserializing fracture message: {0}", [|msg.ToString()|], Some e)
    do
        try
            let l = Fracture.TcpServer.Create(onReceived)
            l.Listen(IPAddress.Any, listenPort)
            log.Debug("listening on %d",[|listenPort|], None)
        with e -> 
            log.Error("Failed to create listener on port {0}",[|listenPort|], Some e)
            reraise()

    let clients = new ConcurrentDictionary<ActorPath, Fracture.TcpClient>()

    let tryResolve (address:ActorPath) (dict:ConcurrentDictionary<_,_>) = 
        match dict.TryGetValue(address) with
        | true, v -> Some v
        | _ -> None

    let getOrAdd (ctor:ActorPath -> Async<_>) (address:ActorPath) (dict:ConcurrentDictionary<ActorPath,_>) =
        async {
            match dict.TryGetValue(address) with
            | true, v -> return Choice1Of2 v
            | _ -> 
                let! instance = ctor address
                match instance with
                | Choice1Of2 v -> return Choice1Of2 (dict.AddOrUpdate(address, v, (fun address _ -> v)))
                | Choice2Of2 e -> return Choice2Of2 e 
        }

    let remove (address:ActorPath) (dict:ConcurrentDictionary<ActorPath,'a>) = 
        dict.TryRemove(address) |>ignore

    let tryCreateClient (address:ActorPath) =
        async {
            try
                let endpoint = ActorPath.toIPEndpoint address
                let connWaitHandle = new AutoResetEvent(false)
                let client = new Fracture.TcpClient()
                client.Connected |> Observable.add(fun x -> log.Debug("Client connected on {0}",[|x|], None); connWaitHandle.Set() |> ignore) 
                client.Disconnected |> Observable.add (fun x -> log.Debug("Client disconnected on %A",[|x|], None); remove address clients)
                client.Start(endpoint)
                let! connected = Async.AwaitWaitHandle(connWaitHandle, 10000)
                if not <| connected 
                then return Choice2Of2(TimeoutException() :> exn)
                else return Choice1Of2 client
            with e ->
                return Choice2Of2 e
        }

    interface ITransport with
        member val BasePath = basePath with get
        member val Scheme = "actor.fracture" with get
        member x.Send(remoteAddress, msg, sender) =
             async {
                let! client = getOrAdd tryCreateClient remoteAddress clients
                match client with
                | Choice1Of2(client) ->
                    let rm = { Target = remoteAddress; Sender = sender ; Body = msg } 
                    let bytes = serialiser.Pickle rm
                    client.Send(bytes, true)
                | Choice2Of2 e -> log.Error("Failed to create client for send {0}",[|remoteAddress|], Some e)
             } |> Async.Start

