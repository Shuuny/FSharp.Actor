namespace FSharp.Actor

open System.Net
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent

type ActorHostConfiguration =
    {
        mutable CancellationToken : CancellationToken
        mutable Transports : Map<string,ITransport>
    }
    with
        static member internal Default(?transports, ?token) = 
            {
                CancellationToken = defaultArg token (Async.DefaultCancellationToken)
                Transports = (defaultArg transports Seq.empty) |> Seq.map (fun (t:ITransport) -> t.Scheme, t) |> Map.ofSeq
            }

module ActorHost = 
    
    exception UnknownTransport of string

    let private configuration = ActorHostConfiguration.Default() 

    let configure(cfgF : ActorHostConfiguration -> unit) =
        cfgF configuration
        
    let tryResolveTransport (scheme:string) = configuration.Transports.TryFind scheme
    
    let resolveTransport scheme = 
        match tryResolveTransport scheme with
        | Some t -> t
        | None -> raise(UnknownTransport scheme)