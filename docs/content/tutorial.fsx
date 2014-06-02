(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"

(**
Introducing F# Actor
========================

F# Actor is an actor library. The actor programming model is inherently concurrent, an actor is a primitive that wraps a computation, the computation is ran by sending messages to the actor.
The actor can then respond to the reciept of the message by executing one or more of the following actions (possibly concurrently),

 * Create a number of new actors
 * Send a another message to other actors
 * Change the behaviour to be used upon reciept of the next message. 
*)



(**
*)
