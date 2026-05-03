# The initial IDEAS 

-----------------------------------------
[IDEA] Event Browser Multi-select & Copy to JSON
I need to be able to select multiple events in the Event Browser.
In the context menu of the event list I would like to see "Copy" item
which should make a JSON copy of each selected event (from older to newer) and concatenate these and copy the resulting
text to the clipboard.
[
    {
      "EventType": "Fdp.Toolkit.Replication.Messages.OwnershipUpdate",
      "Frame": 773,
      "Payload": { ... }
    },
    {
      "EventType": "Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand",
      "Frame": 769,
      "Payload": { ... }
    }
]

The json needs to be post processed via Newtonsoft.json as done when saving scenario file
 - put array containing numbers in a single line etc to save number of lines
 - Share this code with scenario saving!
 - This post-process should be done in same manner what clickin "Copy to JSON" for individual records

---------------------------------------
[IDEA] Entity inspector Multi-select & Copy to JSON
I need to be able to select multiple entities in the Entity inspector.
In the context menu of the entity list I would like to see "Copy" item
which should make a JSON copy of each selected entity

[
    {
      "EntityId": [0, 1],
      "Components": { ... }
    },
    {
      "EntityId": [1, 5],
      "Components": { ... }
    },
]

 and copy the resulting json text to the clipboard.

The json needs to be post processed via Newtonsoft.json as done when saving scenario file
 - put array containing numbers in a single line etc to save number of lines
 - Share this code with scenario saving!
 - This post-process should be done in same manner what clickin "Copy to JSON" for individual records
---------------------------------------
[IDEA] Grabbing complete entity state & events across the cluster
In cluster runner distributed config, i need to take a snapshot of selected entity (or all entities)
across all nodes, as a json file looking like { "CFG": { entity json }, "SimHost": { entity json } }
and copy it to the clipboard.

Smilarly i would like to grab the event browser event snapshot of all registered event providers,
in chronological order of frame indexes (from older to newer)
{ "CFG": { "World": [ list of json formatted events ], "Perception": [ ... ] }, "SimHost": { ... }  }

The orchestrator should support this new state dump operation - asking all nodes to make the snapshots
and send them (copy them as files) to dedicated central NAS location.

The orchestrator should have UI dialog where we can select what kind of snapshot dump to create
 - a matrix table with subsystems as columns and dump kinds as rows
     - if we want the entity dump
       - if just selected entity (by network id) or all entities
     - if we want the event dump
       - from what subsystems
       - what event provider (hardcoded list of names like "World", "Perception", "Orchestration"
          matching the names given to the event providers by the nodes)
     - if we want architectural diagnostic window dump
     - if we want the message log dump
       - how much detailed - threshold debug level
       - age threshold (hours)
       - note: logs are never saved as json or markdown, alway just like .log file

     - if we want the message log dump

 - checkbox if we want to add .md extension to the files istead of plain json - in such a case the files
   should contain markdown like the following, with pretty formatted json
     ``` json
     { "CFG": { ... }, "SimHost": { ... } }
     ```
 - The UI should monitor the progress of the operation and show the result table (tree) per node
       CGF
         Entities
         Events
         Arch
         Logs
     
   For each element there should be a context menu allowing to
      - copy to the content to clipboard
      - copy the NAS file name to clipboard
      - open the file (directly from NAS) in default application
      - save to a local file (should open some kind of save as dialog)

The dump files should be saved to the NAS folder (same as defined for recordings, scenarios..)
 - "[NAS]/dumps/dump_DATETIME_entities.json[.md]"
 - "[NAS]/dumps/dump_DATETIME_events.json[.md]"
 - "[NAS]/dumps/dump_DATETIME_arch.json[.md]"
 - "[NAS]/dumps/dump_DATETIME_logs_CGF.log"
The DATETIME part is the timestamp or the user request and must be identical for all files frot the same request.
format "YYYYMMDD_HHMMSS" in local time of the orchestrator.


The jsons needs to be post processed via Newtonsoft.json as done when saving scenario file
 - put array containing numbers in a single line etc to save number of lines
 - Share this code with scenario saving!

---------------------------------------
[BUG] Event browser's "Copy to json" wrongly serializaes the "Reason" field below
(probably fixedString)

{
  "EventType": "Fdp.Toolkit.Lifecycle.Events.DestructionOrder",
  "Frame": 3095,
  "Payload": {
    "Entity": {
      "PackedValue": 4294967297,
      "IsNull": false,
      "Index": 1,
      "Generation": 1
    },
    "FrameNumber": 3096,
    "Reason": { // <== FIXED string???
      "Length": 11,
      "IsEmpty": false
    }
  }
}


The serialization code including the custom formatters and post-processing should be shared as much as possible.
---------------------------------------
[IDEA] JSON Dump of the architectural diagnostic window
The content of the window should be saveable to json string
  - module list
  - system list
  - translator list
Each including also the available stats - avg time, max time, total time, number of runs...
---------------------------------------
[IDEA] Dump of the node log file (the nlog file)
The node must be setup to save its nlog logs into one single file, with automatic rotation.
The content of the files (current one including the rotation archives),
after filtering by the criteria like record age and log level threshold,
should be copied to the dump file on NAS.
Because of the size the log copying should not go via orchestrator dds network.

---------------------------------------
----




 I would like to greatly enhance the diagnostic capabilities by allowing to save the dumps from across the whole cluster. Let's discuss the ideas first, thinking abouot improvements, gaps, flaws, to clarify the requirements.

This is a fantastic set of ideas for improving the diagnostic observability of the cluster. Extracting unified state across a distributed ECS architecture is notoriously difficult, so having a robust, centralized dump mechanism will save countless debugging hours.

Let's break down these ideas from `IDEAS.md` into architectural layers, looking at how they integrate with the current codebase, where the gaps are, and how we can refine the requirements.

1\. Cluster-Wide Dump Orchestration & Network I/O

**The Idea:** The Orchestrator sends a command to all nodes to take a snapshot (entities, events, arch stats, logs) and save them to a central NAS `[NAS]/dumps/dump_DATETIME_*`. Logs specifically shouldn't go over the DDS network due to size.

**Improvements & Alignments:**

-   **Zero Bulk Data Over DDS:** You correctly noted that logs shouldn't go over DDS. In fact, _none_ of the dump files should go over DDS. We should fully leverage the existing **SMB Pull Gateway Pattern** used by the `StorageGatewayModule`.-   **Reusing the 2PC Pipeline:** We can introduce a new `ClusterOpType.DumpDiagnostics` and `NodeOpType.DumpLocalDiagnostics`. The workflow would perfectly match `SerializeLocal` / `SaveScenario`:
    -   Orchestrator fans out the dump request (carrying a DTO of what to dump).-   Nodes write their local JSON/MD/Log files to their `LocalTempRoot` (e.g., `C:\FDP_Temp\dumps\...`).-   Nodes respond with a `NodeOpStatus(Success)` containing a `FileManifestEntry`.-   The `StorageProcessManager` aggregates these manifests and uses `StorageGatewayModule.PullToNasAsync` to securely pull all files to the central NAS in parallel.

**Gaps/Flaws:**

-   **Time Synchronization:** You requested `DATETIME` to be identical across all files. Using the local time of the orchestrator is good, but we should pass this exact timestamp (or a generated batch GUID) inside the `ClusterOpRequest` payload. This guarantees that files from node 1 and node 400 perfectly align in their naming convention.

2\. Event History & Snapshotting

**The Idea:** Grab an event snapshot of all registered event providers chronologically (e.g., World, Perception, Orchestration) across the cluster.

**Gaps/Flaws:**

-   **Event Buffer Depth Limits:** The `FdpEventBus` is double-buffered (front/back), meaning it only holds 1 frame of history natively. The `EventAccumulator` captures history but trims it to `_maxHistoryFrames` (default 10).-   **Requirement Clarification:** If you want a deep chronological history of events in this JSON dump, 10 frames (~160ms) might not be enough. We need to clarify if this dump relies on a background rolling logger (like the FlightRecorder) or if it just dumps the immediate `EventAccumulator` state for immediate crash/state inspection.

3\. Entity State Correlation

**The Idea:** Dump selected or all entities across all nodes into a unified JSON structure.

**Improvements:**

-   **Network ID Resolution:** On the local nodes, entities are identified by internal generational indices (e.g., `Entity { Index: 1, Generation: 1 }`). To make the JSON readable across nodes, the extraction logic _must_ map local `Entity` handles to their global `NetworkId` (via the `NetworkEntityMap`). The resulting JSON should probably be keyed by `NetworkId` so you can easily diff the SimHost representation of an entity vs the CGF representation.

4\. The `FixedString64` Serialization Bug

**The Idea/Bug:** The event browser's "Copy to JSON" incorrectly serializes `FixedString64` fields (like the `Reason` field in `DestructionOrder`), resulting in `{ "Length": 11, "IsEmpty": false }` instead of the actual text.

**Flaws / Fix:**

-   `FixedString64` is an unmanaged struct wrapping a fixed byte buffer. The default `System.Text.Json` serializer doesn't know how to read the bytes as UTF-8.-   **Requirement:** We need to write a custom `JsonConverter<FixedString64>` and register it globally in `HrotSerializerOptions.HrotJsonOptions` (which the UI and dump system can share).

5\. Architectural Diagnostics Dump

**The Idea:** Dump the contents of the Architecture Diagnostic Window (modules, systems, translators, execution counts, circuit breaker states, etc.).

**Improvements:**

-   The data is already beautifully structured via `ModuleHostKernel.GetModuleDiagnostics()` which returns a list of `ModuleDiagnostics` containing `RunMode`, `TargetFrequencyHz`, `CircuitState`, `ExecutionCount`, etc. Exporting this to JSON is trivial, but doing this across the cluster provides a holistic health check that is currently impossible to see from a single pane of glass.

6\. Node NLog File Dump

**The Idea:** Copy the node's NLog file to the NAS dump. Filter by age and log-level threshold.

**Gaps/Flaws:**

-   **Filtering Overhead:** Asking a node to parse its own text-based `.log` files (and rotation archives) to filter by age and threshold on the fly could cause massive CPU/I/O spikes, potentially interrupting the 60Hz deterministic simulation loop.-   **Proposed Alternative:** The nodes should just copy their current `Active.log` (and maybe the immediate previous rotation) directly to the `LocalTempRoot` as part of the dump. The Orchestrator's UI (or a separate tool) can do the parsing/filtering _after_ it has been safely pulled to the NAS. This keeps the node's task O(1) and safe.

7\. UI / UX Considerations

**The Idea:** UI Dialog with a matrix of subsystems/dump kinds, markdown wrapper generation, and a results tree with context menus (copy to clipboard, open from NAS, save locally).

**Improvements:**

-   The Orchestrator's `ClusterScenarioPanel` already tracks operations via `DistributedTransaction` and `ClusterUiCache`. We can add a "Diagnostics" tab.-   For the Markdown requirement (`.md` extension with formatted JSON): This is a great touch for readability in IDEs/GitHub. The payload processor should just wrap the JSON string in ` ```json\n ... \n``` `.

\--------------------------------------------------------------------------------

Suggested Next Steps for Implementation:

-   **Serialization Layer:** Fix the `FixedString64` bug and abstract the "Single-line Array" JSON post-processing logic into a shared utility.-   **Orchestration Protocol:** Add the new Enums (`ClusterOpType.DumpDiagnostics`, `NodeOpType.DumpDiagnostics`) and create the DTO payload containing the user's matrix selections.-   **Node-side Handlers:** Create a `DiagnosticsDumpClusterOpHandler` that reads the DTO, queries the `EntityRepository`, `EventBus`, and `ModuleHostKernel`, writes to `C:\FDP_Temp`, and returns the `FileManifestEntry`.-   **UI & UX:** Build the matrix dialog in the Orchestrator and the multi-select "Copy to JSON" context menus in the Event/Entity browsers.

How would you like to prioritize these? Should we start by designing the exact JSON schema for the dumps, or tackle the `FixedString64` serialization bug first?

the Event History shouldl be the one shown by the Event Browser now, keeping areound 500 events (at least). Maybe we will need a backend service to keep the data if not already existing, reusable by the event browser as well as the cluster diagnostic system.

entity state correlation & network id resolution - each entity has its NetworkIdentity component. No special keying necessary, this component is the key by itself.

Node NLog File Dump: the logs can be massive. filtering MUST be done on node. Including all rotation archives. In background thread. Ignore CPU spikes.

"Diagnostics" tab is a good idea. But i would like this whole "Diagnostics" tab to be usable from ExCon, i.e. the features (triggering, monitoring, opening...) should be available from non-orchestrator subsystem.

Systém nedokázal odpovědět.

the Event History shouldl be the one shown by the Event Browser now, keeping areound 500 events (at least). Maybe we will need a backend service to keep the data if not already existing, reusable by the event browser as well as the cluster diagnostic system.

entity state correlation & network id resolution - each entity has its NetworkIdentity component. No special keying necessary, this component is the key by itself.

Node NLog File Dump: the logs can be massive. filtering MUST be done on node. Including all rotation archives. In background thread. Ignore CPU spikes.

"Diagnostics" tab is a good idea. But i would like this whole "Diagnostics" tab to be usable from ExCon, i.e. the features (triggering, monitoring, opening...) should be available from non-orchestrator subsystem.

The revised requirements clarify the architectural boundaries and dictate specific data ownership models. Let us break down the design for each of these four refined areas.

1\. Event History Backend Service

Currently, the 500-event history is tightly coupled to the UI layer in `Fdp.Presentation.Panels.EventBrowserPanel`, which maintains its own `List<CapturedEvent>` and manually enforces the `_capacity = 500` limit. This is an anti-pattern if the data must be accessible headlessly for diagnostic dumps.

**Design:**

-   Extract the event capture logic into a dedicated, headless backend service, e.g., `DiagnosticEventHistoryService`.-   This service will run in the simulation loop (or as an `IEcsModuleSystem`) and call `GetDebugInspectors()` on the `FdpEventBus` to read the current buffers via `InspectReadBuffer()`, exactly as the UI does now.-   The service will maintain a thread-safe circular buffer or capped queue of the last ~500 events.-   The `EventBrowserPanel` will be refactored to observe this service rather than doing the capture itself.-   When a diagnostic dump is requested, the node-side dump handler will simply query this service and serialize its buffer to JSON.

2\. Entity State Correlation via NetworkIdentity

Using `NetworkIdentity` as the natural correlation key simplifies the extraction logic.

-   We do not need to construct a custom dictionary or mapping scheme for the JSON.-   The extraction pipeline can just dump the raw JSON array of the requested entities. Because the `FdpAutoSerializer` includes all components on the entity, the `NetworkIdentity` component (Component ID 50) will naturally be present in the serialized output.-   Off-node tooling or the clipboard consumer will use standard JSON parsing to group or diff the entities by reading the `NetworkIdentity.Value` field.

3\. Node NLog File Dump (Node-Side Background Filtering)

Since CPU spikes are acceptable and filtering must occur on the node including all rotation archives, we will implement this via a dedicated background task to avoid completely stalling the 60Hz `ModuleHostKernel` loop.

**Design:**

-   Create a `NodeOpType.DumpDiagnostics`.-   The corresponding `IClusterStateHandler` on the node will implement `PrepareAsync` to spawn a `Task.Run(..., TaskCreationOptions.LongRunning)`.-   This background task will locate the NLog directory, iterate the active log and all rotation archives, and apply the age and severity threshold filters line-by-line.-   The filtered output will be written to a new file in the `LocalTempRoot` (e.g., `C:\FDP_Temp\dump_YYYYMMDD_HHMMSS_logs_SimHost.log`).-   The handler will return a `FileManifestEntry` containing the `SourceUnc` and `RelativeDest`.-   The Orchestrator will aggregate these manifests and use the existing `StorageGatewayModule.PullToNasAsync` to move the filtered logs to the NAS.

4\. ExCon Integration for the Diagnostics Tab

To make the "Diagnostics" tab fully functional from `ExConSubsystem` (a non-orchestrator node), the UI must strictly adhere to the CQRS cluster management pattern. It cannot access the `ClusterMaster` directly.

**Design:**

-   **Triggering:** The ExCon UI will construct the matrix selection and publish a `ClusterOpIntent` onto its local `FdpEventBus`. The `OperationType` will be a newly added `ClusterOpType.DumpDiagnostics`. The `DomainPayload` will contain the JSON-serialized dump configuration (what to dump, time thresholds, markdown wrapper flag).-   **Egress:** The `ClusterOpEgressTranslator` on the ExCon node will consume the intent and write a `ClusterOpRequest` to DDS.-   **Orchestration:** The Orchestrator's `ClusterOpMasterTranslator` receives the request and forwards it to the `ClusterMaster`. The `ClusterMaster` will fan out `NodeOpType.DumpDiagnostics` to all active nodes.-   **Aggregation & NAS Pull:** A new `DiagnosticsConsensusAggregator` (implementing `INodeResponseAggregator`) will collect the `FileManifestEntry` results from all nodes. A dedicated Process Manager (like `StorageProcessManager`) will react to the completion, execute the `PullToNasAsync` to the NAS, and then publish the final `ClusterOpStatus`.-   **Monitoring & UI Updates:** ExCon's `OrchestrationObserverTranslator` will read the `ClusterOpStatus` from DDS and update the `ClusterUiCache`. The ExCon Diagnostics Tab will read from this cache to display the operation's progress, the final NAS paths, and populate the context menus for opening/saving files.

good! what will the data model look like? What new message types (or new payload formats) will be needed

To support the distributed diagnostic dump feature, the data model must be extended across the orchestration and networking layers. The design will follow the existing CQRS and 2PC (Two-Phase Commit) patterns used for scenarios and checkpoints, avoiding new DDS topics by embedding the diagnostic requests within the established `ClusterOpRequest` and `NodeOpCommand` pipeline.

1\. Operation Enums Extensions

The Orchestrator and Node operation enumerations must be expanded to represent the new command across the DDS boundary and the internal event bus.

**DDS Wire Enums (****Hrot.NED.Descriptors.Orchestration** **&** **Fdp.Toolkit.Orchestration****)**

```
public enum ClusterOpType : int
{
    // ... existing ops [3]
    DumpDiagnostics = 16
}

public enum NodeOpType : int
{
    // ... existing ops [3]
    DumpDiagnostics = 28
}
```

2\. Network Payload DTO (`DiagnosticDumpPayloadDto`)

To transmit the exact user selections from ExCon to the Orchestrator, and subsequently from the Orchestrator to the nodes, we will introduce a new DTO in `OrchestrationPayloadDtos.cs`. It will serialize to JSON using `OrchestrationJsonOptions.Default`.

```
/// <summary>
/// Payload DTO for ClusterOpType.DumpDiagnostics and NodeOpType.DumpDiagnostics.
/// Carries the specific matrix selections requested by the operator.
/// </summary>
public record DiagnosticDumpPayloadDto(
    [property: JsonPropertyName("ExerciseId")]
    Guid ExerciseId, // Used to group the dump files under the correct NAS directory

    [property: JsonPropertyName("TargetNodeIds")]
    List<int>? TargetNodeIds, // Controls which columns of the matrix were selected

    [property: JsonPropertyName("DumpEntities")]
    bool DumpEntities,

    [property: JsonPropertyName("SpecificNetworkIds")]
    List<long>? SpecificNetworkIds, // Null/Empty means all entities

    [property: JsonPropertyName("DumpEvents")]
    bool DumpEvents,

    [property: JsonPropertyName("EventProviders")]
    List<string>? EventProviders, // e.g., ["World", "Perception", "Orchestration"]

    [property: JsonPropertyName("DumpArchitecture")]
    bool DumpArchitecture,

    [property: JsonPropertyName("DumpLogs")]
    bool DumpLogs,

    [property: JsonPropertyName("LogSeverityThreshold")]
    int LogSeverityThreshold,

    [property: JsonPropertyName("LogAgeHours")]
    float LogAgeHours,

    [property: JsonPropertyName("UseMarkdownWrapper")]
    bool UseMarkdownWrapper
);
```

3\. CQRS Intent Events

When the Orchestrator's `ClusterOpMasterTranslator` receives the `ClusterOpRequest` from DDS, it will deserialize the payload and publish a strongly-typed intent onto the Orchestrator's local event bus.

**In** **ClusterOpIntents.cs****:**

```
[EventId(9058)] // Next available ID [8-10]
[DataPolicy(DataPolicy.NoRecord)]
public struct ExecuteDiagnosticDumpIntent
{
    public Guid RequestId;
    public DiagnosticDumpPayloadDto Configuration;
}
```

4\. Node Response Data Model

There is no need to invent a new response DTO for the nodes. The nodes will execute the dump locally to `LocalTempRoot`, and the response must leverage the existing `FileManifestEntry` array.

When the background task on a `ClusterSlave` completes the dump, it will publish a `NodeOpCompletedEvent` containing a `List<FileManifestEntry>` in its `ResultPayload`. This list defines the source UNC path and the relative destination path on the NAS.

```
// Example NodeOpStatus.ResultJson from Node 400
[
  {
    "SourceUnc": "C:\\FDP_Temp\\dumps\\dump_20260503_110900_entities_node400.json.md",
    "RelativeDest": "dumps\\dump_20260503_110900_entities_node400.json.md"
  },
  {
    "SourceUnc": "C:\\FDP_Temp\\dumps\\dump_20260503_110900_logs_node400.log",
    "RelativeDest": "dumps\\dump_20260503_110900_logs_node400.log"
  }
]
```

5\. Orchestrator Aggregation & Completion

On the Orchestrator side, we do not need a custom aggregator. We will reuse the existing `StorageConsensusAggregator` (or create an identical `DiagnosticsConsensusAggregator`) which implements `INodeResponseAggregator`.

This aggregator flattens the `FileManifestEntry` lists from all participating nodes into a single array. The `StorageProcessManager` will observe the resulting `ClusterOpCompletedEvent`, invoke `StorageGatewayModule.PullToNasAsync` to execute the parallel SMB pull of all generated diagnostic files, and finally publish the terminal status back to the ExCon UI.

in DiagnosticDumpPayloadDto, empty EventProviders list = all providers available on the node.

pls suggest the wireframes of the UIs

The `DiagnosticDumpPayloadDto` adjustment is logical; treating an empty `EventProviders` list as a wildcard for all providers prevents unnecessary coupling between the ExCon configuration layer and the specific event providers registered on each remote node.

Given the application relies on Dear ImGui for its presentation layer, the wireframes below map directly to standard ImGui composition paradigms (Tables, TreeNodes, Selectables, and Popups).

1\. Cluster Diagnostics Panel (ExCon / Orchestrator)

This panel orchestrates the 2PC diagnostic dump. It is split into a configuration matrix and an asynchronous results tree.

