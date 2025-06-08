module DrawioParser

open FSharp.Data

// Use a sample file to define the schema
type Drawio = XmlProvider<"sample.drawio">

type Edge = {
  Id: string
  Source: string
  Target: string
}

type Service = {
    Label: string
    Image: string
    EnvVars: (string * string) list
}

exception GraphError of string

// Node type prefixes
[<Literal>]
let API_PREFIX = "api:"

[<Literal>]
let WATCHER_PREFIX = "watcher:"

[<Literal>]
let WORKER_PREFIX = "worker:"

[<Literal>]
let QUEUE_PREFIX = "queue:"

[<Literal>]
let SINK_PREFIX = "sink:"

// Then dynamically load another file based on user input
let loadFromPath (path: string) =
    let doc = Drawio.Load(path)
    doc

let isEdge (cell: Drawio.MxCell) =
    cell.Edge = (Some 1)

let envVariables(cell: Drawio.Object) =
    let allUppercaseAttributes = 
        cell.XElement.Attributes()
        |> Seq.map (fun a -> a.Name.LocalName, a.Value)
        |> Seq.filter (fun (k,_) -> k.ToUpper() = k)
        |> Seq.toList
    allUppercaseAttributes    

let cellToEdge(cell: Drawio.MxCell) =
    let id = cell.Id
    let source = cell.Source
    let target = cell.Target
    if id.IsNone then raise (GraphError "Edge does not have id") 
    let idValue = id.Value
    if source.IsNone then raise (GraphError $"Edge {idValue} has no source")
    if target.IsNone then raise (GraphError $"Edge {idValue} has no target")
    let sourceValue = source.Value
    let targetId = target.Value
    let edge: Edge = {Id = idValue; Source = sourceValue; Target = targetId}
    edge

let buildAdjacencyMap (edges: Edge list) : Map<string, Set<string>> =
    edges
    |> Seq.groupBy _.Source
    |> Seq.map (fun (src, targets) ->
        src, targets |> Seq.map _.Target |> Set.ofSeq)
    |> Map.ofSeq

let rec dfs (adj: Map<string, Set<string>>) visited node =
    if Set.contains node visited then visited
    else
        let neighbors = Map.tryFind node adj |> Option.defaultValue Set.empty
        Set.fold (dfs adj) (Set.add node visited) neighbors

