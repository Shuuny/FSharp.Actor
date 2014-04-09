namespace FSharp.Actor

open System

module Registry = 
     let actors : Trie.trie<string, IActor> ref = ref Trie.empty

     let computeKeysFromPath (path:string) = 
         path.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

     let resolve address = 
        Trie.subtrie (computeKeysFromPath address) !actors |> Trie.values

     let register (actor:IActor) = 
         actors := Trie.add (computeKeysFromPath (string actor.Name)) actor !actors
     
     let unregister (actor:IActor) =  
         actors := Trie.remove (computeKeysFromPath actor.Name) !actors

[<AutoOpen>]
module RegistryOperations = 
    let resolve path = Registry.resolve path
    let register (actor:IActor) = actor |> Registry.register
    let inline (!!) path = resolve path