```
=============================================================================
[X] Diagnostics                                                         [X]
=============================================================================
[ Configuration ]
  [x] Wrap JSON output in Markdown block (.md)
  
  Target Matrix:
  +-------------------+--------+---------+-------+-------+
  | Dump Kind         | Master | SimHost |  CGF  | ExCon |
  +-------------------+--------+---------+-------+-------+
  | Entities (All)    |  [x]   |   [x]   |  [x]  |  [-]  |
  | Entities (Select) |  [ ]   |   [ ]   |  [ ]  |  [-]  | Network IDs: [_______]
  | Events            |  [x]   |   [x]   |  [x]  |  [x]  | Providers:   [_______] (Empty=All)
  | Architecture      |  [x]   |   [x]   |  [x]  |  [x]  |
  | NLog Files        |  [x]   |   [x]   |  [x]  |  [x]  | 
  +-------------------+--------+---------+-------+-------+
  Log Filters:  Severity >= [ Info  v ]    Max Age: [ 24.0 ] hours

  [ EXECUTE CLUSTER DUMP ]

-----------------------------------------------------------------------------
[ Operation Progress & Results ]
  Status: COMPLETED (TxID: a1b2c3d4...)

  v [Node 1] SimHost
    |- Entities: dump_20260503_110900_entities_SimHost.json.md 
    |- Events:   dump_20260503_110900_events_SimHost.json.md   
    |- Arch:     dump_20260503_110900_arch_SimHost.json.md     
    |- Logs:     dump_20260503_110900_logs_SimHost.log         [Right-Click]
                                                               +-------------------------+
  > [Node 400] CGF                                             | Copy Content            |
  > [Node 500] ExCon                                           | Copy NAS Path           |
                                                               | Open from NAS...        |
                                                               | Save Local Copy As...   |
                                                               +-------------------------+
```

**Architectural Notes for ImGui Implementation:**

-   **Target Matrix:** Implemented via `ImGui.BeginTable` with the `ImGuiTableFlags.Borders` flag. The columns map to the discovered nodes in the `ClusterUiCache`.-   **Result Tree:** Uses `ImGui.TreeNodeEx` for the node headers. The file elements are `ImGui.Selectable` items.-   **Result Context Menu:** Triggered via `ImGui.BeginPopupContextItem()` on the file selectables. The "Open from NAS" command will delegate to `Process.Start(new ProcessStartInfo { FileName = uncPath, UseShellExecute = true })`.

2\. Event Browser Multi-Select & JSON Extraction

The current `EventBrowserPanel` assumes a single `_selectedEvent`. To support multi-selection, the state must transition to a `HashSet<CapturedEvent> _selectedEvents`.

```
=============================================================================
[X] Event Browser                                                       [X]
=============================================================================
  [ Bus: World v ] [ Clear ] [ ] Pause  [ Filter... ] | Showing 500 / 500

  +-------------------------------------------------------------------------+
  | Frame/Type                                  | Summary                   |
  +-------------------------------------------------------------------------+
  | [5] Fdp.Toolkit.Replication.Messages...   | OwnershipUpdate: Entity...|
  | [6] Fdp.Toolkit.NetworkSpawning.Events... | SpawnEntityCommand: 401...| (Selected)
  | [7] Fdp.Toolkit.Combat.Events.Weapon...   | WeaponFireIntent: 401...  | (Selected)
  | [8] Fdp.Toolkit.Lifecycle.Events.Con...   | ConstructionAck: 401...   | [Right-Click]
  | ...                                         |                           +--------------+
  +-------------------------------------------------------------------------+ Copy to JSON |
                                                                            +--------------+
```

**Architectural Notes for ImGui Implementation:**

-   **Multi-Select Behavior:** Standard OS modifiers (Ctrl+Click, Shift+Click) must be wired into the `ImGui.Selectable` interactions.-   **Copy Operation:** Iterates `_selectedEvents` sorted by `CapturedEvent.Frame` ascending, serializes the payload, and executes `ImGui.SetClipboardText(json)`.-   **Post-Processing:** The output array must run through the newly proposed array-flattening formatting logic to reduce line count.

3\. Entity Inspector Multi-Select & JSON Extraction

The `EntityInspectorPanel` uses an `IInspectorContext` currently strictly defining `Entity? SelectedEntity`. This contract requires modification to `IReadOnlyCollection<Entity> SelectedEntities` or similar.

```
=============================================================================
[X] Entity Inspector                                                    [X]
=============================================================================
  Total Entities: 1042  Search ID: [        ] 

  +-------------------------------+-----------------------------------------+
  | List                          | Details                                 |
  +-------------------------------+-----------------------------------------+
  | [14, v2] (NetID: 4001)        | [ Multiple Entities Selected ]          |
  | [15, v1] (NetID: 4002) (Sel)  |                                         |
  | [16, v1] (NetID: 4003) (Sel)  | Details cannot be displayed for         |
  | [17, v1] (NetID: 4004)        | multiple entities simultaneously.       |
  | [18, v1] (NetID: 4005) [R-Clk]|                                         |
  |                        +------------------------+                       |
  |                        | Center on Entity       |                       |
  |                        | Rename...              |                       |
  |                        | Edit Shape             |                       |
  |                        | Rotate                 |                       |
  +------------------------|------------------------|-----------------------+
                           | Delete                 |
                           |------------------------|
                           | Copy to JSON (2 items) |
                           +------------------------+
```

**Architectural Notes for ImGui Implementation:**

-   **Context Menu Handlers:** The existing `IEntityContextMenuHandler.PopulateMenu` signature passes a single `Entity`. We must introduce an overload or modify the signature to `PopulateMenu(IReadOnlyCollection<Entity> entities, IContextMenuBuilder builder)`.-   **JSON Serialization:** The custom "Copy to JSON" handler will iterate the selected set and format the output as a JSON array of entity dumps, using the same formatting utilities required by the Event Browser and Scenario Saver.

Note that there can be multiple instances of each subsystem/node, each having different unique node id!

If user selects it wants the dump from CGF, it means he wants it from all CGF node instances.

The format of the file name should change to include both the subsystem type and node id:

dump\_20260503\_110900\_entities\_CGF\_400.json

Also the tree in Operation Progress & Results should group results by subsystem type and show corresponding node ids belonging to each group.

IN event browser, the format of the Frame/Type column should not change - must stay as it is rendered now; no FQNs necessary \[frame\] color-coded-Short-name-of-event grayed details | Frame/Type | Summary |

The architectural adjustments for multi-instance subsystems and the corrected Event Browser presentation dictate changes to the orchestration payload resolution, the node-side file generation, and the ExCon UI rendering logic.

1\. Multi-Instance Subsystem Targeting & File Naming

The cluster often runs multiple instances of specific subsystems (e.g., several `IG` nodes, multiple `CGF` brains).

**Routing Design:** The configuration matrix will display columns for `SubsystemName` rather than individual nodes. When the dump is triggered, the ExCon UI will query the `ClusterUiCache.ActiveNodes` to resolve the selected subsystem columns into an explicit `List<int> TargetNodeIds`. This ensures the orchestration layer targets exact, currently-known instances without pushing string-matching logic down into the cluster master.

**Node-Side File Generation:** The `DiagnosticsDumpClusterOpHandler` on each node already has access to its `_subsystemName` and `_nodeId` via the `ClusterSlave` context. The file generation template will be strictly formatted as: `dump_{DATETIME}_{kind}_{SubsystemName}_{NodeId}.{ext}`

Example outputs generated by the nodes:

-   `dump_20260503_110900_entities_CGF_400.json.md`-   `dump_20260503_110900_entities_CGF_401.json.md`-   `dump_20260503_110900_logs_IG_100.log`

2\. ExCon Diagnostics UI Wireframe Updates

The results tree will group the aggregated `FileManifestEntry` responses by `SubsystemName`, then by `NodeId`. The ExCon UI will cross-reference the incoming `NodeId` from the operation status with its `ClusterUiCache` to build this hierarchy.

```
=============================================================================
[X] Diagnostics                                                         [X]
=============================================================================
[ Configuration ]
  [x] Wrap JSON output in Markdown block (.md)
  
  Target Matrix (Applies to all active instances of the subsystem):
  +-------------------+--------+---------+-------+-------+-------+
  | Dump Kind         | Master | SimHost |  CGF  |  IG   | ExCon |
  +-------------------+--------+---------+-------+-------+-------+
  | Entities (All)    |  [x]   |   [x]   |  [x]  |  [-]  |  [-]  |
  | Entities (Select) |  [ ]   |   [ ]   |  [ ]  |  [-]  |  [-]  | Network IDs: [_______]
  | Events            |  [x]   |   [x]   |  [x]  |  [x]  |  [x]  | Providers:   [_______]
  | Architecture      |  [x]   |   [x]   |  [x]  |  [x]  |  [x]  |
  | NLog Files        |  [x]   |   [x]   |  [x]  |  [x]  |  [x]  | 
  +-------------------+--------+---------+-------+-------+-------+
  Log Filters:  Severity >= [ Info  v ]    Max Age: [ 24.0 ] hours

  [ EXECUTE CLUSTER DUMP ]

-----------------------------------------------------------------------------
[ Operation Progress & Results ]
  Status: COMPLETED (TxID: a1b2c3d4...)

  v CGF
    v [Node 400]
      |- Entities: dump_20260503_110900_entities_CGF_400.json.md 
      |- Events:   dump_20260503_110900_events_CGF_400.json.md   
      |- Arch:     dump_20260503_110900_arch_CGF_400.json.md     
      |- Logs:     dump_20260503_110900_logs_CGF_400.log         [Right-Click]
    > [Node 401]                                                 +-------------------------+
  > SimHost                                                      | Copy Content            |
    > [Node 1]                                                   | Copy NAS Path           |
  > ExCon                                                        | Open from NAS...        |
    > [Node 500]                                                 | Save Local Copy As...   |
                                                                 +-------------------------+
```

3\. Event Browser UI Wireframe (Corrected)

The `EventBrowserPanel` will retain its current visual implementation. The first column will display the frame number and the short type name (e.g., `DestructionOrder`), mapped from `inspector.EventType.Name`. The FQN is completely omitted from the display layer and is only utilized internally when constructing the JSON payload.

```
=============================================================================
[X] Event Browser                                                       [X]
=============================================================================
  [ Bus: World v ] [ Clear ] [ ] Pause  [ Filter... ] | Showing 500 / 500

  +-------------------------------------------------------------------------+
  | Frame/Type                                  | Summary                   |
  +-------------------------------------------------------------------------+
  |  OwnershipUpdate                      | Entity...                 |
  |  SpawnEntityCommand                   | 401...                    | (Selected)
  |  WeaponFireIntent                     | 401...                    | (Selected)
  |  DestructionOrder                     | Entity: [1, v1], Re...    | [Right-Click]
  | ...                                         |                           +--------------+
  +-------------------------------------------------------------------------+ Copy to JSON |
                                                                            +--------------+
```

The multi-select copy operation will iterate over the selection and execute the serialization, maintaining the strict requirement to flatten arrays via the shared formatting utility while handling the `FixedString64` converter correctly.

Ok. what about the local "Save local copy as..." implementation? We might need some reusable Save As dialog as we are already saving scenario files in the editor as we want to be DRY.

The current implementation of the scenario "Save As" functionality is not a generic file dialog that we can reuse directly. The `ScenarioBrowserPanel` implements its "Save As" feature using a custom ImGui popup modal that merely prompts the user for a scenario name via a text input field. The `EditorApplication` then resolves this name against a predefined root directory (`EditorBootstrap.ScenariosRoot`) to construct the final save path.

