# Cluster-Wide Diagnostic Dump — Design

## Overview

This design adds a unified, cluster-wide diagnostic snapshot capability to the Hrot simulation
platform. Operators can trigger a dump from ExCon (or the Orchestrator) that causes every selected
node to gather its local entity state, event history, architecture profile, and NLog files, stage
them in `LocalTempRoot`, and have them pulled to the central NAS via the existing SMB Pull Gateway.
Alongside cluster-wide dumping, the design also adds multi-select copy-to-JSON in the Event Browser
and Entity Inspector, and centralises all JSON serialisation settings to eliminate the
`FixedString64` bug and the scattering of `JsonSerializerOptions` instances.

---

## Architectural Principles

- All dump data travels over **SMB** (via `StorageGatewayModule.PullToNasAsync`), never DDS.
- The Orchestrator's **2PC pipeline** (`ClusterSlave` / `ClusterMaster`) is reused without
  modification.
- ExCon triggers the dump through the standard **CQRS intent pathway** (intent -> egress translator
  -> DDS -> master translator -> master -> fan-out), so the Diagnostics Panel is fully functional
  from non-orchestrator nodes.
- All heavy work (serialisation, log streaming, formatting) runs on a **background `Task`** spawned
  inside `PrepareAsync`, keeping the 60 Hz simulation tick unaffected.
- The two-stage JSON pipeline (`System.Text.Json` stage 1 for semantic correctness, then
  `Newtonsoft.Json` stage 2 for aesthetic formatting) is applied everywhere uniformly.

---

## Phase 1: JSON Serialisation Foundation

**Goal:** Eliminate duplicated `JsonSerializerOptions`, fix the `FixedString64` bug, and extract
the numeric-array flattening logic into a shared utility.

### 1.1 Move Custom Converters to Fdp.Core

`FixedString32Converter`, `FixedString64Converter`, and the `Vector`/`Quaternion` array converters
currently live in `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioJsonConverters.cs`. Because
`FixedString32` and `FixedString64` are defined in `Fdp.Core`, their converters can and should move
to `Fdp.Core/Serialization/Converters/` so that any layer (UI, diagnostic dump handlers, tests)
can use them without pulling in the Scenario toolkit.

`StrictStringEnumConverter` (currently in
`Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationJsonOptions.cs`) must also move
to `Fdp.Core/Serialization/Converters/`. Keeping it only in the orchestration layer and using
the standard `JsonStringEnumConverter` in the registry would mean diagnostic dumps silently
accept integer-valued enum fields, reintroducing the class of bug the registry is designed to
prevent. `OrchestrationJsonOptions` retains a thin forwarding wrapper after the move.

`ScenarioJsonConverters.cs` retains public type aliases (or `[Obsolete]` forwarders) to avoid
breaking existing callers until they are migrated.

### 1.2 FdpJsonOptionsRegistry (Fdp.Core)

A new static class `FdpJsonOptionsRegistry` in `Fdp.Core.Serialization` exposes two pre-configured
`JsonSerializerOptions` singletons:

- **`DefaultRelaxed`** — `IncludeFields = true`, `PropertyNameCaseInsensitive = true`,
  `AllowTrailingCommas = true`, `ReadCommentHandling = Skip`,
  `DefaultIgnoreCondition = WhenWritingNull`, plus all custom converters from Phase 1.1
  (FixedString32/64, Vector2/3/4, Quaternion) and `StrictStringEnumConverter` (moved to Core
  in Phase 1.1). Using `StrictStringEnumConverter` in the central registry enforces strict enum
  parsing universally — both UI clipboard operations and cluster dump handlers parse enum fields
  as strings, never as silent integers.
  Replaces `FdpAutoSerializer._fieldAwareOptions`, `OrchestrationJsonOptions.Default`,
  `MetadataSerializer._options`, and `HrotSerializerOptions.HrotJsonOptions`.

- **`Indented`** — same as `DefaultRelaxed` plus `WriteIndented = true`. Used for clipboard and
  diagnostic dump output.

### 1.3 JsonAestheticFormatter (Fdp.Toolkits)

The private `WriteFormattedToken` / `IsPureNumericArray` methods in
`Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs` are extracted into
a public static class `JsonAestheticFormatter` in `FDP/Toolkits/Fdp.Toolkits/Serialization/`.
`Fdp.Toolkits` already has a `Newtonsoft.Json` package reference. The formatter accepts a raw JSON
string and returns one with purely-numeric arrays collapsed to single lines.

