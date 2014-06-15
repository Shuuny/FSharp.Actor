namespace FSharp.Actor

open System.Net
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent

type ActorEnvironmentConfiguration =
    {
        CancellationToken : CancellationToken
        Transports : seq<ITransport>

    }
    
    let endpoint = new IPEndPoint(Net.getIPAddress(), defaultArg port (Net.getFirstFreePort()))
    let token = defaultArg token Async.DefaultCancellationToken

    let transports = 
        defaultArg transports Seq.empty
        |> Seq.map (fun t -> t.Start(token); t.Scheme, t)
        |> Map.ofSeq
    