This design is tightly coupled to the scenario domain and does not support arbitrary filesystem navigation. Reusing it for diagnostic dumps would violate the Single Responsibility Principle and provide a poor user experience, as operators need to be able to choose arbitrary local paths for diagnostic files.

To support "Save local copy as..." while maintaining clean architecture, we must introduce a dedicated file dialog abstraction:

-   **Interface Segregation:** Define an `IFileDialogService` interface in the Presentation abstraction layer. This interface should expose a method like `TryGetSaveFilePath(string defaultFileName, string extensionFilter, out string selectedPath)`.-   **Implementation:** Since the application is a .NET 8 desktop application relying on Raylib and ImGui, we lack a built-in OS file dialog in the immediate-mode GUI stack. The implementation of `IFileDialogService` should either wrap a lightweight native OS dialog library or utilize a generic, domain-agnostic ImGui file browser component.-   **UI Decoupling:** In the Diagnostics panel, the context menu's "Save Local Copy As..." action will invoke this service. Upon receiving a valid path, it will perform a standard file copy from the aggregated `FileManifestEntry.SourceUnc` (the NAS location) to the selected local path.

This approach guarantees we do not contaminate the scenario authoring UI with generic OS filesystem concerns and prevents tightly coupling the diagnostic tooling to the scenario directory structure.

correct, scenario is not saved as local copy so we can not share it. You are right, we need some ImGui Save As dialog.

To implement a reusable ImGui "Save As" dialog without violating the immediate-mode GUI paradigm, we must bridge the synchronous 60Hz render loop with an asynchronous request pipeline. This mirrors the exact pattern already established by `IMapPickService` and `MapPickServiceBridge` for map interactions.

Here is the architectural design to implement this cleanly.

1\. The Service Contract

Define an asynchronous interface in the presentation abstraction layer. This ensures subsystems (like the Diagnostics panel) can request a file path without knowing how the dialog is rendered.

```
namespace Fdp.Presentation.Abstractions
{
    public interface IFileDialogService
    {
        /// <summary>
        /// Requests a local file path from the operator.
        /// Returns null if the operation is cancelled.
        /// </summary>
        Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter);
    }
}
```

2\. The ImGui Dialog State Machine

An immediate-mode file browser requires persistent state across frames (current directory, file list, input buffer). We encapsulate this state and the `TaskCompletionSource` inside a dedicated implementation.

```
using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Presentation.Abstractions;
using ImGuiNET;

namespace Fdp.Presentation.ImGui.Windows
{
    public sealed class ImGuiFileDialogService : IFileDialogService
    {
        private bool _isOpen;
        private string _currentDirectory;
        private string _fileNameBuffer = string.Empty;
        private string _extensionFilter = string.Empty;
        private TaskCompletionSource<string?>? _tcs;

        public ImGuiFileDialogService()
        {
            _currentDirectory = Directory.GetCurrentDirectory();
        }

        public Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter)
        {
            if (_isOpen && _tcs != null)
            {
                _tcs.TrySetCanceled();
            }

            _fileNameBuffer = defaultFileName;
            _extensionFilter = extensionFilter;
            _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _isOpen = true;

            return _tcs.Task;
        }

        /// <summary>
        /// Must be called every frame within the ImGui render loop.
        /// </summary>
        public void Draw()
        {
            if (!_isOpen) return;

            // Force the popup to open if it transitioned to true this frame
            ImGui.OpenPopup("Save Local Copy As...");

            Vector2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);

            if (ImGui.BeginPopupModal("Save Local Copy As...", ref _isOpen, ImGuiWindowFlags.NoCollapse))
            {
                DrawDirectoryNavigator();
                DrawFileList();
                DrawFileNameInputAndButtons();
                ImGui.EndPopup();
            }

            // Handle ImGui 'x' close button
            if (!_isOpen && _tcs != null && !_tcs.Task.IsCompleted)
            {
                _tcs.TrySetResult(null);
                _tcs = null;
            }
        }

        private void DrawDirectoryNavigator()
        {
            ImGui.Text("Directory:");
            ImGui.SameLine();
            ImGui.TextDisabled(_currentDirectory);

            if (ImGui.Button("Up"))
            {
                var parent = Directory.GetParent(_currentDirectory);
                if (parent != null) _currentDirectory = parent.FullName;
            }
            ImGui.Separator();
        }

        private void DrawFileList()
        {
            if (ImGui.BeginChild("##file_list", new Vector2(0, -35), ImGuiChildFlags.Borders))
            {
                foreach (var dir in Directory.GetDirectories(_currentDirectory))
                {
                    if (ImGui.Selectable($"[DIR] {Path.GetFileName(dir)}", false, ImGuiSelectableFlags.AllowDoubleClick))
                    {
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            _currentDirectory = dir;
                        }
                    }
                }

                foreach (var file in Directory.GetFiles(_currentDirectory, $"*{_extensionFilter}"))
                {
                    if (ImGui.Selectable(Path.GetFileName(file)))
                    {
                        _fileNameBuffer = Path.GetFileName(file);
                    }
                }
                ImGui.EndChild();
            }
        }

        private void DrawFileNameInputAndButtons()
        {
            ImGui.InputText("File Name", ref _fileNameBuffer, 256);
            
            if (ImGui.Button("Save") && !string.IsNullOrWhiteSpace(_fileNameBuffer))
            {
                string fullPath = Path.Combine(_currentDirectory, _fileNameBuffer);
                // Enforce extension
                if (!fullPath.EndsWith(_extensionFilter, StringComparison.OrdinalIgnoreCase))
                {
                    fullPath += _extensionFilter;
                }

                _tcs?.TrySetResult(fullPath);
                _isOpen = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _tcs?.TrySetResult(null);
                _isOpen = false;
            }
        }
    }
}
```

3\. Orchestration Integration

The `ImGuiFileDialogService` must be registered in the composition root (e.g., `Program.cs` or the `SubsystemOrchestrator` bootstrapping phase) and its `Draw()` method must be invoked globally, outside of any specific subsystem window.

The application's `WindowManager` (`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`) is the correct place to host this instance, ensuring it is drawn last and overlays all other windows.

```
// Inside WindowManager.Render()
public void Render()
{
    // ... existing MainMenuBar and DockSpace logic

    // Render registered managed windows
    foreach (var window in _windows.Values)
    {
        window.Render(CurrentPerspective, _iconAtlas);
    }

    // Render the global modal dialogs on top of everything
    FileDialogService.Draw();
}
```

4\. Consumer Implementation (Diagnostics Panel)

The Diagnostics UI can now safely request a local save location. The asynchronous execution cleanly separates the 60Hz frame from the disk I/O.

```
private async void HandleSaveLocalCopyAs(FileManifestEntry entry, IFileDialogService fileDialog)
{
    string extension = entry.RelativeDest.EndsWith(".md") ? ".md" : Path.GetExtension(entry.RelativeDest);
    string? targetPath = await fileDialog.ShowSaveAsDialogAsync(Path.GetFileName(entry.RelativeDest), extension);

    if (targetPath != null)
    {
        try
        {
            File.Copy(entry.SourceUnc, targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // Log failure or push error to UI alert system
            FdpLog<ClusterScenarioPanel>.Error("Failed to save local copy: {0}", ex.Message);
        }
    }
}
```

pls write a mermaid diagram explaining the mechanics of processing the dump from user request in Excon via request processing on orchestrator and local handling on the nodes down to gathering the results by the master and presenting them in excon, allowing fo local view/save etc.

Systém nedokázal odpovědět.

pls write a mermaid diagram explaining the mechanics of processing the dump from user request in Excon via request processing on orchestrator and local handling on the nodes down to gathering the results by the master and presenting them in excon, allowing fo local view/save etc.

```
sequenceDiagram
    participant Op as Operator (ExCon UI)
    participant ExCon as ExCon Node
    participant DDS as CycloneDDS Wire
    participant Orch as Orchestrator (ClusterMaster)
    participant NAS as Central NAS
    participant Node as Target Node (SimHost/CGF)

    %% 1. Intent & Egress
    Op->>ExCon: Trigger Cluster Dump (Matrix/Filters)
    ExCon->>ExCon: Publish ClusterOpIntent(DumpDiagnostics)
    Note over ExCon: ClusterOpEgressTranslator
    ExCon->>DDS: Write ClusterOpRequest

    %% 2. Orchestration Ingress & Fan-out
    Note over Orch: ClusterOpMasterTranslator
    DDS->>Orch: Read ClusterOpRequest
    Orch->>Orch: Publish ExecuteDiagnosticDumpIntent
    Note over Orch: ClusterMaster 2PC Fan-out
    Orch->>Orch: Publish ExecuteNodeOpIntent(TargetNodeId)
    Note over Orch: NodeOpMasterTranslator
    Orch->>DDS: Write NodeOpCommand

    %% 3. Node-Side Execution
    Note over Node: NodeOpSlaveTranslator
    DDS->>Node: Read NodeOpCommand
    Node->>Node: Publish ExecuteNodeOpIntent
    Note over Node: DiagnosticsDumpClusterOpHandler
    Node->>Node: PrepareAsync() (Background Thread)
    Node->>Node: Extract Entities/Events/Logs
    Node->>Node: Write to LocalTempRoot
    Node->>Node: Publish NodeOpCompletedEvent (FileManifestEntry[])
    Note over Node: NodeOpSlaveTranslator
    Node->>DDS: Write NodeOpStatus

    %% 4. Aggregation & NAS Pull
    Note over Orch: NodeOpMasterTranslator
    DDS->>Orch: Read NodeOpStatus
    Orch->>Orch: Publish NodeOpCompletedEvent
    Note over Orch: DiagnosticsConsensusAggregator
    Orch->>Orch: Aggregate FileManifestEntry[]
    Note over Orch: StorageProcessManager
    Orch->>NAS: StorageGatewayModule.PullToNasAsync()
    Note right of Orch: SMB parallel pull from node's LocalTempRoot
    NAS-->>Orch: Files successfully copied

    %% 5. Completion & UI Update
    Note over Orch: ClusterMaster
    Orch->>DDS: Write ClusterOpStatus(Success)
    Note over ExCon: OrchestrationObserverTranslator
    DDS->>ExCon: Read ClusterOpStatus
    ExCon->>ExCon: Publish ClusterOpCompletedEvent
    Note over ExCon: ClusterUiCache
    ExCon->>ExCon: Update ActiveTransaction State
    ExCon-->>Op: Display Results Tree

    %% 6. Local Save
    Op->>ExCon: Right-Click -> "Save Local Copy As..."
    Note over ExCon: ImGuiFileDialogService
    ExCon->>Op: ShowSaveAsDialogAsync()
    Op->>ExCon: Provide Local Path
    ExCon->>NAS: File.Copy(SourceUnc, targetPath)
```

The workflow strictly adheres to the established Command Query Responsibility Segregation (CQRS) and Two-Phase Commit (2PC) patterns, ensuring domain logic remains decoupled from the DDS transport layer.

**1\. Operator Request & Egress** The operator initiates the dump via the ExCon UI, which publishes a `ClusterOpIntent` onto the local `FdpEventBus`. The `ClusterOpEgressTranslator` intercepts this managed event, serializes the domain payload (the matrix selections), and writes a `ClusterOpRequest` to the DDS wire.

**2\. Orchestration Ingress & Fan-Out** On the Orchestrator, the `ClusterOpMasterTranslator` reads the `ClusterOpRequest` and publishes a corresponding strongly-typed intent onto the master's event bus. The `ClusterMaster` processes this intent and fans out the operation by publishing `ExecuteNodeOpIntent` for each target node. The `NodeOpMasterTranslator` converts these intents into `NodeOpCommand` DDS messages.