`ScenarioFileService.SaveScenario` delegates to `JsonAestheticFormatter` after serialising.

**Note:** `JsonAestheticFormatter` lives in `Fdp.Toolkits`, not `Fdp.Core`, because `Fdp.Core`
has no `Newtonsoft.Json` dependency.

### 1.4 Refactor Existing Callers

- `FdpAutoSerializer._fieldAwareOptions` -> `FdpJsonOptionsRegistry.DefaultRelaxed`
- `OrchestrationJsonOptions.Default` -> `FdpJsonOptionsRegistry.DefaultRelaxed`
- `MetadataSerializer._options` -> `FdpJsonOptionsRegistry.DefaultRelaxed`
- `HrotSerializerOptions.HrotJsonOptions` -> `FdpJsonOptionsRegistry.Indented`
- `EventBrowserPanel` "Copy to JSON" -> `FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter`
- `EntityJsonDumper.Dump` -> `FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter`

This immediately resolves the `FixedString64` bug: `DestructionOrder.Reason` will serialise as
`"HealthDepleted"` instead of `{ "Length": 11, "IsEmpty": false }`.

---

## Phase 2: Diagnostic Data Service Interfaces & Implementations

**Goal:** Extract data-gathering logic out of the UI panels into headless services consumed by both
the presentation layer and the cluster dump handler.

### 2.1 IDiagnosticEventHistoryService (Fdp.Core)

The `EventBrowserPanel` currently captures events inside its own render loop by calling
`FdpEventBus.GetDebugInspectors()` / `InspectReadBuffer()` and maintaining a `List<CapturedEvent>`
with `_capacity = 500`. This logic must move to a headless service so it is available to the
cluster dump handler without touching the UI.

Interface defined in `Fdp.Core.Diagnostics`:
```
public interface IDiagnosticEventHistoryService
{
    IReadOnlyList<CapturedEventDto> GetHistory(IReadOnlyList<string>? providerFilter = null);
    void ClearHistory();
}
```

`CapturedEventDto` replaces the private `CapturedEvent` class:
```
public record CapturedEventDto(uint Frame, string TypeName, bool IsManaged, string Summary, object? RawEvent);
```

`DiagnosticEventHistoryService` maintains a thread-safe circular buffer of ~500 events. To
guarantee deterministic, tear-free capture it must be updated by a dedicated `IEcsModuleSystem`
registered in the **`PostSimulation`** or **`Export`** phase. Running after all domain systems
have published their intents for the current tick ensures the buffer reflects a fully-committed
state before the diagnostic handler or UI panel reads it. A generic kernel tick hook is
insufficient; the explicit system phase registration is mandatory.

`GetHistory()` must **copy-under-lock**: acquire the circular buffer's lock, copy all current
buffer references to a transient `CapturedEventDto[]`, release the lock immediately, then return
the array snapshot to the caller. The background dump handler (or UI panel) may then serialise
that array without holding any service lock, guaranteeing that the 60 Hz `PostSimulation` writer
is blocked only for the duration of an O(N) memory copy — never for the duration of string
serialisation. Holding the lock during serialisation would stall the main simulation loop under
the non-blocking engine mandate.

`EventBrowserPanel` is refactored to read from this service instead of doing capture itself.

### 2.2 IArchitectureDiagnosticsService (Fdp.ModuleHost)

`ArchitectureDiagnosticsPanel` currently calls `kernel.GetModuleDiagnostics()`,
`kernel.SystemScheduler.GetProfileData<T>()`, and uses reflection to find translators. This is
extracted into `IArchitectureDiagnosticsService` in `Fdp.ModuleHost.Diagnostics`:

```
public interface IArchitectureDiagnosticsService
{
    ArchitectureSnapshotDto GetSnapshot();
}
```

`ArchitectureSnapshotDto` holds `IReadOnlyList<ModuleDiagnostics> Modules`,
`IReadOnlyList<SystemProfileData> Systems`, and `IReadOnlyList<TranslatorDiagnosticsDto> Translators`.

`ArchitectureDiagnosticsPanel` is refactored to read from this service.

### 2.3 IEntityStateExtractionService (Fdp.Toolkits)

Defined in `Fdp.Toolkits.Diagnostics`:
```
public interface IEntityStateExtractionService
{
    IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds);
}
```

`EntityStateDumpDto` wraps the existing `EntityJsonDumper.Dump` output:
```
public record EntityStateDumpDto(long NetworkId, int LocalIndex, int LocalGeneration,
    Dictionary<string, object> Components);
```

