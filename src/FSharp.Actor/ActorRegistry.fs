namespace FSharp.Actor

open System
open System.Net
open System.Threading

type IActorRegistry = 
    abstract Resolve : actorPath -> actorRef list
    abstract Register : actorRef -> unit
    abstract UnRegister : actorRef -> unit
    abstract All : actorRef list with get

type InMemoryActorRegistry() =
    let syncObj = new ReaderWriterLockSlim()
    let actors : Trie.trie<actorRef> ref = ref Trie.empty
    interface IActorRegistry with
        member x.All with get() = !actors |> Trie.values

        member x.Resolve(path) = 
            try
                syncObj.EnterReadLock()
                let comps = ActorPath.components path
                Trie.resolve comps !actors
            finally
                syncObj.ExitReadLock()

        member x.Register(actor) =
            try
                let components = ActorPath.components (ActorRef.path actor)
                syncObj.EnterWriteLock()
                actors := Trie.add components actor !actors
            finally
                syncObj.ExitWriteLock()

        member x.UnRegister actor =
            try
                let components = ActorPath.components (ActorRef.path actor)
                syncObj.EnterWriteLock()
                actors := Trie.remove components !actors
            finally
                syncObj.ExitWriteLock()

module ActorRegistry = 
    
    let mutable private instance : IActorRegistry = (new InMemoryActorRegistry() :> IActorRegistry)

    let setRegistry(registry:IActorRegistry) = 
        instance <- registry

    let resolve path = instance.Resolve path
    let register actor = instance.Register actor
    let unRegister actor = instance.UnRegister actor

    let map f =  instance.All |> List.map f
    let iter f = instance.All |> List.iter f