**3\. Node Execution** Target nodes receive the `NodeOpCommand` via their `NodeOpSlaveTranslator`, which deserializes the payload and pushes an `ExecuteNodeOpIntent` to the node's local bus. The local `DiagnosticsDumpClusterOpHandler` executes the extraction logic in a background task (to avoid stalling the 60Hz loop) and writes the resulting JSON, Markdown, and Log files to the node's `LocalTempRoot`. The handler returns a `FileManifestEntry` array, which the slave translator packs into a `NodeOpStatus` DDS message and sends back to the Orchestrator.

**4\. Aggregation & NAS Pull** The Orchestrator's `NodeOpMasterTranslator` receives the `NodeOpStatus` messages and translates them into `NodeOpCompletedEvent` bus messages. The consensus aggregator flattens the `FileManifestEntry` arrays from all nodes. A process manager observes the completed transaction and invokes `StorageGatewayModule.PullToNasAsync`, leveraging the SMB Pull Gateway pattern to securely copy files from the remote nodes to the central NAS concurrently.

**5\. State Synchronization & Presentation** Upon completion of the NAS pull, the Orchestrator writes a terminal `ClusterOpStatus` to DDS. ExCon's `OrchestrationObserverTranslator`, which listens promiscuously to cluster traffic, reads this status and updates the local `ClusterUiCache`. The UI reacts to the cache update, rendering the results tree.

**6\. Local Save Execution** When the operator requests a local copy, the UI invokes the asynchronous `IFileDialogService` state machine within the ImGui render loop. Once a valid path is returned, the system performs a standard `File.Copy` from the NAS (`SourceUnc`) directly to the operator's local disk, separating the filesystem I/O from the 60Hz UI frame execution.

how to approach the sharing/unification of the json serialization of events and components (using various custom formatters) and converting to pretty format with inlined arrays as required in IDEAS?

To achieve a unified, clean architecture for JSON serialization that satisfies the formatting constraints across events, entities, cluster dumps, and scenarios, we must decouple the serialization of data types from the aesthetic post-processing of the text output.

**1\. Extract the Newtonsoft Formatting Logic** Currently, the logic to collapse numeric arrays into a single line exists inside `ScenarioFileService.SaveScenario` via the `WriteFormattedToken` and `IsPureNumericArray` methods. Keeping this locked inside the scenario authoring module violates the DRY principle.

Extract these methods into a dedicated, globally accessible utility class (e.g., `JsonFormattingUtility` in a shared core library). This utility will take a raw JSON string, parse it into a `JToken` via `Newtonsoft.Json`, and traverse it. When it encounters a `JArray` where `IsPureNumericArray` is true, it will write the array continuously using `writer.WriteRawValue($"[{string.Join(", ", elements)}]")`.

**2\. Consolidate System.Text.Json Converters** The serialization bug where `FixedString64` outputs `{ "Length": 11, "IsEmpty": false }` occurs because the default `System.Text.Json` serializer blindly dumps the struct's properties. We already have the solution: `FixedString32Converter` and `FixedString64Converter` exist in `Fdp.Toolkit.Scenario.ScenarioJsonConverters`.

Move these string converters, along with the custom vector converters (`Vector2ArrayConverter`, `Vector3ArrayConverter`, `QuaternionArrayConverter`, etc.), into a centralized configuration. Add them to a globally shared options instance, such as `HrotSerializerOptions.HrotJsonOptions`, or the internal `_fieldAwareOptions`.

**3\. Establish a Two-Stage Pipeline** Force all subsystems (Event Browser, Entity Inspector, Scenario Saver, and Cluster Diagnostics) to use a unified two-stage pipeline for JSON extraction:

-   **Stage 1 (Data):** Serialize the object graph using `System.Text.Json.JsonSerializer` with the globally shared options instance containing the `FixedString` and `Vector` converters. This guarantees semantic correctness.-   **Stage 2 (Aesthetics):** Pass the resulting string into the newly extracted `Newtonsoft.Json` utility to enforce the single-line array formatting rules.

**4\. Refactor Existing Callers** Update the currently fragmented call sites to route through this pipeline:

-   Refactor `EntityJsonDumper.Dump`, which currently serializes directly via `System.Text.Json`.-   Refactor the `EventBrowserPanel`, which manually constructs dictionaries and serializes them using local `JsonSerializerOptions`.-   Wire the new cluster-wide diagnostic dump orchestration to pass all its extracted payloads through this exact same utility before saving to the central NAS.

pls describe the formats of the json files for different kind of dumps (events, entities, architecture diagnostics...)

The logs should keep the format as saved by the nodes but this format must be standardized to allow for later parsing and filtering and still be well visually separating the individual parts to stay human readable... Something like "\[time stamp\] \[loger name\] \[node id\] message text"...

The data models for the diagnostic dumps must be strictly typed and leverage the unified two-stage serialization pipeline to guarantee valid, readable JSON across all subsystems. Here are the precise schemas for each dump kind, followed by the standardized log format architecture.

1\. Events Dump Schema

The event dump captures the `EventAccumulator` history chronologically. It serializes an array of captured event objects, ensuring the custom converters for `FixedString64` and the post-processing for numeric arrays are applied.

```
[
  {
    "EventType": "Fdp.Toolkit.Replication.Messages.OwnershipUpdate",
    "Frame": 773,
    "Payload": {
      "EntityId": 4001,
      "NewOwnerId": 1
    }
  },
  {
    "EventType": "Fdp.Toolkit.Lifecycle.Events.DestructionOrder",
    "Frame": 774,
    "Payload": {
      "Entity": { "Index": 1, "Generation": 1 },
      "FrameNumber": 774,
      "Reason": "HealthDepleted" 
    }
  }
]
```

_Note: The_ _Reason_ _field will now correctly output the string value due to the newly globally registered_ _FixedString64Converter__._

2\. Entities Dump Schema

The entity dump extracts the requested entity states into an array. It utilizes the existing `EntityJsonDumper.Dump` logic, returning the internal generational indices alongside the flattened component dictionary. The payload implicitly includes `NetworkIdentity` as a standard component for cross-node correlation.

```
[
  {
    "EntityId": [7],
    "Components": {
      "NetworkIdentity": { "Value": 4001 },
      "SimTransform": { "Position": [10.5, 0.0, -5.2], "Rotation": [0.0, 0.0, 0.0, 1.0] },
      "Health": { "Current": 50.0, "Max": 100.0 }
    }
  }
]
```

_Note: The_ _Vector3_ _and_ _Quaternion_ _arrays are collapsed into single lines using the aesthetic post-processing utility__._

3\. Architecture Diagnostics Schema

The architecture dump aggregates the live diagnostic profiles of the `ModuleHostKernel`. It maps directly to the `ModuleDiagnostics`, system profiling data, and translator metrics structures.

```
{
  "Modules": [
    {
      "ModuleName": "LiveKinematicsModule",
      "RunMode": "Synchronous",
      "DataStrategy": "Live",
      "TargetFrequencyHz": 60,
      "ExecutionCount": 4250,
      "CircuitState": "Closed",
      "FailureCount": 0
    }
  ],
  "Systems": [
    {
      "Phase": "Simulation",
      "SystemName": "CarKinematicsSystem",
      "AverageMs": 0.45,
      "MaxMs": 1.21,
      "TotalMs": 1912.5,
      "ErrorCount": 0
    }
  ],
  "Translators": [
    {
      "Topic": "EntityState",
      "Direction": "Egress",
      "ReceivedSampleCount": 0,
      "SentSampleCount": 15000
    }
  ]
}
```

4\. Standardized NLog Configuration

The requirement to filter massive log files on the node by age and severity dictates that the raw `.log` text must be predictably machine-parseable. Currently, NLog layouts differ across targets (e.g., `"${longdate}|${level:uppercase=true}|${logger}|tick=${event-properties:tick}| ${message}..."`).

To fulfill your request for the `[time stamp] [logger name] [node id] message text` format, we must update the NLog `LoggingConfiguration` across all applications.

_Architectural Deviation Notice:_ I am adding the `[LEVEL]` token to your requested format. This is standard practice and strictly required; without the severity level physically present on each line, the background thread cannot execute the `LogSeverityThreshold` filtering you requested.

The new standardized NLog layout string will be: `[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] [Node-${event-properties:nodeId}] ${message} ${exception:format=tostring}`

**Example Output:** `[2026-05-03 11:09:00.123] [INFO] [ReferenceCheckpointHandler] [Node-400] Commit: snapshot enqueued to I/O worker.` `[2026-05-03 11:09:01.456] [WARN] [EntityInfoIngressTranslator] [Node-1] Commander entity not found in NetworkEntityMap.`

The background task executing the dump can efficiently read this via a `StreamReader`, split by the bracketed tokens to evaluate the timestamp and log level, and stream the passing lines directly to the output dump file without memory exhaustion.

I forgot one extremely useful feature request - merging the logs from different nodes into one chronological stream. How to approach it? It could be a post-process on the orchestrator once it gets all logs from all nodes. He could save a new merged log file to NAS. This migth be time consuming but is it so usefull that it is worth the time and effort. Maybe we can implement this as optional operation that the user can request after the dumps are finished grabbing. We need to allow the usual "Save as", "Open", "Copy Content" operations.

Merging massive text logs across a distributed cluster requires strict memory discipline. Loading gigabytes of log text into the Orchestrator's memory to perform a standard `List<T>.Sort()` will trigger an `OutOfMemoryException` or provoke severe garbage collection pauses that stall the 60Hz simulation loop.

To implement this safely as an optional post-process, we must use a **streaming K-way merge algorithm** executed on a background thread.

1\. K-Way Merge Architecture (O(N) Memory Bound)

Since each individual node's NLog file is already chronologically sorted by definition, we do not need to sort the entire dataset. We only need to compare the current line of each file.

-   **Priority Queue:** We instantiate a `PriorityQueue<LogLineRef, DateTime>` where `N` is the number of log files (one per subsystem instance).-   **Streaming I/O:** The background task opens a `StreamReader` for each source log file on the NAS and a `StreamWriter` for the destination `dump_{DATETIME}_logs_MERGED.log`.-   **Execution:**
    -   Read the first line from every stream, parse the timestamp, and enqueue it.-   Dequeue the earliest line and write it to the merged stream.-   Read the next line from the `StreamReader` that produced the dequeued line and enqueue it.-   Repeat until all streams reach EOF.-   **Parsing:** Because we standardized the NLog layout to strictly start with `[yyyy-MM-dd HH:mm:ss.fff]`, we can use `ReadOnlySpan<char>` slicing to parse the timestamp without allocating substrings.

2\. Orchestrator Integration & Concurrency

The merge must be completely decoupled from the `ClusterMaster` main thread execution.

-   When the user triggers the merge, the UI publishes a `MergeLogsIntent` to the local event bus.-   A new headless service (e.g., `DiagnosticLogMergeWorker`) observes this intent and spawns a `Task.Run(..., TaskCreationOptions.LongRunning)` to execute the K-way merge against the NAS files.-   Upon completion, the worker publishes a `LogMergeCompletedEvent` containing the final NAS path.-   The `ClusterUiCache` consumes this event and updates the results tree.

3\. UI Wireframe Updates

We will add a dedicated action button that becomes available once the cluster dump finishes and log files are present. The resulting merged file will appear as a first-class citizen in the results tree, reusing the exact same context menu components you defined for individual files.

