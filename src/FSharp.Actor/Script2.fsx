﻿#r @"..\..\packages\NLog.2.0.1.2\lib\net45\NLog.dll"
#load "Trie.fs"
#load "Types.fs"
#load "Exceptions.fs"
#load "Mailbox.fs"
#load "Logger.fs"
#load "EventStream.fs"
#load "Pervasives.fs"
#load "Actor.Configuration.fs"
#load "Actor.fs"
#load "Supervisor.fs"

open System
open FSharp.Actor

let logger = Logger.create "console"

ActorSystem.Configure()

ActorSystem.EventStream.Subscribe(function
             | ActorStarted(ref) -> logger.Debug("Actor Started {0}",[|ref|], None)
             | ActorShutdown(ref) -> logger.Debug("Actor Shutdown {0}",[|ref|], None)
             | ActorRestart(ref) -> logger.Debug("Actor Restart {0}",[|ref|], None)
             | ActorErrored(ref,err) -> logger.Error("Actor Errored {0}", [|ref|], Some err)
             | ActorAddedChild(parent, ref) -> logger.Debug("Linked Actors {1} -> {0}",[|parent; ref|], None)
             | ActorRemovedChild(parent, ref) -> logger.Debug("UnLinked Actors {1} -> {0}",[|parent;ref|], None)
             )

let sup = 
    Supervisor.create (actor {
            path "error/supervisor"
        }) (fun err -> async { 
                match err.Error with
                | :? InvalidOperationException -> return Restart
                | :? ArgumentException -> return Shutdown
                | _ -> return Shutdown
        })
    |> Actor.register |> Actor.ref

let errorActor = 
    actor { 
        path "exampleActor"
        supervisedBy sup
        messageHandler (fun (ctx:ActorContext<string>) ->
                          let rec loop count = 
                              async {
                                  let! msg = ctx.Receive()
                                  match msg.Message with
                                  | "OK" -> ctx.Logger.Debug("Received {0} - {1}", [|msg; count|], None)
                                  | "Continue" -> invalidArg "foo" "foo"
                                  | "Restart" -> invalidOp "op" "op"
                                  | _ -> failwithf "Boo"
                                  return! loop (count + 1)
                              }
                          loop 0)
    } |> Actor.fromDefinition |> Actor.register |> Actor.ref 

let publisher = 
    async {
        while true do
            errorActor <-- "OK"
            Threading.Thread.Sleep(1000)
    }

Async.Start(publisher)

errorActor <-- Shutdown
errorActor <-- Restart

(errorActor :> IDisposable).Dispose()

errorActor <-- "Restart"
errorActor <-- "Fail"

!!"fracture://remote/actor"

!!"local://exampleActor"