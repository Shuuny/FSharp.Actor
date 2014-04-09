namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("FSharp.Actor")>]
[<assembly: AssemblyProductAttribute("FSharp.Actor")>]
[<assembly: AssemblyDescriptionAttribute("An actor library for F#.")>]
[<assembly: AssemblyVersionAttribute("1.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0"