The implementation uses the `NetworkEntityMap` to resolve `NetworkIdentity` -> local `Entity`
handle and delegates to `EntityJsonDumper.Dump`. The `NetworkIdentity` component acts as the
natural cross-node correlation key — no custom keying needed.

### 2.4 ILogArchiveExtractionService (Hrot.Core)

Defined in `Hrot.Core.Diagnostics` (because it requires knowledge of NLog file naming conventions
and the `HrotNodeConfig.LogDirectory` path):

```
public interface ILogArchiveExtractionService
{
    Task ExtractLogsAsync(string targetFilePath, int severityThreshold,
        float maxAgeHours, CancellationToken ct);
}
```

The implementation streams active and rotation archives (`{SubsystemName}_{NodeId}*.log`)
line-by-line, parses the standardised log layout to filter by severity and age, and writes to
`targetFilePath`. It does NOT load all content into memory. CPU spikes are acceptable.

---

## Phase 3: Multi-Select Copy-to-JSON in UI Panels

**Goal:** Allow operators to select multiple events or entities and copy them as a JSON array.

### 3.1 EventBrowserPanel Multi-Select

`EventBrowserPanel._selectedEvent` (type `CapturedEvent?`) is replaced with
`_selectedEvents` (type `HashSet<CapturedEventDto>`) and `_lastClickedIndex` (type `int`,
initialised to `-1`). Standard OS multi-select modifiers are handled via `ImGui.Selectable`:

- **Plain Click**: clears `_selectedEvents`, adds the clicked item, updates `_lastClickedIndex`.
- **Ctrl+Click**: toggles the clicked item in `_selectedEvents`; updates `_lastClickedIndex`.
- **Shift+Click**: computes the inclusive index range
  `[min(_lastClickedIndex, currentIndex) .. max(_lastClickedIndex, currentIndex)]` in the
  **currently filtered and sorted view list** (the same list iterated in the current frame's
  Selectable loop), adds all items in that range to `_selectedEvents`. Does NOT update
  `_lastClickedIndex`.

Storing `_lastClickedIndex` alongside the `HashSet` is mandatory because the set has no concept
of sequential order; without the stored index the range bounds for Shift+Click are undefined.

The existing "Copy to JSON" context menu item becomes available for both single and multi
selections. For multi-select, the payload is a JSON array of event objects sorted by `Frame`
ascending, processed through the two-stage pipeline (`FdpJsonOptionsRegistry.Indented` +
`JsonAestheticFormatter`).

The `Frame/Type` column display is **unchanged** from the current rendering (frame number +
short type name, colour-coded). No FQNs in the column.

### 3.2 EntityInspectorPanel Multi-Select

`EntityInspectorPanel` is extended to support `HashSet<Entity> _selectedEntities` and
`int _lastClickedIndex` (initialised to `-1`). Shift+Click uses the currently filtered and
sorted entity list (the view list rendered in the current frame) to compute the inclusive index
range between `_lastClickedIndex` and the current row's index, adding all items in that range
to `_selectedEntities`. Ctrl+Click toggles and updates `_lastClickedIndex`; plain Click clears
`_selectedEntities`, adds the clicked item, and updates `_lastClickedIndex`; Shift+Click extends
the range but does NOT update `_lastClickedIndex`.

The existing `IEntityContextMenuHandler.PopulateMenu` receives an overload accepting
`IReadOnlyCollection<Entity>`.

"Copy to JSON (N items)" appears in the context menu when N > 1. The payload is a JSON array of
`EntityStateDumpDto` objects.

---

## Phase 4: Cluster-Wide Dump Orchestration Protocol

**Goal:** Wire the new dump operation into the existing 2PC pipeline.

### 4.1 Enum Extensions

```
// FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/ClusterOpType.cs
DumpDiagnostics = 16

// FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/NodeOpType.cs
DumpDiagnostics = 28
```

### 4.2 DiagnosticDumpPayloadDto

Added to `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs`:

```
public record DiagnosticDumpPayloadDto(
    Guid TransactionId,           // 2PC transaction ID; used for staging directory name
    DateTime RequestedAt,         // Orchestrator local time; identical across all nodes;
                                  // used as DATETIME in file names ("YYYYMMDD_HHmmss")
    List<int>? TargetNodeIds,     // Resolved from subsystem-column selection; null = all
    bool DumpEntities,
    List<long>? SpecificNetworkIds, // null/empty = all entities with NetworkIdentity
    bool DumpEvents,
    List<string>? EventProviders,   // null/empty = all providers on the node
    bool DumpArchitecture,
    bool DumpLogs,
    int LogSeverityThreshold,
    float LogAgeHours,
    bool UseMarkdownWrapper
);
```

