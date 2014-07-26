namespace FSharp.Actor

open System
open System.Net.Sockets
open System.Collections.Concurrent
open Nessos.FsPickler

module Remoting = 
    
    type InvalidMessageException(innerEx:Exception) =
        inherit Exception("Unable to handle msg", innerEx)

    type ActorProtocol = 
        | Resolve of actorPath
        | Resolved of actorPath list
        | Error of string * exn

    type Beacon =
        | Beacon of NetAddress

    type RemotableInMemoryActorRegistry(tcpConfig, udpConfig, ct) = 
        
        let registry = new InMemoryActorRegistry() :> ActorRegistry
        let messages = new ConcurrentDictionary<Guid, AsyncResultCell<ActorProtocol>>()
        let clients = new ConcurrentDictionary<string,NetAddress>()
        let pickler = new FsPickler()
        let tcpChannel : TCP = new TCP(tcpConfig)
        let udpChannel : UDP = new UDP(udpConfig)
        let logger = Log.Logger("RemoteRegistry", Log.defaultFor Log.Debug)

        let tcpHandler = (fun (address:NetAddress, messageId:TcpMessageId, payload:byte[]) -> 
            async { 
                try
                    let msg = pickler.UnPickle payload
                    match msg with
                    | Resolve(path) -> 
                        let transport = 
                            path.Transport |> Option.bind ActorHost.resolveTransport
                        let resolvedPaths = 
                            match transport with
                            | Some(t) -> 
                                registry.Resolve path
                                |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase t.BasePath)
                            | None ->
                                let tcp = ActorHost.resolveTransport "actor.tcp" |> Option.get
                                registry.Resolve path
                                |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase tcp.BasePath)
                        do! tcpChannel.PublishAsync(address.Endpoint, pickler.Pickle (Resolved resolvedPaths), messageId)
                    | Resolved _ as rs -> 
                        match messages.TryGetValue(messageId) with
                        | true, resultCell -> resultCell.Complete(rs)
                        | false , _ -> ()
                    | Error(msg, err) -> 
                        logger.Error(sprintf "Remote error received %s" msg, exn = err)
                with e -> 
                   let msg = sprintf "TCP: Unable to handle message : %s" e.Message
                   logger.Error(msg, exn = new InvalidMessageException(e))
                   do! tcpChannel.PublishAsync(address.Endpoint, pickler.Pickle (Error(msg,e)), messageId)
            }
        )

        let udpHandler = (fun (address:NetAddress, payload:byte[]) ->
            async {
                try
                    let msg = pickler.UnPickle payload
                    match msg with
                    | Beacon(netAddress) -> 
                        let key = netAddress.Endpoint.ToString()
                        if not <| clients.ContainsKey(key)
                        then 
                            clients.TryAdd(key, netAddress) |> ignore
                            logger.Debug(sprintf "New actor registry joined @ %A" netAddress)
                with e -> 
                   logger.Error("UDP: Unable to handle message", exn = new InvalidMessageException(e))  
            }
        )
        
        do
            let beaconBytes = pickler.Pickle(Beacon(NetAddress(tcpChannel.Endpoint)))
            tcpChannel.Start(tcpHandler, ct)
            udpChannel.Start(udpHandler, ct)
            udpChannel.Heartbeat(5000, (fun () -> beaconBytes), ct)
        
        let handledResolveResponse result =
             match result with
             | Some(Resolve _) -> failwith "Unexpected message"
             | Some(Resolved rs) ->                        
                 List.choose (fun path -> 
                     path.Transport 
                     |> Option.bind ActorHost.resolveTransport
                     |> Option.map (fun transport -> ActorRef(new RemoteActor(path, transport)))
                 ) rs
             | Some(Error(msg, err)) -> raise (new Exception(msg, err))
             | None -> failwith "Resolving Path %A timed out" path

        interface IRegistry<actorPath, actorRef> with
            member x.All with get() = registry.All
            
            member x.ResolveAsync(path, timeout) =
                 async {
                        let! remotePaths =
                             clients.Values
                             |> Seq.map (fun client -> async { 
                                             let msgId = Guid.NewGuid()
                                             let resultCell = new AsyncResultCell<ActorProtocol>()
                                             messages.TryAdd(msgId, resultCell) |> ignore
                                             do! tcpChannel.PublishAsync(client.Endpoint, pickler.Pickle(Resolve path), msgId)
                                             let! result = resultCell.AwaitResult(?timeout = timeout)
                                             messages.TryRemove(msgId) |> ignore
                                             return handledResolveResponse result
                                          })
                             |> Async.Parallel
                        let paths = remotePaths |> Array.toList |> List.concat
                        return paths @ registry.Resolve(path)     

                 }
                     
            member x.Resolve(path) = (x :> ActorRegistry).ResolveAsync(path, None) |> Async.RunSynchronously                   
            
            member x.Register(actor) = registry.Register actor
            
            member x.UnRegister actor = registry.UnRegister actor

    let enable(tcpConfig, udpConfig, transports, ct) = 
        let addTransport transports (transport:ITransport) =
            Map.add transport.Scheme transport transports
        ActorHost.configure (fun c -> 
            c.Transports <- Seq.fold (addTransport) Map.empty transports
            c.Registry <- (new RemotableInMemoryActorRegistry(tcpConfig, udpConfig, ct))
        )
            
            
type TestMessage =
    | SendToPing of string
    | KeepLocal of string

