namespace FSharp.Actor

open System
open System.Threading

type IActorRegistry = 
    abstract Resolve : ActorPath -> ActorRef list
    abstract Register : ActorRef -> unit
    abstract UnRegister : ActorRef -> unit 

type LocalActorRegistry() =
    let syncObj = new ReaderWriterLockSlim()
    let actors : Trie.trie<string, ActorRef> ref = ref Trie.empty
    interface IActorRegistry with
        member x.Resolve(path) = 
            try
                syncObj.EnterReadLock()
                Trie.subtrie (ActorPath.components path) !actors |> Trie.values
            finally
                syncObj.ExitReadLock()

        member x.Register(actor) =
            try
                syncObj.EnterWriteLock()
                actors := Trie.add (ActorPath.components (ActorPath.ofRef actor)) actor !actors
            finally
                syncObj.ExitWriteLock()

        member x.UnRegister (actor:ActorRef) =
            try
                syncObj.EnterWriteLock()
                actors := Trie.remove (ActorPath.components (ActorPath.ofRef actor)) !actors
            finally
                syncObj.ExitWriteLock()