Serialised via `FdpJsonOptionsRegistry.DefaultRelaxed`.

### 4.3 ExecuteDiagnosticDumpIntent

Added to `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterOpIntents.cs`:

```
[EventId(9058)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ExecuteDiagnosticDumpIntent
{
    public Guid RequestId;
    public DiagnosticDumpPayloadDto Configuration;
}
```

EventId 9058 is the next available after the existing 9057 (`LoadZoneIntent`).

### 4.4 DiagnosticsConsensusAggregator

Mirrors `StorageConsensusAggregator` in `Hrot/Subsystems/Hrot.Orchestrator/`. Implements
`INodeResponseAggregator` for `NodeOpType.DumpDiagnostics`. Flattens the
`List<FileManifestEntry>` arrays from all participating nodes into a single aggregated manifest.

The `SourceUnc` field (node-local absolute path used only by `StorageGatewayModule.PullToNasAsync`
within the Orchestrator process) is **never transmitted in the DDS `ClusterOpStatus`** sent back
to ExCon. In a large cluster a combined JSON payload of absolute paths for hundreds of dump files
could exceed the DDS message size limit. `DiagnosticsDumpProcessManager` therefore builds two
separate representations:
- Full manifest (SourceUnc + RelativeDest): used internally by `PullToNasAsync`.
- Stripped manifest (RelativeDest only): serialised into `ClusterOpStatus` and transmitted to ExCon.

ExCon UI reconstructs the NAS-absolute path as `NasBasePath + \ + RelativeDest`.

### 4.5 DiagnosticsDumpProcessManager

Mirrors `StorageProcessManager`. Observes the aggregated manifest and calls
`StorageGatewayModule.PullToNasAsync` to pull all staged dump files to `[NAS]/dumps/`.

The process manager must observe **both** `ClusterOpCompletedEvent` and transaction abort events
for `ClusterOpType.DumpDiagnostics`. On abort (any node's `PrepareAsync` fails or the master
cancels the 2PC), the manager skips the NAS pull and immediately publishes a terminal
`ClusterOpStatus(Failure)`. This prevents `PullToNasAsync` from being called with a partial
or empty manifest, which would cause spurious SMB errors.

### 4.6 ClusterOpEgressTranslator / ClusterOpMasterTranslator

Both translators receive `DumpDiagnostics` case handling. The Master translator deserialises the
`DiagnosticDumpPayloadDto` from the incoming `ClusterOpRequest`, publishes an
`ExecuteDiagnosticDumpIntent` to the local bus, and fans out `NodeOpType.DumpDiagnostics` commands
to all targeted nodes.

---

## Phase 5: Node-Side Handler and NLog Configuration

**Goal:** Each node handles the `DumpDiagnostics` node op, gathers data, and writes files to
`LocalTempRoot`.

### 5.1 NLog Programmatic FileTarget and Layout

The standardised layout is enforced in `Hrot/Runner/Hrot.ClusterRunner/Program.cs` by adding
an NLog `FileTarget` to the `LoggingConfiguration` after CLI parsing:

```
[YYYY-MM-DD HH:MM:SS.mmm] [LEVEL] [ShortLoggerName] [Node-{nodeId}] message exception
```

Settings:
- `ArchiveNumbering = Rolling`, `MaxArchiveFiles = 10`, `ArchiveAboveSize = 50 MB`
- Base filename: `{SubsystemName}_{NodeId}.log`; archives: `{SubsystemName}_{NodeId}.{#}.log`
- Both are placed in `config.LogDirectory` (new CLI option).

Including `SubsystemName` in the filename prevents file-lock collisions when multiple subsystems
(e.g., `SimHost` and `IG`) run on the same physical machine as separate processes and happen to
share a local node ID (common in test harnesses). Without this prefix, both processes would
attempt to open the same `node_1.log` file with `KeepFileOpen = true`, causing NLog to crash.

### 5.2 HrotRunnerConfiguration — `--log-dir` Option

```
[Option("log-dir", Required = false, HelpText = "Target directory for node log files")]
public string LogDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs");
```

After parsing, `resolvedLogDir` is set into `HrotNodeConfig.LogDirectory` (new property).

### 5.3 HrotNodeConfig.LogDirectory