```
-----------------------------------------------------------------------------
[ Operation Progress & Results ]
  Status: COMPLETED (TxID: a1b2c3d4...)

  [ Generate Merged Cluster Log ] <--- Appears when operation completes

  v Cluster Aggregates
    |- Merged Logs: dump_20260503_110900_logs_MERGED.log         [Right-Click]
                                                                 +-------------------------+
  v CGF                                                          | Copy Content            |
    v [Node 400]                                                 | Copy NAS Path           |
      |- Entities: dump_20260503_110900_entities_CGF_400.json.md | Open from NAS...        |
      |- Logs:     dump_20260503_110900_logs_CGF_400.log         | Save Local Copy As...   |
                                                                 +-------------------------+
```

By constraining the merge to an on-demand, streaming background task, we fulfill the diagnostic requirement without compromising the orchestrator's performance characteristics or memory footprint.

The clusterrunner should take optional command line argument specifying the log file location. The log grabbing mechanism (the local handlers on nodes) should honor this log file path. Next to the log format enforcement (which must be done via code to force it equally on each node), the nlog must be also configured for auto-rotation of the logs.

Systém nedokázal odpovědět.

The clusterrunner should take optional command line argument specifying the log file location. The log grabbing mechanism (the local handlers on nodes) should honor this log file path. Next to the log format enforcement (which must be done via code to force it equally on each node), the nlog must be also configured for auto-rotation of the logs.

To implement this cleanly, we must push the log directory configuration down from the CLI arguments into the composition root, enforce the NLog layout and rotation policies purely in code (bypassing XML configurations), and propagate the resolved path to the node-side diagnostic handlers.

Here is the architectural implementation.

1\. CLI Configuration Extension

We extend `HrotRunnerConfiguration` to accept the optional log directory argument. Defaulting to a local `logs` directory maintains backward compatibility for local developer runs.

```
// In Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs
public class HrotRunnerConfiguration : Fdp.Toolkit.Runner.RunnerConfiguration
{
    // ... existing options

    [Option("log-dir", Required = false, HelpText = "Target directory for node log files and rotation archives")]
    public string LogDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs");
}
```

2\. Programmatic NLog Enforcement & Auto-Rotation

We modify the composition root in `Hrot.ClusterRunner/Program.cs` to explicitly define the `FileTarget`. This guarantees every node uses the exact same parser-friendly layout requested previously, while enforcing strict size-based log rotation.

```
// In Hrot/Runner/Hrot.ClusterRunner/Program.cs
// Called after CLI parsing, before subsystem initialization

string resolvedLogDir = Path.GetFullPath(config.LogDirectory);
Directory.CreateDirectory(resolvedLogDir);

// Base filename tied to the specific node ID to prevent locking conflicts in multi-process deployments
string baseLogFileName = $"node_{config.NodeId}.log";

var fileTarget = new FileTarget("logfile")
{
    FileName = Path.Combine(resolvedLogDir, baseLogFileName),
    // Auto-rotation settings: Roll over at 50MB, keep the last 10 archives
    ArchiveFileName = Path.Combine(resolvedLogDir, $"node_{config.NodeId}.{{#}}.log"),
    ArchiveNumbering = ArchiveNumberingMode.Rolling,
    MaxArchiveFiles = 10,
    ArchiveAboveSize = 50 * 1024 * 1024, 
    KeepFileOpen = true,
    ConcurrentWrites = false,
    // Standardized machine-parseable layout
    Layout = "[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] [Node-${event-properties:nodeId}] ${message} ${exception:format=tostring}"
};

logConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);
LogManager.Configuration = logConfig;
```

3\. Propagating the Path to the Diagnostic Handlers

The node-side diagnostic handler must know where to look for these files without relying on magic strings. We extend `HrotNodeConfig` to carry this directory.

```
// In Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeConfig.cs
public sealed class HrotNodeConfig
{
    // ... existing properties [3]
    
    /// <summary>
    /// Directory where this node writes its NLog files.
    /// Passed down to diagnostic dump handlers for log extraction.
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;
}
```

When `Program.cs` initializes the subsystems, it passes `resolvedLogDir` into this configuration. The builder subsequently registers the `DiagnosticsDumpClusterOpHandler` and provides the path via dependency injection.

4\. Background Log Grabbing Implementation

With the path injected, the background task in `DiagnosticsDumpClusterOpHandler` can now safely aggregate the active log and its rotation archives.

```
private void ExtractLogs(string targetDumpFilePath, float maxAgeHours, int severityThreshold)
{
    DateTime cutoffTime = DateTime.UtcNow.AddHours(-maxAgeHours);
    
    // NLog rolling archives use the format node_X.#.log. 
    // We grab the active file and all archives, sort them chronologically (oldest to newest based on LastWriteTime).
    var logFiles = Directory.GetFiles(_logDirectory, $"node_{_nodeId}*.log")
                            .Select(f => new FileInfo(f))
                            .OrderBy(f => f.LastWriteTimeUtc)
                            .ToList();

    using var writer = new StreamWriter(targetDumpFilePath, append: false);

    foreach (var fileInfo in logFiles)
    {
        // Skip files that haven't been modified since the cutoff
        if (fileInfo.LastWriteTimeUtc < cutoffTime) continue;

        using var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (TryParseSeverity(line, out int severity) && severity >= severityThreshold)
            {
                // Optional: We can also parse the exact timestamp per line here to strictly enforce the maxAgeHours,
                // but file-level LastWriteTimeUtc is usually sufficient for bulk extraction and avoids string allocation overhead.
                writer.WriteLine(line);
            }
        }
    }
}
```

By decoupling the file discovery from hardcoded paths and strictly enforcing the NLog `FileTarget` settings in the runner executable, the diagnostic extraction remains deterministic and handles the scale of massive, rotating log files cleanly.

please describe the architecture of the node, related to providing the dump information - including the event history service. pls describe the anatomy of the node handler that handles master's dump requests.

The node-level architecture relies on strict decoupling of the 60Hz deterministic simulation loop from the heavy I/O and CPU spikes inherent in diagnostic data extraction.

To fulfill the event history requirement without coupling domain data to the presentation layer, we introduce the `DiagnosticEventHistoryService`. This headless service executes continuously alongside the simulation, hooking into the `FdpEventBus`. It calls `GetDebugInspectors()` to access the bus's internal streams and utilizes `InspectReadBuffer()` to capture the transient frame data. The service maintains a thread-safe circular buffer strictly capped at 500 events, guaranteeing O(1) memory bounds while providing an immediate chronological snapshot for diagnostic dumps.

The `DiagnosticsDumpClusterOpHandler` is the core node-side execution unit, implementing the `IClusterStateHandler` interface to participate in the cluster's distributed Two-Phase Commit protocol. Because parsing rotation logs and serializing massive ECS graphs would stall the main thread, the handler isolates this work entirely. When the `ClusterSlave` invokes `PrepareAsync`, the handler immediately delegates the execution to a long-running background `Task`, keeping the simulation tick uninterrupted.

Inside the background task, the handler's anatomy is split into extraction, serialization, and manifest generation phases:

**Extraction:** The handler decodes the incoming `DiagnosticDumpPayloadDto` to determine the exact matrix requirements. It queries the `EntityRepository` to extract entity states, utilizing the `NetworkIdentity` component as the natural cross-node correlation key. For architectural profiles, it queries the `ModuleHostKernel` via `GetModuleDiagnostics()` and aggregates system profiling metrics. Log extraction is handled via streaming I/O; the task reads the active NLog file and rotation archives from the injected `HrotNodeConfig.LogDirectory`, parsing the standardized layout line-by-line to filter by severity and age thresholds without loading the entire gigabyte payload into memory.

**Serialization Pipeline:** To maintain clean architecture and adhere to DRY principles, the handler routes all extracted JSON payloads through a unified two-stage pipeline. Stage 1 handles semantic correctness by using `System.Text.Json` configured with globally registered custom converters for unmanaged types like `FixedString64`. Stage 2 enforces the aesthetic requirements by passing the output through a shared `Newtonsoft.Json` utility that collapses pure numeric arrays into single lines.

**Egress:** The serialized JSON, Markdown, and filtered log text are written directly to the node's `LocalTempRoot` using the strict `dump_{DATETIME}_{kind}_{SubsystemName}_{NodeId}.{ext}` file naming convention. Upon completion, the background task returns an array of `FileManifestEntry` records defining the generated source UNC paths and relative destinations. The `ClusterSlave` automatically packs this manifest into the `NodeOpCompletedEvent` and transmits it via DDS back to the orchestrator, formally handing off the files to the central SMB Pull Gateway.

pls write mermaid block diagram and sequence disagram showing how the node handles the dump requests, using the dump-data providing services

```
flowchart TD
    subgraph Node boundary
        Bus[FdpEventBus] --> |ExecuteNodeOpIntent| CS
        CS[ClusterSlave] --> |PrepareAsync| DH[DiagnosticsDumpClusterOpHandler]
        
        subgraph Background Execution
            Task((LongRunning Task))
        end
        
        DH -.-> |Spawns| Task
        
        subgraph Data Providers
            ER[EntityRepository]
            EHS[DiagnosticEventHistoryService]
            MHK[ModuleHostKernel]
            NLOG[NLog Rotation Archives]
        end
        
        Task --> |Query NetworkIdentity| ER
        Task --> |InspectReadBuffer| EHS
        Task --> |GetModuleDiagnostics| MHK
        Task --> |Stream Filter| NLOG
        
        subgraph Serialization Pipeline
            STJ[System.Text.Json\nStage 1: Semantic]
            NTS[Newtonsoft.Json\nStage 2: Aesthetic/Flattening]
        end
        
        ER & EHS & MHK --> STJ
        STJ --> NTS
        NTS --> LTR
        NLOG --> |Write stream| LTR
        
        LTR[(LocalTempRoot)]
    end
```
```
sequenceDiagram
    participant Bus as FdpEventBus
    participant Slave as ClusterSlave
    participant Handler as DiagnosticsDumpClusterOpHandler
    participant BG as Background Task (Thread Pool)
    participant Providers as Data Providers (ECS, Kernel, Logs)
    participant Disk as LocalTempRoot

    %% Intent Ingress
    Bus->>Slave: Read ExecuteNodeOpIntent(DumpDiagnostics)
    Note over Slave: Tick() execution on main thread
    Slave->>Handler: PrepareAsync(intent, ct)
    
    %% Background Task Handoff
    Handler->>BG: Task.Run(LongRunning)
    Handler-->>Slave: Task<object?> (Pending)
    Note right of Slave: Main 60Hz loop continues uninterrupted
    
    %% Concurrent Data Gathering
    Note over BG: Extraction Phase
    BG->>Providers: Extract specific entities (NetworkIdentity)
    BG->>Providers: Read EventAccumulator history (500 frames)
    BG->>Providers: Extract ModuleDiagnostics & System profiles
    BG->>Providers: Stream and filter NLog lines (Severity/Age)
    
    %% Serialization
    Note over BG: Serialization Phase
    BG->>BG: Serialize via System.Text.Json (Custom Converters)
    BG->>BG: Post-process via Newtonsoft.Json (Single-line Arrays)
    
    %% Disk I/O
    BG->>Disk: Write JSON, Markdown, and .log files
    Disk-->>BG: Files written
    BG-->>Slave: Returns FileManifestEntry[]
    
    %% Completion & Egress
    Note over Slave: Next Tick() evaluates pending prepare
    Slave->>Slave: Detect Task.IsCompleted
    Slave->>Handler: Commit(intent, repo)
    Slave->>Bus: Publish NodeOpCompletedEvent(FileManifestEntry[])
```

