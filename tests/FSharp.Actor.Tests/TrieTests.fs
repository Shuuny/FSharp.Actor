namespace FSharp.Actor.Tests

open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``Given a trie``() = 
    
    [<Test>]
    member t.``I can create an empty one``() =
        Trie.empty |> should equal (Trie.Node(None, Map.empty))

    [<Test>]
    member t.``I can add a value to an empty trie``() =
        let actual = 
            Trie.empty |> Trie.add [Trie.Key "a"; Trie.Key "b"] "ab"
        let expected = 
            Trie.Node (None,
                        Map [
                            (Trie.Key "a", Trie.Node (None, 
                                                Map [
                                                        (Trie.Key  "b", Trie.Node (Some "ab", Map []))
                                                    ]
                                            )
                            )]
                      )
        actual |> should equal expected

    [<Test>]
    member t.``I can add different values with the same root``() =
        let actual = 
            Trie.empty 
            |> Trie.add [Trie.Key "a";Trie.Key "b"] "ab" 
            |> Trie.add [Trie.Key "a";Trie.Key "c"] "ac"
        let expected = 
            Trie.Node (None,
                        Map [
                            (Trie.Key "a", Trie.Node (None, 
                                                Map [
                                                        (Trie.Key "b", Trie.Node (Some "ab", Map []))
                                                        (Trie.Key "c", Trie.Node (Some "ac", Map []))
                                                    ]
                                            )
                            )]
                      )
        actual |> should equal expected

    [<Test>]
    member t.``I can retrieve a node with its children``() =
        let actual = 
            Trie.add [Trie.Key "a";Trie.Key "b"] "ab" Trie.empty
            |> Trie.add [Trie.Key "a"; Trie.Key "c"] "ac"
            |> Trie.subtrie [Trie.Key "a"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ab"; "ac"]
        actual |> should equal expected

    [<Test>]
    member t.``I can retrieve a leaf node it should have just its value``() =
        let actual = 
            Trie.add [Trie.Key "a";Trie.Key "b"] "ab" Trie.empty
            |> Trie.add [Trie.Key "a"; Trie.Key "c"] "ac"
            |> Trie.subtrie [Trie.Key "a";Trie.Key "c"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ac"]
        actual |> should equal expected
        
    [<Test>]
    member t.``I can remove a value from a trie``() =
        let actual = 
            Trie.empty |> Trie.add [Trie.Key "a";Trie.Key "b"] "ab" |> Trie.remove [Trie.Key "a";Trie.Key "b"]
        let expected : Trie.trie<string> = Trie.empty
        actual |> should equal expected
        
    [<Test>]
    member t.``I can add a wildcard into a trie key``() =
           let actual = 
                Trie.empty 
                |> Trie.add [Trie.Key "a";Trie.Key "b"] "ab" 
                |> Trie.add [Trie.Key "a";Trie.Key "c"] "ac"
                |> Trie.add [Trie.Key "a";Trie.Key "b";Trie.Key "d"] "abd"
                |> Trie.add [Trie.Key "a";Trie.Key "b";Trie.Key "e"] "abe"
                |> Trie.add [Trie.Key "a";Trie.Key "c";Trie.Key "e"] "ace"
                |> Trie.resolve [Trie.Key "a"; Trie.Wildcard; Trie.Key "e"]
           let expected = ["abe"; "ace"]
           actual |> should equal expected
            