```
// Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeConfig.cs
public string LogDirectory { get; set; } = string.Empty;
```

### 5.4 DiagnosticsDumpClusterOpHandler (Hrot.Common)

Placed in `Hrot/Engine/Hrot.Common/Diagnostics/DiagnosticsDumpClusterOpHandler.cs`. All four
diagnostic services are injected. The `CanHandle` method returns `true` for
`NodeOpType.DumpDiagnostics`.

**PrepareAsync:** Immediately spawns `Task.Run(..., TaskCreationOptions.LongRunning)` and returns
the pending task. The background task:
1. Decodes `DiagnosticDumpPayloadDto` from `intent.PayloadJson`
2. Skips if this node's ID is not in `TargetNodeIds`
3. Creates `LocalTempRoot/dumps/{TransactionId}/` directory
4. Conditionally calls the relevant services:
   - Entities: `_entityService.ExtractEntities(...)`
   - Architecture: `_archService.GetSnapshot()`
   - Logs: `_logService.ExtractLogsAsync(...)`
   - Events: calls `_eventService.GetHistory(provider)` once per entry in `EventProviders`
     (or `GetHistory(null)` if empty), then groups results into a
     `Dictionary<string, List<CapturedEventDto>>` keyed by provider name.
5. Serialises each dump kind through the two-stage JSON pipeline:
   - Entities / Architecture: serialised as a flat JSON array or object.
   - Events: serialised as a **provider-keyed JSON object** `{ "ProviderName": [...], ... }`
     (not a flat array). This format is required by the `ClusterDiagnosticsPanel` aggregation
     logic which merges per-provider across nodes to produce the required
     `{ "Subsystem": { "Provider": [...] } }` composite schema.
6. Wraps in Markdown triple-backtick block if `UseMarkdownWrapper`
7. Writes files with naming: `dump_{DATETIME}_{kind}_{SubsystemName}_{NodeId}.{ext}`
8. Returns `List<FileManifestEntry>`

**Commit:** No-op (files are already staged, orchestrator pulls them).

**Abort:** Deletes the staging directory `LocalTempRoot/dumps/{TransactionId}/`.

The registration of `DiagnosticsDumpClusterOpHandler` in each subsystem's bootstrapper
(SimHost, CGF, IG, ExCon) follows the same pattern as existing handlers in those subsystems.

---

### 5.5 Node LocalTempRoot Isolation and NAS Path Separation

**Problem:** When all subsystems run on the same machine (or in a single process), the default
`LocalTempRoot` value (`C:\FDP_Temp`) is identical for every node AND is the same root used by
the Orchestrator as the NAS pull destination. `StorageGatewayModule.PullToNasAsync` calls
`File.Copy(entry.SourceUnc, destPath, overwrite: true)`. If `SourceUnc` and `destPath` resolve
to the same file the OS throws an `IOException`.

**Fix — Node isolation:** Each subsystem bootstrapper namespaces `LocalTempRoot` by node ID:
```
var baseTempRoot = nodeConfig.LocalTempRoot ?? OrchestrationConstants.DefaultStagingDirectory;
var isolatedTempRoot = Path.Combine(baseTempRoot, "nodes", $"node-{localNodeId}");
hrotConfig.LocalTempRoot = isolatedTempRoot;
```
Node 400 then writes to `C:\FDP_Temp\nodes\node-400\...`; Node 1 to `C:\FDP_Temp\nodes\node-1\...`.

**Fix — Orchestrator NAS path:** `ClusterConfiguration` gains a `NasBasePath` property:
```
// Hrot/Subsystems/Hrot.Orchestrator/ClusterConfiguration.cs (or equivalent)
public string NasBasePath { get; init; } = @"C:\FDP_Temp\shared";
```
`OrchestratorSubsystem.Initialize()` passes `_config.NasBasePath` into all process managers
(`StorageProcessManager`, `AssetInventoryProcessManager`, `AssetPrefetchProcessManager`,
`DiagnosticsDumpProcessManager`) instead of the current `OrchestrationConstants.DefaultStagingDirectory`.

**Resulting FileManifestEntry layout for a dump on Node 400:**
- `SourceUnc` = `C:\FDP_Temp\nodes\node-400\dumps\{txId}\dump_20260503_entities_CGF_400.json`
- `RelativeDest` = `dumps\{txId}\dump_20260503_entities_CGF_400.json`

`StorageGatewayModule` constructs destination as `NasBasePath + \ + RelativeDest`
= `C:\FDP_Temp\shared\dumps\{txId}\dump_20260503_entities_CGF_400.json`, cleanly separate
from the source.

