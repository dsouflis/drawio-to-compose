open System
open System.IO
open Argu
open DrawioParser

type CLIArgs =
    | [<EqualsAssignment>] Format of RequestedFormat    
    | [<MainCommand; ExactlyOnce>] Input of path:string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Input _ -> "Path to the .drawio file"
            | Format _ -> "Format (either 'compose' or 'aca'; default is 'compose')"

let fullPath (path: string) = Path.GetFullPath(path, Environment.CurrentDirectory)

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<CLIArgs>(programName = "drawio-to-compose")
    try
        let results = parser.Parse argv
        match results.GetResult(Input) with
        | path ->
            let format = results.GetResult(Format, defaultValue = Compose)
            // printfn $"Parsing file: {path} {format}"
            processDrawio (fullPath path) format
            0
    with e ->
        printfn $"ERROR: %s{e.Message} {e.StackTrace}"
        1
