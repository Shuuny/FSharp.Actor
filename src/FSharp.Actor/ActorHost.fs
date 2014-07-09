namespace FSharp.Actor

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent

type ActorHostRemotingConfiguration = {
    Transports : Map<string, ITransport>
}
with 
    static member internal Default(?transports, ?hostDiscovered) = 
        {
            Transports = (defaultArg transports Seq.empty) |> Seq.map (fun (t:ITransport) -> t.Scheme, t) |> Map.ofSeq
        }

type ActorHostConfiguration =
    {
        mutable CancellationToken : CancellationToken
        mutable SystemRegistry : ActorSystemRegistry
        mutable Remoting : ActorHostRemotingConfiguration
    }
    with
        static member internal Default(?systemRegistry, ?remoting, ?token) =
            {
                CancellationToken = defaultArg token Async.DefaultCancellationToken
                SystemRegistry = defaultArg systemRegistry (new InMemoryActorSystemRegistry() :> ActorSystemRegistry)
                Remoting = defaultArg remoting (ActorHostRemotingConfiguration.Default())
            }

module ActorHost = 
    
    exception UnknownTransport of string
   
    let private configuration = ActorHostConfiguration.Default() 

    let configure(cfgF : ActorHostConfiguration -> unit) =
        cfgF configuration

    let start() = 
        configuration.Remoting.Transports
        |> Map.toSeq
        |> Seq.iter (fun (_,t) -> t.Start(configuration.CancellationToken))
                
    let tryResolveTransport (scheme:string) = configuration.Remoting.Transports.TryFind scheme
    
    let resolveTransport scheme = 
        match tryResolveTransport scheme with
        | Some t -> t
        | None -> raise(UnknownTransport scheme)

    let resolveSystem name = configuration.SystemRegistry.Resolve name   
    
    let systems() = configuration.SystemRegistry.All     