**File naming:**
```
dump_{YYYYMMDD_HHmmss}_{kind}_{SubsystemName}_{NodeId}.json
dump_{YYYYMMDD_HHmmss}_{kind}_{SubsystemName}_{NodeId}.json.md  (when UseMarkdownWrapper)
dump_{YYYYMMDD_HHmmss}_logs_{SubsystemName}_{NodeId}.log
```

The `YYYYMMDD_HHmmss` part is formatted from `DiagnosticDumpPayloadDto.RequestedAt`
(the orchestrator local time at the moment the operator triggers the dump). This value is
identical in every node's copy of the DTO, ensuring all files from the same request share
the same timestamp prefix.

---

## Phase 6: Cluster Diagnostics UI Panel

**Goal:** Provide the operator with a matrix-based dump trigger and a results tree accessible from
ExCon and the Orchestrator.

### 6.1 ClusterDiagnosticsPanel

A new panel in `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterDiagnosticsPanel.cs`
(shared via `Hrot.Orchestrator`, which `Hrot.ExCon` already references).

**Configuration section:**
- `[x] Wrap JSON output in Markdown block (.md)` checkbox
- ImGui `BeginTable` matrix with subsystem-type columns (derived from
  `ClusterUiCache.ReachableTargets`) and dump-kind rows:
  - Entities (All)
  - Entities (Selected) + network ID input field (comma-separated `long` values; sanitised
    at submit time: split by comma, trim whitespace, discard tokens that fail `long.TryParse`)
  - Events + providers filter input
  - Architecture
  - NLog Files
- Log Filters: severity dropdown + max age float field
- `[ EXECUTE CLUSTER DUMP ]` button: sanitises the network ID input (split by `','`, trim
  whitespace per token, discard tokens where `long.TryParse` fails), resolves the selected
  subsystem columns to concrete node IDs from `ClusterUiCache`, builds
  `DiagnosticDumpPayloadDto` with the sanitised `SpecificNetworkIds`, and publishes
  `ExecuteClusterOpIntent(DumpDiagnostics)` to the local bus.

**Results section:**
- Tree grouped by subsystem type, then by node ID
- **Subsystem group nodes** (e.g., "CGF", "SimHost") expose a `BeginPopupContextItem` with:
  - **Copy Aggregated JSON** — immediately renders a "Copying..." transient label in the
    panel and spawns a background `Task` to avoid blocking the 60 Hz render loop. The task:
    1. Collects all dump file entries for that subsystem's nodes from the manifest (entity
       files, event files, architecture files as applicable).
    2. Reads each NAS file (bounded to 10 MB per file; oversized files are skipped and an
       inline warning is recorded for display in the panel).
    3. For **entity** files: parses each flat JSON array via
       `FdpJsonOptionsRegistry.DefaultRelaxed`, accumulates into one merged list, wraps as
       `{ "SubsystemName": [ ...merged entities... ] }`.
    4. For **event** files: parses each provider-keyed JSON object
       (`{ "ProviderName": [...] }`) via `FdpJsonOptionsRegistry.DefaultRelaxed`, merges
       event arrays per provider across nodes, wraps as
       `{ "SubsystemName": { "ProviderName": [ ...merged... ], ... } }`.
    5. Serialises via `FdpJsonOptionsRegistry.Indented` +
       `JsonAestheticFormatter.FlattenNumericArrays`.
    6. On completion sets `_pendingClipboardText` (a `volatile string?` field). The
       per-frame render method checks for a non-null value and calls
       `ImGui.SetClipboardText(_pendingClipboardText)` then clears the field. This ensures
       `ImGui.SetClipboardText` is always called from the render thread.
- **Root tree node** exposes the same async "Copy Aggregated JSON" action combining all
  subsystem groups into the full cross-cluster composite:
  - Entities: `{ "CGF": [...], "SimHost": [...] }`
  - Events: `{ "CGF": { "World": [...], "Perception": [...] }, "SimHost": { ... } }`
  This is the format required by IDEAS.md.
- File-level entries are `ImGui.Selectable` items with `BeginPopupContextItem` menus:
  - Copy Content — reads file from NAS and writes to clipboard
  - Copy NAS Path — writes the `RelativeDest` NAS path to clipboard
  - Open from NAS — `Process.Start(new ProcessStartInfo { FileName = uncPath, UseShellExecute = true })`
  - Save Local Copy As — invokes `IFileDialogService.ShowSaveAsDialogAsync`