let tryPickOne (s: Set<'T>) : 'T option =
    if Set.isEmpty s then None else Some (Set.minElement s)

let distinctPaths (edges: Edge list) : Set<string> list =
    let nodes =
        edges
        |> List.collect (fun e -> [e.Source; e.Target])
        |> Set.ofList

    let adj = buildAdjacencyMap edges

    let rec loop remaining components =
        match tryPickOne remaining with
        | None -> List.rev components
        | Some n ->
            let comp = dfs adj Set.empty n
            loop (Set.difference remaining comp) (comp :: components)

    loop nodes []

let buildUndirectedAdjacencyMap (edges: Edge list) : Map<string, Set<string>> =
    edges
    |> List.collect (fun e -> [ e.Source, e.Target; e.Target, e.Source ]) // both directions
    |> Seq.groupBy fst
    |> Seq.map (fun (src, targets) ->
        src, targets |> Seq.map snd |> Set.ofSeq)
    |> Map.ofSeq

let partitionSubgraphs (edges: Edge list) : Set<string> list =
    let nodes =
        edges
        |> List.collect (fun e -> [ e.Source; e.Target ])
        |> Set.ofList

    let adj = buildUndirectedAdjacencyMap edges

    let tryPickOne (s: Set<'T>) = if Set.isEmpty s then None else Some (Set.minElement s)

    let rec loop remaining components =
        match tryPickOne remaining with
        | None -> List.rev components
        | Some n ->
            let comp = dfs adj Set.empty n
            loop (Set.difference remaining comp) (comp :: components)

    loop nodes []

exception CycleDetected of string

let rec dfsCycle (adj: Map<string, Set<string>>) path node =
    if Set.contains node path then
        raise (CycleDetected node)
    else
        let neighbors = Map.tryFind node adj |> Option.defaultValue Set.empty
        neighbors
        |> Set.iter (fun neighbor ->
            dfsCycle adj (Set.add node path) neighbor)


let processDrawio path =
    let doc = loadFromPath path
    let edgeCells =
        doc.Diagram.MxGraphModel.Root.MxCells
        |> Array.filter isEdge
    // printfn $"Found {edgeCells.Length} edges"
    let edges = edgeCells |> Seq.map cellToEdge |> Seq.toList

    let nodeCells =
        doc.Diagram.MxGraphModel.Root.Objects
        |> Seq.map (fun o -> o.Id, (o.Label, o))
        |> Map.ofSeq
    printfn $"Found {nodeCells.Count} nodes"
    // printfn $"Nodes: {nodeCells}"    

    let paths = distinctPaths edges
    let pathsCopula = if paths.Length = 1 then "is" else "are"
    let pathsPlural = if paths.Length = 1 then "" else "s"
    printfn $"There {pathsCopula} {paths.Length} distinct path{pathsPlural} through the system"
    // for path in paths do
    //     printfn "Distinct path:"
    //     for node in path do
    //         printfn $"  - {node}"
    printfn "\n"

    let subGraphs = partitionSubgraphs edges
    let subGraphsCopula = if subGraphs.Length = 1 then "is" else "are"
    let subGraphsPlural = if subGraphs.Length = 1 then "" else "s"
    printfn $"There {subGraphsCopula} {subGraphs.Length} distinct subgraph{subGraphsPlural} through the system"
    
    let fullDirectedAdjMap = buildAdjacencyMap edges
    // printfn $"Adjacency map: {fullDirectedAdjMap}"

    for subGraph in subGraphs do
        let subAdj =
            subGraph
            |> Seq.map (fun n -> n, Map.tryFind n fullDirectedAdjMap |> Option.defaultValue Set.empty)
            |> Map.ofSeq

        for node in subGraph do
            dfsCycle subAdj Set.empty node

    let apiSources = 
        nodeCells 
        |> Map.filter (fun k v -> (fst v).StartsWith(API_PREFIX))

    let watcherSources = 
        nodeCells 
        |> Map.filter (fun k v -> (fst v).StartsWith(WATCHER_PREFIX))

    let workers = 
        nodeCells 
        |> Map.filter (fun k v -> (fst v).StartsWith(WORKER_PREFIX))

    let queues = 
        nodeCells 
        |> Map.filter (fun k v -> (fst v).StartsWith(QUEUE_PREFIX))

    let sinks = 
        nodeCells 
        |> Map.filter (fun k v -> (fst v).StartsWith(SINK_PREFIX))
    
    printfn $"Queues: {queues.Count}"
    printfn $"Api sources: {apiSources.Count}"
    printfn $"Watcher sources: {watcherSources.Count}"
    printfn $"Workers: {workers.Count}"
    printfn $"Sinks: {sinks.Count}"

    let edgeExistsStartingFrom id=  fullDirectedAdjMap.Keys.Contains id

    let apiSourcesWithNoOutgoingEdge = 
        apiSources.Keys
        |> Seq.filter (fun i -> not (edgeExistsStartingFrom i))
        |> Seq.toList
    if apiSourcesWithNoOutgoingEdge.Length > 0 then 
        raise (GraphError "Api sources found with no outgoing edge")
    //todo more validations

    let getQueueConnectionString edgeTarget direction kind label =
        let queueOpt = queues.TryFind edgeTarget
        if queueOpt.IsNone then raise (GraphError $"{kind} {label} does not {direction} a queue")
        let queue = queueOpt.Value
        let queueCell = snd queue  
        let queueEnvVars = envVariables queueCell
        let connectionStringOpt = queueEnvVars |> List.tryFind (fun (v, _) -> v = "CONNECTION_STRING") 
        if connectionStringOpt.IsNone then raise (GraphError $"Queue {fst queue} has no connection string")
        connectionStringOpt.Value

    let nodeToService (hasIncoming: bool) (hasOutgoing: bool) apiSourceKey apiSourceValue =
        let label = fst apiSourceValue
        printfn $"Processing {label}"
        let kind = 
            match hasIncoming, hasOutgoing with
            | false, true -> "api source or worker"
            |true, true -> "worker"
            | true, false -> "sink"
            | _ -> raise (GraphError "Error computing kind")

        let outgoingConnectionStringOpt =
            if hasOutgoing then
                let edgeTargets = fullDirectedAdjMap.Item apiSourceKey
                //todo modify this when multiple outgoing edges are allowed
                if edgeTargets.Count > 1 then 
                    raise (GraphError $"More than one outgoing edge found from {kind} {label}")
                let edgeTarget = edgeTargets.MinimumElement
                Some (getQueueConnectionString edgeTarget "feed into" kind label)
            else
                None    

        let incomingConnectionStringOpt =
            if hasIncoming then
                let edgeSourceOpt = fullDirectedAdjMap |> Map.tryFindKey (fun _ v -> v.Contains apiSourceKey)
                if edgeSourceOpt.IsNone then 
                    raise (GraphError $"No incoming edge found to {kind} {label}")
                let edgeTarget = edgeSourceOpt.Value
                Some (getQueueConnectionString edgeTarget "receive from" kind label)
            else
                None    

        let cell: Drawio.Object = snd apiSourceValue
        let imageOpt = 
            cell.XElement.Attributes()
            |> Seq.map (fun a -> a.Name.LocalName, a.Value)
            |> Seq.filter (fun (k,_) -> k = "image")
            |> Seq.map snd
            |> Seq.tryHead
        if imageOpt.IsNone then raise (GraphError $"{kind} {label} has no image")
        let image = imageOpt.Value

        let envVars = envVariables cell
        let telemetryVars = [
            "OTEL_EXPORTER_OTLP_ENDPOINT", System.Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
            "OTEL_RESOURCE_ATTRIBUTES", System.Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES")
        ]
        let allEnvVarsExceptQueues = 
            ("OTEL_SERVICE_NAME", label) :: 
            (envVars @ telemetryVars)
        let allEnvVarsExceptIncomingQueue = 
            if outgoingConnectionStringOpt.IsSome then
                ("OUTGOING_CONNECTION_STRING", snd outgoingConnectionStringOpt.Value) :: allEnvVarsExceptQueues
            else
                allEnvVarsExceptQueues    
        let allEnvVars = 
            if incomingConnectionStringOpt.IsSome then
                ("INCOMING_CONNECTION_STRING", snd incomingConnectionStringOpt.Value) :: allEnvVarsExceptIncomingQueue
            else
                allEnvVarsExceptIncomingQueue    

        let service: Service = {
            Label = label.Replace(":", "-") ;
            EnvVars = allEnvVars;
            Image = image
        }
        service

    let services = 
        ((apiSources |> Map.map (nodeToService false true)).Values |> List.ofSeq)  @
        ((watcherSources |> Map.map (nodeToService false true)).Values |> List.ofSeq)  @
        ((sinks|> Map.map (nodeToService true false)).Values |> List.ofSeq)  @
        ((workers|> Map.map (nodeToService true true)).Values |> List.ofSeq)
    // printfn $"Services: {services}"

    printfn "Compose.yaml follows>>>>\n"
    printfn "services:"
    for service in services do
        printfn $"  {service.Label}:"
        printfn $"    image: {service.Image}"
        printfn $"    environment:"
        for var in service.EnvVars do
            printfn $"      {fst var}: {snd var}"

    printfn "Done"    
    //     printfn "Distinct subgraph:"
    //     for node in subGraph do
    //         printfn $"  - {node}"
    