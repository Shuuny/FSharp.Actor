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

    type Beacon =
        | Beacon of NetAddress

    type RemotableInMemoryActorRegistry(tcpConfig, udpConfig, ct) = 
        
        let registry = new InMemoryActorRegistry() :> ActorRegistry
        let messages = new ConcurrentDictionary<Guid, AsyncResultCell<ActorProtocol>>()
        let clients = new ConcurrentDictionary<NetAddress,Unit>()
        let pickler = new FsPickler()
        let tcpChannel : TCP = new TCP(tcpConfig)
        let udpChannel : UDP = new UDP(udpConfig)
        let logger = Log.Logger("RemoteRegistry", Log.defaultFor Log.Debug)

        let tcpHandler = (fun (address:NetAddress, messageId:TcpMessageId, payload:byte[]) -> 
            async { 
                try
                    match pickler.UnPickle payload with
                    | Resolve(path) -> 
                        let transport = 
                            path.Transport |> Option.bind ActorHost.configuration.Transports.TryFind
                        let resolvedPaths = 
                            match transport with
                            | Some(t) -> 
                                registry.Resolve path
                                |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase t.BasePath)
                            | None -> []
                        do! tcpChannel.PublishAsync(address.Endpoint,(pickler.Pickle (Resolved resolvedPaths)), messageId)
                    | Resolved _ as rs -> 
                        match messages.TryGetValue(messageId) with
                        | true, resultCell -> resultCell.Complete(rs)
                        | false , _ -> ()
                with e -> 
                   logger.Error("TCP: Unable to handle message", exn = new InvalidMessageException(e)) 
            }
        )

        let udpHandler = (fun (address:NetAddress, payload:byte[]) ->
            async {
                try
                    match pickler.UnPickle payload with
                    | Beacon(netAddress) -> 
                        if not <| clients.ContainsKey(netAddress)
                        then 
                            clients.TryAdd(netAddress, ()) |> ignore
                            logger.DebugFormat(fun f -> f "New actor registry found @ %A" netAddress)
                with e -> 
                   logger.Error("UDP: Unable to handle message", exn = new InvalidMessageException(e))  
            }
        )
        
        do
            tcpChannel.Start(tcpHandler, ct)
            udpChannel.Start(udpHandler, ct)
        
        let handledResolveResponse result =
             match result with
             | Some(Resolve _) -> failwith "Unexpected message"
             | Some(Resolved rs) ->                        
                 List.choose (fun path -> 
                     path.Transport 
                     |> Option.bind ActorHost.configuration.Transports.TryFind 
                     |> Option.map (Actor.remote path)
                 ) rs
             | None -> failwith "Resolving Path %A timed out" path

        interface IRegistry<actorPath, actorRef> with
            member x.All with get() = registry.All
            
            member x.ResolveAsync(path, timeout) =
                 async {
                        let! remotePaths =
                             clients.Keys
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

    let enable(tcpConfig, udpConfig, ct) = 
        ActorHost.configuration.Registry <- (new RemotableInMemoryActorRegistry(tcpConfig, udpConfig, ct))
            
            