- `[ Generate Merged Cluster Log ]` button appears once log files exist (Phase 8)
- Cluster Aggregates sub-tree shows the merged log once generated (Phase 8)

### 6.2 Registration

The panel is registered as a "Diagnostics" tab in both:
- `OrchestratorSubsystem` (existing window registrar)
- `ExConSubsystem` (already references `Hrot.Orchestrator`)

---

## Phase 7: IFileDialogService — Reusable Save As Dialog

**Goal:** Provide a domain-agnostic ImGui file save dialog usable from the Diagnostics panel and
any future consumer.

### 7.1 IFileDialogService Interface (Fdp.Presentation)

Defined in `FDP/Engine/Fdp.Presentation/Abstractions/IFileDialogService.cs`:

```
public interface IFileDialogService
{
    Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter);
}
```

Follows the same async bridge pattern as `IMapPickService` /
`MapPickServiceBridge` in `Hrot/Engine/Hrot.Presentation/`.

### 7.2 ImGuiFileDialogService (Fdp.Presentation)

`ImGuiFileDialogService` maintains state for the current directory, file-name buffer, and
a `TaskCompletionSource<string?>`. Its `Draw()` method is called every frame from the
`WindowManager` main rendering path. `OpenPopup` / `BeginPopupModal` drive the dialog lifecycle.
Directory navigation (Up button, double-click folder) and file-name input are provided.

The `Draw()` method must be invoked from the `WindowManager` rendering loop after all other
windows, so the modal overlays everything. The `WindowManager` holds a reference to the singleton
`IFileDialogService` instance and calls `Draw()` unconditionally each frame.

### 7.3 Registration

`ImGuiFileDialogService` is registered in the composition root (subsystem bootstrapper) as a
singleton implementing `IFileDialogService`. The `ClusterDiagnosticsPanel` receives it via
constructor injection.

---

## Phase 8: Cluster Log Merge (Optional Post-Process)

**Goal:** Allow operators to merge all per-node log files pulled to the NAS into a single
chronologically-ordered stream.

### 8.1 K-Way Merge Algorithm

`DiagnosticLogMergeWorker` in `Hrot/Subsystems/Hrot.Orchestrator/` uses a
`PriorityQueue<LogLineRef, DateTime>` to merge N pre-sorted log files in streaming fashion
(O(N) memory — only one line per file is held in the queue at any time).

Timestamp parsing uses `ReadOnlySpan<char>` slicing against the standardised log layout to avoid
allocations. The output file is saved to `[NAS]/dumps/dump_{DATETIME}_logs_MERGED.log`.

Multi-line log entries (principally exception stack traces) lack the `[YYYY-MM-DD HH:mm:ss.fff]`
prefix. The worker must handle these **continuation lines** by buffering them under the timestamp
of the last successfully parsed line: when `TryParseTimestamp` fails on a line, that line is
appended to the `Line` string of the most-recently-dequeued `LogLineRef` rather than inserted as
a new entry. This prevents the background task from crashing and keeps stack traces intact and
co-located with their originating log record in the merged output.

### 8.2 Integration

When triggered via the `[ Generate Merged Cluster Log ]` button, the UI publishes a
`MergeLogsIntent` to the local bus. `DiagnosticLogMergeWorker` observes this intent and spawns a
`Task.Run(..., LongRunning)`. On completion it publishes `LogMergeCompletedEvent` containing the
NAS path. The `ClusterUiCache` is updated; the results tree gains a "Cluster Aggregates" node with
the merged file entry.

The merged file supports the same context menu (Copy Content, Copy NAS Path, Open from NAS, Save
Local Copy As) as individual files.

---

## JSON Dump Schemas

### Events Dump

Event dump files use a **provider-keyed format** to support merge-by-provider aggregation in
`ClusterDiagnosticsPanel`. Top-level keys are provider names derived from `EventProviders`
(or inferred from the TypeName namespace segment when all providers are dumped):

```json
{
  "Replication": [
    {
      "EventType": "Fdp.Toolkit.Replication.Messages.OwnershipUpdate",
      "Frame": 773,
      "Payload": { "EntityId": 4001, "NewOwnerId": 1 }
    }
  ],
  "Lifecycle": [
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
}
```

`FixedString64Converter` ensures `Reason` is a string, not `{ "Length": N, "IsEmpty": false }`.

### Entities Dump

