(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
open System

(**
F# Actor
===================

Documentation

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The F# Actor library can be <a href="https://nuget.org/packages/FSharp.Actor">installed from NuGet</a>:
      <pre>PM> Install-Package FSharp.Actor</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Example
-------
The example below is essentially the `Hello World` of actors.
Here we create a discriminated union to represent the type of messages that we want the actor to process. 
Next we create an actor called `/greeter` to handle the messages. Then finally we use `<--` to send the message
so the actor can process it. 
*)
#r "FSharp.Actor.dll"
open FSharp.Actor

let system = ActorSystem.Create("greeterSystem")

type Say =
    | Hello
    | Name of string

let greeter = 
    actor {
        path "/greeter"
        messageHandler (fun actor ->
            let rec loop() = async {
                let! msg = actor.Receive()
                match msg.Message with
                | Hello ->  actor.Logger.Debug("Hello")
                | Name name -> actor.Logger.DebugFormat(fun p -> p "Hello, %s" name, Log.TraceHeader.Create(123UL, 10UL))
                return! loop()
            }
            loop())
    } |> system.SpawnActor

greeter <-- Name("from F# Actor")

(**

Samples & documentation
-----------------------

The library comes with comprehensible documentation. 
It can include a tutorials automatically generated from `*.fsx` files in [the content folder][content]. 
The API reference is automatically generated from Markdown comments in the library implementation.

 * [Tutorial](tutorial.html) contains a further explanation of this sample library.

 * [API Reference](reference/index.html) contains automatically generated documentation for all types, modules
   and functions in the library. This includes additional brief samples on using most of the
   functions.
 
Contributing and copyright
--------------------------

The project is hosted on [GitHub][gh] where you can [report issues][issues], fork 
the project and submit pull requests. If you're adding new public API, please also 
consider adding [samples][content] that can be turned into a documentation. You might
also want to read [library design notes][readme] to understand how it works.

The library is available under Public Domain license, which allows modification and 
redistribution for both commercial and non-commercial purposes. For more information see the 
[License file][license] in the GitHub repository. 

  [content]: https://github.com/colinbull/FSharp.Actor/tree/master/docs/content
  [gh]: https://github.com/colinbull/FSharp.Actor
  [issues]: https://github.com/colinbull/FSharp.Actor/issues
  [readme]: https://github.com/colinbull/FSharp.Actor/blob/master/README.md
  [license]: https://github.com/colinbull/FSharp.Actor/blob/master/LICENSE.txt
*)
