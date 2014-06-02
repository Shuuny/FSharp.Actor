namespace System
open System.Reflection
open System.Runtime.CompilerServices

[<assembly: AssemblyTitleAttribute("FSharp.Actor")>]
[<assembly: AssemblyProductAttribute("FSharp.Actor")>]
[<assembly: AssemblyDescriptionAttribute("An actor library for F#.")>]
[<assembly: AssemblyVersionAttribute("1.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0")>]
[<assembly: InternalsVisibleToAttribute("FSharp.Actor.Tests")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0"