The node's execution architecture strictly adheres to the Two-Phase Commit (2PC) protocol managed by the `ClusterSlave`.

When the `ClusterSlave` receives an `ExecuteNodeOpIntent` for the diagnostic dump, it invokes `PrepareAsync` on the `DiagnosticsDumpClusterOpHandler`, which implements `IClusterStateHandler`. To guarantee the 60Hz deterministic simulation loop remains free from CPU or I/O stalls, `PrepareAsync` instantly delegates the heavy extraction and serialization workload to a long-running background task and returns the incomplete `Task` back to the slave.

The background task orchestrates the extraction phase across the local providers: fetching entity state from the `EntityRepository`, live profiling data from the `ModuleHostKernel`, and the transient event history from the headless `DiagnosticEventHistoryService` tapping into the `FdpEventBus`. It routes structured data through the shared two-stage JSON serialization pipeline and applies line-by-line streaming filters to the `NLog` archives.

All outputs are written to the node's `LocalTempRoot`. Once the background task completes, it yields a `FileManifestEntry` array. The `ClusterSlave` observes the completed task on a subsequent `Tick()`, triggers the handler's `Commit()` method, and publishes the `NodeOpCompletedEvent` to the local event bus to be shipped over CycloneDDS back to the orchestrator for aggregation.

pls define the interfaces for the dump-data-providing services. I guess these should be used by the nodes' UI visualization as well as the dump request handlers

To achieve strict decoupling and adhere to the Single Responsibility Principle, we must extract the data-gathering logic out of both the ImGui panels and the cluster orchestration handlers.

By defining a set of core diagnostic provider interfaces, both the local rendering loop (e.g., `ArchitectureDiagnosticsPanel`, `EventBrowserPanel`) and the background dump task (`DiagnosticsDumpClusterOpHandler`) will depend on the same abstractions. This eliminates duplicated reflection logic and guarantees that what the operator sees on screen perfectly matches what is exported to the NAS.

1\. Architecture Diagnostics Service

Currently, the `ArchitectureDiagnosticsPanel` gathers data by manually calling `kernel.GetModuleDiagnostics()`, querying `kernel.SystemScheduler.GetAllProfileData()`, and heavily utilizing reflection to find translators on systems. This logic must be abstracted into a dedicated service.

```
namespace Fdp.Core.Diagnostics
{
    public record ArchitectureSnapshotDto(
        IReadOnlyList<ModuleDiagnostics> Modules,
        IReadOnlyList<SystemProfileData> Systems,
        IReadOnlyList<TranslatorDiagnosticsDto> Translators
    );

    public record TranslatorDiagnosticsDto(
        string SystemName, 
        string Direction, 
        string Topic, 
        long Ordinal, 
        long ReceivedCount, 
        long SentCount,
        SystemProfileData Profile
    );

    /// <summary>
    /// Provides a unified, point-in-time snapshot of the node's architectural health.
    /// Shared by the ArchitectureDiagnosticsPanel and the cluster dump handler.
    /// </summary>
    public interface IArchitectureDiagnosticsService
    {
        ArchitectureSnapshotDto GetSnapshot();
    }
}
```

2\. Event History Service

The `EventBrowserPanel` currently captures events natively inside its own UI update loop. This violates clean architecture, as the diagnostic dump handler cannot access UI state. We extract this into a headless service that runs as a regular system or kernel hook.

```
namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// Abstracted representation of a captured event, replacing the private 
    /// EventBrowserPanel.CapturedEvent class.
    /// </summary>
    public record CapturedEventDto(
        uint Frame, 
        string TypeName, 
        bool IsManaged, 
        string Summary, 
        object? RawEvent
    );

    /// <summary>
    /// Headless service that continuously taps the FdpEventBus to maintain a capped 
    /// chronological buffer of events.
    /// </summary>
    public interface IDiagnosticEventHistoryService
    {
        /// <summary>
        /// Returns a thread-safe snapshot of the current event buffer.
        /// </summary>
        IReadOnlyList<CapturedEventDto> GetHistory();

        /// <summary>
        /// Clears the current history buffer.
        /// </summary>
        void ClearHistory();
    }
}
```

3\. Entity State Extraction Service

The entity extraction logic requires resolving global `NetworkIdentity` values to local `Entity` handles using the `NetworkEntityMap`, and reading their current state via `IInspectableSession` or `EntityRepository`.

```
namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// Represents the flattened state of a single entity, ready for the two-stage 
    /// JSON serialization pipeline.
    /// </summary>
    public record EntityStateDumpDto(
        long NetworkId,
        int LocalIndex,
        int LocalGeneration,
        Dictionary<string, object> Components
    );

    /// <summary>
    /// Service responsible for gathering entity states, abstracting away the 
    /// NetworkEntityMap resolution and component dictionary extraction.
    /// </summary>
    public interface IEntityStateExtractionService
    {
        /// <summary>
        /// Extracts the state of specific entities by network ID. 
        /// If networkIds is null or empty, extracts all network-identity entities.
        /// </summary>
        IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds);
    }
}
```

4\. Log Archive Extraction Service

Unlike the other subsystems, the logs are not kept entirely in memory. The node relies on NLog auto-rotation to disk. The UI uses an in-memory `IMessageLogSource` for live viewing, but the dump handler must stream historical files. A dedicated service encapsulates the disk I/O and filtering logic, ensuring it can be safely called from a background `Task`.

```
namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// Encapsulates the streaming I/O required to merge and filter massive NLog 
    /// rotation archives without allocating them into RAM.
    /// </summary>
    public interface ILogArchiveExtractionService
    {
        /// <summary>
        /// Streams active and archived node logs to a target destination, 
        /// filtering by severity and maximum age.
        /// </summary>
        /// <param name="targetFilePath">The output dump file path in the LocalTempRoot.</param>
        /// <param name="severityThreshold">Minimum NLog severity to include.</param>
        /// <param name="maxAgeHours">Maximum age of log entries to include.</param>
        /// <param name="ct">Cancellation token for aborting long-running extracts.</param>
        Task ExtractLogsAsync(
            string targetFilePath, 
            int severityThreshold, 
            float maxAgeHours, 
            CancellationToken ct);
    }
}
```

Implementation & Wiring

These interfaces should be implemented in `Fdp.Core.Diagnostics` and injected via the composition root (e.g., inside `HrotNodeBuilder` or `NodeBootstrapper`).

-   The `ArchitectureDiagnosticsPanel` and `EventBrowserPanel` will be refactored to take `IArchitectureDiagnosticsService` and `IDiagnosticEventHistoryService` in their constructors, replacing their direct coupling to the `ModuleHostKernel` and `FdpEventBus`.-   The `DiagnosticsDumpClusterOpHandler` will inject all four interfaces. When `PrepareAsync` executes on its background thread, it will simply call `GetSnapshot()`, `GetHistory()`, `ExtractEntities()`, and `ExtractLogsAsync()`, passing the resulting DTOs into the shared JSON flattening utilities before writing to the NAS directory.

what the node's dump request handler implementation would look like conceptually?

Systém nedokázal odpovědět.

what the node's dump request handler implementation would look like conceptually?

The node's dump request handler acts as a participant in the cluster's two-phase commit (2PC) protocol by implementing the `IClusterStateHandler` interface. To preserve the deterministic simulation loop, the handler strictly isolates CPU-bound serialization and I/O workloads from the main thread by performing all extraction during the asynchronous `PrepareAsync` phase. When the `ClusterSlave` invokes this method, the handler delegates execution to a background task and returns the incomplete task, allowing the slave to defer the `Commit` call to a subsequent tick once the background work finishes.

Within the background task, the handler orchestrates data extraction by querying dedicated headless services. If entity state correlation is requested, it relies on the `NetworkIdentity` component as the cluster-wide correlation key. For architecture diagnostics, it extracts execution counts, circuit states, and system profiling metrics directly from the `ModuleHostKernel`,. Log extraction avoids massive memory spikes by streaming the rotated NLog archives directly from disk and applying age and debug-level threshold filters line-by-line.

All extracted objects are routed through a unified two-stage serialization pipeline to adhere to DRY principles. Stage one ensures semantic correctness by utilizing `System.Text.Json` with globally registered custom formatters, while stage two applies aesthetic post-processing via `Newtonsoft.Json` to collapse pure numeric arrays into single lines. The resulting JSON, optional Markdown wrappers, and filtered logs are written directly to the node's local temporary staging directory using a strict naming convention that includes the orchestrator's timestamp, dump kind, subsystem name, and node ID,,.

Upon successful disk I/O, the background task returns an array of `FileManifestEntry` records, each defining the source UNC path and the relative destination on the NAS. The `ClusterSlave` observes the completed task, executes the synchronous (but fast) `Commit` method, and packages the manifest array into the `ResultPayload` of a `NodeOpCompletedEvent`,,. This event is shipped over CycloneDDS back to the orchestrator, where the `StorageConsensusAggregator` flattens the manifests from all participating nodes into a single array for the final parallel NAS pull,. If the orchestrator cancels the distributed transaction mid-flight, the `Abort` method is invoked to roll back the operation by deleting any transient files written to the local disk.

could you pls be more specific in showing the example handler, using pseudocode?

Systém nedokázal odpovědět.

could you pls be more specific in showing the example handler, using pseudocode?

The `DiagnosticsDumpClusterOpHandler` implements the `IClusterStateHandler` interface to participate in the cluster's two-phase commit protocol. To preserve the 60Hz deterministic simulation loop, all heavy extraction, formatting, and disk I/O are strictly isolated to a background task spawned during the `PrepareAsync` phase.

Observe the separation of concerns: the handler manages the orchestration protocol and file manifesting, while the injected services handle the domain-specific data extraction.

