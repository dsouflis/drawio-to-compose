open System
open System.IO
open Argu
open DrawioParser

type CLIArgs =
    | [<MainCommand; ExactlyOnce>] Input of path:string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Input _ -> "Path to the .drawio file"

let fullPath (path: string) = Path.GetFullPath(path, Environment.CurrentDirectory)

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<CLIArgs>(programName = "drawio-to-compose")
    try
        let results = parser.Parse argv
        match results.GetResult(Input) with
        | path ->
            printfn $"Parsing file: {path}"
            processDrawio (fullPath path)
            0
    with e ->
        printfn $"ERROR: %s{e.Message}"
        1