```json
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

Vector/Quaternion arrays are collapsed to single lines by `JsonAestheticFormatter`.
`NetworkIdentity` is the natural cross-node correlation key and is always present.

### Architecture Dump

```json
{
  "Modules": [
    {
      "ModuleName": "LiveKinematicsModule",
      "RunMode": "Synchronous",
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
      "SentSampleCount": 15000,
      "ReceivedSampleCount": 0
    }
  ]
}
```

### NLog File Format

The standardised layout applied to every node's `FileTarget`:
```
[2026-05-03 11:09:00.123] [INFO] [ReferenceCheckpointHandler] [Node-400] Commit: snapshot enqueued.
[2026-05-03 11:09:01.456] [WARN] [EntityInfoIngressTranslator] [Node-1] Commander entity not found.
```

Fields are bracket-delimited to allow robust line-by-line parsing for filtering and K-way merge.

---

## Architectural Decision Record

| Decision | Rationale |
|---|---|
| `JsonAestheticFormatter` in `Fdp.Toolkits`, not `Fdp.Core` | `Fdp.Core` has no Newtonsoft.Json dependency; adding it would bloat the base layer |
| `ILogArchiveExtractionService` in `Hrot.Core` | Requires knowledge of NLog file naming convention and `HrotNodeConfig.LogDirectory`; not a generic engine concern |
| `DiagnosticsDumpClusterOpHandler` in `Hrot.Common` | `Hrot.Common` references `Hrot.Core`, `Hrot.Network.Orchestration`, and transitively `Fdp.Toolkits`; it is used by all subsystems |
| Log filtering on the node | Logs can be gigabytes; network transfer of raw logs is impractical; CPU spikes on node are acceptable |
| `DiagnosticsDumpPayloadDto.EventProviders` empty = all providers | Avoids coupling ExCon config to specific provider names registered on remote nodes |
| `NetworkIdentity` as correlation key | Already present as a component; no custom mapping needed |
| Target matrix columns = subsystem types (not node IDs) | Node IDs are resolved at submit time from `ClusterUiCache`; UI stays stable as instances join/leave |
| Logs are not wrapped in Markdown | Logs are plain text; Markdown wrapping applies only to JSON dump kinds |
| `StrictStringEnumConverter` moved to `Fdp.Core` | Keeping it in `Hrot.Network.Orchestration` only would mean diagnostic dump handlers use the weaker `JsonStringEnumConverter`, silently accepting integers for enum fields |
| Node `LocalTempRoot` namespaced by node ID | Prevents file-lock collisions and `File.Copy` source-equals-destination errors in single-machine deployments where multiple nodes share the same base path |
| `ClusterConfiguration.NasBasePath` separate from `DefaultStagingDirectory` | Decouples the cluster-wide shared storage location from the per-node ephemeral staging root, making the path boundary explicit and configurable |
| `SourceUnc` stripped from `ClusterOpStatus` sent to ExCon | Node-local paths are inaccessible from ExCon; transmitting them wastes DDS payload budget; ExCon only needs the NAS-relative path |
| `DiagnosticEventHistoryService` as `IEcsModuleSystem` in PostSimulation/Export | Guarantees all domain systems have committed their intents before the history buffer is updated, preventing torn reads and non-deterministic event ordering in the captured history |
| `GetHistory()` copy-under-lock | Returns a snapshot array rather than holding the buffer lock during serialisation; the 60 Hz PostSimulation writer is blocked only for an O(N) memory copy, never for string serialisation by the background dump task |
| `_lastClickedIndex` stored per panel | A `HashSet<T>` has no sequential ordering; storing the last-clicked row index is mandatory to compute the inclusive Shift+Click range against the current filtered+sorted view list in immediate-mode GUI |
| "Copy Aggregated JSON" assembled in the UI layer on demand via background Task | Constructing on demand avoids storing a second copy of all dump data in the cluster cache; the action is asynchronous because SMB reads and JSON parsing are blocking I/O that would freeze the 60 Hz render loop if executed inline; `ImGui.SetClipboardText` is marshalled back to the render thread via `volatile string? _pendingClipboardText` |
| Events dump uses provider-keyed format (`{ "ProviderName": [...] }`) | Enables merge-by-provider in `ClusterDiagnosticsPanel` without a second grouping pass; directly produces the `{ "Subsystem": { "Provider": [...] } }` schema required by IDEAS.md |
| Network ID input sanitised via `long.TryParse` before DTO construction | ImGui text buffers are raw strings; filtering malformed tokens in the panel is the only safe boundary; the cluster master must receive only valid `long` IDs in `SpecificNetworkIds` |

