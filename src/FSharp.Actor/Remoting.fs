namespace FSharp.Actor.Remoting

open System
open FSharp.Actor

type ITransport =
    abstract BasePath : ActorPath with get
    abstract Scheme : string with get
    abstract Send : ActorPath * 'a * ActorPath -> unit

type RemoteActor<'a>(transport:ITransport, remotePath:ActorPath) = 
     
     interface IActor with
         member x.Path with get() = remotePath
         member x.Post(msg, sender) =
                transport.Send(remotePath,
                               msg, 
                               ActorPath.rebase transport.BasePath (ActorPath.ofRef sender))

     interface IActor<'a> with
         member x.Path with get() = remotePath
         member x.Post(msg:'a, sender) = 
             transport.Send(remotePath, 
                            msg, 
                            ActorPath.rebase transport.BasePath (ActorPath.ofRef sender))

     interface IDisposable with  
         member x.Dispose() = ()

module Actor = 
    
    let remote<'a> (transport:ITransport) (remotePath : ActorPath) = 
        (new RemoteActor<'a>(transport, remotePath) :> IActor) |> ActorRef
      