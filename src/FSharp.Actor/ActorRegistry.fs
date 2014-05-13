namespace FSharp.Actor

open System
open System.Net
open System.Threading

type IActorRegistry = 
    abstract Resolve : actorPath -> actorRef list
    abstract Register : actorRef -> unit
    abstract UnRegister : actorRef -> unit
    abstract All : actorRef list with get

type LocalActorRegistry() =
    let syncObj = new ReaderWriterLockSlim()
    let actors : Trie.trie<actorRef> ref = ref Trie.empty
    interface IActorRegistry with
        member x.All with get() = !actors |> Trie.values

        member x.Resolve(path) = 
            try
                syncObj.EnterReadLock()
                Trie.resolve (ActorPath.components path) !actors
            finally
                syncObj.ExitReadLock()

        member x.Register(actor) =
            try
                syncObj.EnterWriteLock()
                actors := Trie.add (ActorPath.components (ActorRef.path actor)) actor !actors
            finally
                syncObj.ExitWriteLock()

        member x.UnRegister actor =
            try
                syncObj.EnterWriteLock()
                actors := Trie.remove (ActorPath.components (ActorRef.path actor)) !actors
            finally
                syncObj.ExitWriteLock()