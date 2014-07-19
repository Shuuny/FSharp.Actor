namespace FSharp.Actor

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open Nessos.FsPickler


type TCPTransport(config:TcpConfig, ?logger) = 
    let scheme = "actor.tcp"
    let basePath = ActorPath.ofString (sprintf "%s://%s/" scheme (config.ListenerEndpoint.ToString()))
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(sprintf "actor.tcp://%A" config.ListenerEndpoint, log)
    let pickler = new FsPickler()

    let handler =(fun (address:NetAddress, msgId, payload) -> 
                  async {
                    let msg = pickler.UnPickle<Message>(payload)
                    !~msg.Target <-- msg.Message
                  })

    let tcp = new TCP(config) 
    

    interface ITransport with
        member x.Scheme with get() = scheme
        member x.BasePath with get() = basePath
        member x.Post(target, payload) = Async.Start(tcp.PublishAsync((ActorPath.toNetAddress target).Endpoint, pickler.Pickle payload))
        member x.Start(ct) = tcp.Start(handler, ct)