```
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Core.Diagnostics
{
    public sealed class DiagnosticsDumpClusterOpHandler : IClusterStateHandler
    {
        private readonly IEntityStateExtractionService _entityService;
        private readonly IDiagnosticEventHistoryService _eventService;
        private readonly IArchitectureDiagnosticsService _archService;
        private readonly ILogArchiveExtractionService _logService;
        
        private readonly string _localTempRoot;
        private readonly string _subsystemName;
        private readonly int _nodeId;

        public DiagnosticsDumpClusterOpHandler(
            IEntityStateExtractionService entityService,
            IDiagnosticEventHistoryService eventService,
            IArchitectureDiagnosticsService archService,
            ILogArchiveExtractionService logService,
            HrotNodeConfig nodeConfig)
        {
            _entityService = entityService;
            _eventService = eventService;
            _archService = archService;
            _logService = logService;
            
            _localTempRoot = nodeConfig.LocalTempRoot;
            _subsystemName = nodeConfig.SubsystemName;
            _nodeId = nodeConfig.NodeId;
        }

        public bool CanHandle(NodeOpType operation) 
            => operation == NodeOpType.DumpDiagnostics; // Requires extending NodeOpType enum

        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            var dto = JsonSerializer.Deserialize<DiagnosticDumpPayloadDto>(
                intent.PayloadJson, HrotJsonOptions.Default);

            // Guard: Ignore if this node was not selected in the matrix
            if (dto.TargetNodeIds != null && !dto.TargetNodeIds.Contains(_nodeId))
            {
                return Task.FromResult<object?>(null);
            }

            // Isolate CPU/IO bounds from the main thread
            return Task.Run(async () => await ExecuteDumpAsync(dto, intent.TransactionId, ct), ct);
        }

        private async Task<object?> ExecuteDumpAsync(
            DiagnosticDumpPayloadDto dto, 
            Guid transactionId, 
            CancellationToken ct)
        {
            var manifests = new List<FileManifestEntry>();
            
            // Use transaction ID or explicit timestamp from DTO to guarantee cluster-wide filename alignment
            string timestamp = dto.RequestTimestamp.ToString("yyyyMMdd_HHmmss");
            string outputDir = Path.Combine(_localTempRoot, "dumps", transactionId.ToString());
            Directory.CreateDirectory(outputDir);

            // 1. Entity Dump
            if (dto.DumpEntities)
            {
                var entities = _entityService.ExtractEntities(dto.SpecificNetworkIds);
                string path = WriteJsonPayload(entities, "entities", timestamp, outputDir, dto.UseMarkdownWrapper);
                manifests.Add(CreateManifest(path));
            }

            // 2. Event History Dump
            if (dto.DumpEvents)
            {
                // Service returns pre-captured O(1) buffer [3, 4]
                var events = _eventService.GetHistory(dto.EventProviders);
                string path = WriteJsonPayload(events, "events", timestamp, outputDir, dto.UseMarkdownWrapper);
                manifests.Add(CreateManifest(path));
            }

            // 3. Architecture Dump
            if (dto.DumpArchitecture)
            {
                var arch = _archService.GetSnapshot();
                string path = WriteJsonPayload(arch, "arch", timestamp, outputDir, dto.UseMarkdownWrapper);
                manifests.Add(CreateManifest(path));
            }

            // 4. Log Extraction (Streaming I/O)
            if (dto.DumpLogs)
            {
                string logFileName = $"dump_{timestamp}_logs_{_subsystemName}_{_nodeId}.log";
                string logFilePath = Path.Combine(outputDir, logFileName);
                
                await _logService.ExtractLogsAsync(
                    logFilePath, dto.LogSeverityThreshold, dto.LogAgeHours, ct);
                    
                manifests.Add(CreateManifest(logFilePath));
            }

            // Returns the payload for NodeOpCompletedEvent, later aggregated by Orchestrator [5]
            return manifests.ToArray(); 
        }

        private string WriteJsonPayload(
            object data, 
            string kind, 
            string timestamp, 
            string outputDir, 
            bool wrapInMarkdown)
        {
            // Stage 1: Semantic Serialization (Custom formatters applied via HrotJsonOptions)
            string rawJson = JsonSerializer.Serialize(data, HrotJsonOptions.Default);

            // Stage 2: Aesthetic Post-Processing
            string formattedJson = JsonFormattingUtility.FlattenNumericArrays(rawJson);

            if (wrapInMarkdown)
            {
                formattedJson = $"```json\n{formattedJson}\n```";
            }

            string extension = wrapInMarkdown ? "json.md" : "json";
            string fileName = $"dump_{timestamp}_{kind}_{_subsystemName}_{_nodeId}.{extension}";
            string fullPath = Path.Combine(outputDir, fileName);

            File.WriteAllText(fullPath, formattedJson);
            return fullPath;
        }

        private FileManifestEntry CreateManifest(string absolutePath)
        {
            // Translates absolute path to the NAS-relative structure required by StorageGatewayModule [5, 6]
            return new FileManifestEntry
            {
                SourceUnc = absolutePath,
                RelativeDest = Path.Combine("dumps", Path.GetFileName(absolutePath))
            };
        }

        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            // Clean architectural pattern: local disk files are securely staged during PrepareAsync.
            // On Commit, the NodeOpCompletedEvent triggers the orchestrator to initiate the NAS pull.
        }

        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            // Discard the staged directory if orchestrator aborts the 2PC transaction.
            string targetDir = Path.Combine(_localTempRoot, "dumps", intent.TransactionId.ToString());
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
        }
    }
}
```

How to ensure all the json serializers share the same set of settings which are defined centrally? There might be more than one set of settings for different oportunities, but i would like to keep those centralle somewhere in FDP shared place. I guess not the json serializer settings are scattered all over the code base.

The current codebase violates the DRY principle regarding JSON serialization configurations. Options are heavily fragmented across layers: `FdpAutoSerializer` hides a private `_fieldAwareOptions` instance, `MetadataSerializer` maintains its own private `_options`, `HrotSerializerOptions.HrotJsonOptions` is defined at the application layer, and `OrchestrationJsonOptions.Default` manages orchestration DTOs. This scattering directly causes bugs like the `FixedString64` serialization failure in the event browser, as the UI does not have access to the scenario toolkit's hidden converters.

To resolve this and enforce clean architecture, we must consolidate the serialization profiles into the `Fdp.Core` engine layer, making them universally accessible to all toolkits, UI panels, and diagnostic handlers.

1\. Relocate Custom Converters to Core

Currently, critical unmanaged type converters like `Vector3ArrayConverter`, `QuaternionArrayConverter`, and `FixedString64Converter` reside in `Fdp.Toolkit.Scenario.ScenarioJsonConverters`. These must be moved down into a new namespace: `Fdp.Core.Serialization.Converters`. This guarantees that low-level engine constructs, the `FdpEventBus` inspectors, and the event browser can serialize unmanaged ECS memory without creating circular dependencies on the Scenario toolkit.

2\. Establish the Central Options Registry

Create a static registry class, `FdpJsonOptionsRegistry`, within `Fdp.Core.Serialization`. This class will expose immutable, pre-configured `JsonSerializerOptions` singletons tailored to the specific serialization contexts required by the engine.

```
namespace Fdp.Core.Serialization
{
    public static class FdpJsonOptionsRegistry
    {
        /// <summary>
        /// Baseline configuration for serializing ECS component state.
        /// Replaces FdpAutoSerializer._fieldAwareOptions.
        /// </summary>
        public static JsonSerializerOptions ComponentStateDefault { get; }

        /// <summary>
        /// Human-readable formatting for UI clipboard operations and diagnostic dumps.
        /// </summary>
        public static JsonSerializerOptions ComponentStateIndented { get; }

        static FdpJsonOptionsRegistry()
        {
            ComponentStateDefault = new JsonSerializerOptions
            {
                IncludeFields = true, // Required for Vector/Quaternion structs
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters =
                {
                    new Vector2ArrayConverter(),
                    new Vector3ArrayConverter(),
                    new Vector4ArrayConverter(),
                    new QuaternionArrayConverter(),
                    new FixedString32Converter(),
                    new FixedString64Converter(),
                    new JsonStringEnumConverter()
                }
            };

            ComponentStateIndented = new JsonSerializerOptions(ComponentStateDefault)
            {
                WriteIndented = true
            };
        }
    }
}
```

3\. Centralize the Newtonsoft Post-Processor

The requirement to flatten purely numeric arrays into single lines (e.g., `[0.0, 1.0, 0.0]`) is an aesthetic post-processing step currently tightly coupled to `ScenarioFileService.SaveScenario`.

Extract this logic into a globally accessible utility: `Fdp.Core.Serialization.JsonAestheticFormatter`. This utility will accept a raw JSON string, parse it via `Newtonsoft.Json.Linq.JToken`, apply the flattening rules, and return the formatted string. Both the scenario saver and the diagnostic dump handlers will route their outputs through this single class.

4\. Refactor Existing Call Sites

With the core registry established, systematically strip out the scattered configurations:

-   **FdpAutoSerializer****:** Delete `_fieldAwareOptions` and point `SerializeFieldToNode` and `DeserializeNode` to `FdpJsonOptionsRegistry.ComponentStateDefault`.-   **Event Browser & Entity Inspector:** Update the "Copy to JSON" UI commands to serialize objects using `FdpJsonOptionsRegistry.ComponentStateIndented`, then pass the result through `JsonAestheticFormatter`. This immediately resolves the `FixedString64` serialization bug.-   **Diagnostic Dumps:** The background tasks executing the cluster diagnostic dumps will use the exact same two-stage pipeline (System.Text.Json with `ComponentStateIndented` followed by `JsonAestheticFormatter`) before writing to the NAS.

This architecture guarantees that any future custom types or serialization rules added to the ECS engine will immediately inherit proper formatting and extraction logic across all tooling, UI, and networking boundaries.

also the deserializer settings should be centralized; usually we want relaxed rules like case insensitive fields, allow using fields and not just properties...

The codebase currently suffers from severe configuration duplication regarding JSON options. Classes like `HrotSerializerOptions`, `OrchestrationJsonOptions`, `MetadataSerializer`, and `FdpAutoSerializer` all independently instantiate their own `JsonSerializerOptions`. They repeatedly define identical relaxed behaviors such as `PropertyNameCaseInsensitive = true` and `IncludeFields = true`. This violates the DRY principle and creates a fragile architecture where deserialization behavior changes depending on which subsystem parses the payload.

Furthermore, `IncludeFields = true` is strictly required across the board, as ECS components and core engine math types (like `Vector3` and `Quaternion`) rely on public unmanaged fields rather than properties.

To centralize both serialization and relaxed deserialization rules cleanly, the `FdpJsonOptionsRegistry` must define an immutable baseline configuration that consolidates all required behaviors.

Here is the architectural implementation for the registry:

```
namespace Fdp.Core.Serialization
{
    public static class FdpJsonOptionsRegistry
    {
        /// <summary>
        /// Baseline configuration for robust deserialization and standard serialization.
        /// Applies relaxed parsing rules required across the cluster.
        /// </summary>
        public static JsonSerializerOptions DefaultRelaxed { get; }

        /// <summary>
        /// Human-readable formatting for UI clipboard operations and diagnostic dumps.
        /// </summary>
        public static JsonSerializerOptions Indented { get; }

        static FdpJsonOptionsRegistry()
        {
            DefaultRelaxed = new JsonSerializerOptions
            {
                // Relaxed Deserialization Rules
                PropertyNameCaseInsensitive = true,
                IncludeFields = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                
                // Standardization
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                
                // Centralized Converters for Unmanaged / Engine Types
                Converters =
                {
                    new Vector2ArrayConverter(),
                    new Vector3ArrayConverter(),
                    new Vector4ArrayConverter(),
                    new QuaternionArrayConverter(),
                    new FixedString32Converter(),
                    new FixedString64Converter(),
                    new StrictStringEnumConverter() // Replacing the default enum converter
                }
            };

            Indented = new JsonSerializerOptions(DefaultRelaxed)
            {
                WriteIndented = true
            };
        }
    }
}
```

Architectural Mandates for Migration

-   **Enforce** **IncludeFields** **Globally:** Applying `IncludeFields = true` ensures that both serialization and deserialization target fields natively. This eliminates the need for workarounds in the current code that fail to read raw structs during deserialization.-   **Standardize Relaxed Parsing:** Features like `AllowTrailingCommas = true` and `ReadCommentHandling = JsonCommentHandling.Skip` exist in isolated parts of the codebase (like the BTree compiler or Metadata tools). Pushing these to `DefaultRelaxed` guarantees that any JSON payload (whether an Orchestration DTO, a scenario file, or a diagnostic dump) can be safely parsed even if manually edited by an operator.-   **Enum Safety:** Incorporating the `StrictStringEnumConverter` currently locked inside `OrchestrationJsonOptions` prevents the silent integer-as-enum parsing bugs across the entire application domain.

All existing `new JsonSerializerOptions { ... }` instantiations must be deleted from the individual toolkits. Network payload translation, local file operations, and the new diagnostic dump handlers must reference `FdpJsonOptionsRegistry.DefaultRelaxed` or `FdpJsonOptionsRegistry.Indented` exclusively.

