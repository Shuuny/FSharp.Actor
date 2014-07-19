namespace FSharp.Actor

type ActorHostConfiguration = {
    mutable Transports : Map<string,ITransport>
    mutable Registry : ActorRegistry
}

module ActorHost = 
    
    let mutable configuration = { Transports = Map.empty; Registry = new InMemoryActorRegistry() }


