namespace FSharp.Actor

open System

module Registry = 
     
     let private syncObj = new Threading.ReaderWriterLockSlim()
     let actors : Trie.trie<string, IActor> ref = ref Trie.empty

     let computeKeysFromPath (path:string) = 
         path.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

     let resolve address =
         try
            syncObj.EnterReadLock()
            Trie.subtrie (computeKeysFromPath address) !actors |> Trie.values
         finally 
            syncObj.ExitReadLock()

     let register (actor:IActor) = 
         try
            syncObj.EnterWriteLock()
            actors := Trie.add (computeKeysFromPath actor.Name) actor !actors
         finally
            syncObj.ExitWriteLock()

     let unregister (actor:IActor) =  
         try
            syncObj.EnterWriteLock()
            actors := Trie.remove (computeKeysFromPath actor.Name) !actors
         finally
            syncObj.ExitWriteLock()


[<AutoOpen>]
module RegistryOperations = 
    let resolve path = Registry.resolve path
    let register (actor:IActor) = actor |> Registry.register
    let inline (!!) path = resolve path