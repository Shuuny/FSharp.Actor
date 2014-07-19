namespace FSharp.Actor

open System
open System.Net
open System.Threading

type IRegistry<'key, 'ref> = 
    abstract Resolve : 'key -> 'ref list
    abstract ResolveAsync : 'key * TimeSpan option -> Async<'ref list>
    abstract Register : 'ref -> unit
    abstract UnRegister : 'ref -> unit
    abstract All : 'ref list with get

type ActorRegistry = IRegistry<actorPath, actorRef>

type InMemoryActorRegistry() =
    let syncObj = new ReaderWriterLockSlim()
    let actors : Trie.trie<actorRef> ref = ref Trie.empty
    interface ActorRegistry with
        member x.All with get() = !actors |> Trie.values

        member x.Resolve(path) = 
            try
                syncObj.EnterReadLock()
                let comps = ActorPath.components path
                Trie.resolve comps !actors
            finally
                syncObj.ExitReadLock()

        member x.ResolveAsync(path, _) = async { return (x :> ActorRegistry).Resolve(path) }

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
