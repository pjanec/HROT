# Hrot.Network.Orchestration

| Field        | Value                                                                 |
|--------------|-----------------------------------------------------------------------|
| Project      | `Hrot.Network.Orchestration`                                          |
| Path         | `Hrot/Network/Hrot.Network.Orchestration/`                            |
| Target       | `net8.0`                                                              |
| Documented   | 2026-05-23                                                            |

---

## README Validation

**Missing** -- no `README.md` exists in the project folder.

---

## Executive Overview

`Hrot.Network.Orchestration` is the Anti-Corruption Layer (ACL) that sits between
the CycloneDDS wire protocol and the FDP toolkit's CQRS event bus for all
orchestration traffic in the HROT simulation cluster.

The assembly provides five major services:

1. **Cluster-op ingress (Orchestrator side)** -- `ClusterOpMasterTranslator` reads
   raw `ClusterOpRequest` DDS samples, deserialises the `PayloadJson` field, and
   publishes strongly-typed intent events (`TransitionStateIntent`,
   `ManageEpisodeIntent`, etc.) onto the `FdpEventBus`.

2. **Cluster-op egress (ExCon / client side)** -- `ClusterOpEgressTranslator`
   drains typed intent events from the bus and writes `ClusterOpRequest` DDS
   messages to the Orchestrator.

3. **Node-op master path** -- `NodeOpMasterTranslator` handles the two-phase
   commit (2PC) command fan-out from the Orchestrator to individual simulation
   nodes: it serialises `ExecuteNodeOpIntent` events to per-node
   `NodeOpCommand` DDS writers and ingests `NodeOpStatus` replies back onto the
   bus as `NodeOpCompletedEvent`.

4. **Node-op slave path** -- `NodeOpSlaveTranslator` is the mirror image running
   inside each SimHost (slave) node: it reads addressed `NodeOpCommand` samples,
   deserialises the payload, publishes `ExecuteNodeOpIntent` for the local handler
   pipeline, and writes `NodeHeartbeat` / `NodeOpStatus` back to DDS.

5. **Observer path** -- `OrchestrationObserverTranslator` is a read-only
   subscriber that monitors all seven orchestration DDS topics and republishes
   them as bus events so that UI caches (`ClusterUiCache`) and monitoring
   subsystems can observe the full cluster state without directly touching DDS.

Supporting types in the assembly define the DDS wire schema (`OrchestrationMessages.cs`
inside the `Hrot.NED.Descriptors.Orchestration` namespace), the payload DTO records
used for JSON serialisation, the handler interfaces (`IClusterOpHandler`,
`ITickableClusterOpHandler`), the `HrotHandlerAdapter` migration shim, the
`PreviewClusterOpHandler` for dry-run snapshots, the `ListenerRecordReplayController`
no-op for IG/ExCon nodes, and the `DdsIdAllocatorHelper` startup guard.

---

## Architecture

### Layering Model

```
+-----------------------------------------------------------------------+
|                         External Clients                              |
|           (ExCon UI, ClusterRunner, test harnesses)                   |
+-----------------------------------------------------------------------+
         |  typed intent events (FdpEventBus)  ^
         v                                     |
+---------------------------+    +---------------------------+
|  ClusterOpEgressTranslator|    | OrchestrationObserver-    |
|  (client egress)          |    | Translator (read-only)    |
+---------------------------+    +---------------------------+
         |                                     ^
         | DDS ClusterOpRequest                | DDS (7 topics)
         v                                     |
+---------------------------+    +---------------------------+
|  ClusterOpMasterTranslator|    |   CycloneDDS middleware   |
|  (orchestrator ingress)   |    |   (hrot-orchestration IDL)|
+---------------------------+    +---------------------------+
         |  typed intents                      ^
         v                                     | DDS NodeOpStatus
+---------------------------+    +---------------------------+
|    FDP Toolkit            |    |  NodeOpMasterTranslator   |
|    ClusterMaster /        |--->|  (orchestrator egress,    |
|    ClusterSlave           |    |   2PC fan-out)            |
+---------------------------+    +---------------------------+
                                              |
                              DDS NodeOpCommand (per-node keyed)
                                              |
                                              v
+---------------------------+    +---------------------------+
|  NodeOpSlaveTranslator    |    |  IClusterOpHandler        |
|  (SimHost ingress)        |--->|  (subsystem handlers,     |
+---------------------------+    |   Prepare / Commit / Abort|
         |                       +---------------------------+
         | DDS NodeHeartbeat
         | DDS NodeOpStatus
         v
    CycloneDDS middleware
```

### Two-Phase Commit Protocol (2PC) Flow

```
  Orchestrator (ClusterMaster)          Simulation Node (ClusterSlave)
  ============================          ==============================
         |                                          |
         |  ExecuteNodeOpIntent (Bus)               |
         |-->  NodeOpMasterTranslator               |
         |       serialise payload                  |
         |       write NodeOpCommand[TargetNodeId]  |
         |                       DDS ------------->|
         |                                    NodeOpSlaveTranslator
         |                                    deserialise payload
         |                                    publish ExecuteNodeOpIntent
         |                                          |
         |                                    IClusterOpHandler.PrepareAsync()
         |                                          |
         |                                    IClusterOpHandler.Commit()
         |                                          |
         |                       DDS <-------------|
         |     NodeOpStatus (Success/Failure)       |
         |  NodeOpMasterTranslator reads            |
         |  publish NodeOpCompletedEvent (Bus)      |
         |                                          |
```

### Observer (Read-Only) Topology

```
  CycloneDDS                          FdpEventBus consumers
  ==========                          ====================
                                       (ClusterUiCache, etc.)
  ClusterStateTopic ------+
  AssetInventoryTopic ----+
  NodeHeartbeat ----------+--> OrchestrationObserverTranslator --> ClusterStateUpdateEvent
  SwitchTimeModeWireDto --+                                     --> AssetInventoryUpdateEvent
  ClusterOpStatus --------+                                     --> NodeHeartbeatEvent
  NodeOpCommand ----------+                                     --> SwitchTimeModeEvent
  NodeOpStatus -----------+                                     --> ClusterOpCompletedEvent
                                                                --> ExecuteNodeOpIntent
                                                                --> NodeOpCompletedEvent
```

---

## Source Structure

### Folder Layout

```
Hrot.Network.Orchestration/
+-- Hrot.Network.Orchestration.csproj
+-- ClusterOpEgressTranslator.cs
+-- ClusterOpMasterTranslator.cs
+-- ClusterStateChangedEvent.cs
+-- DdsIdAllocatorHelper.cs
+-- HrotHandlerAdapter.cs
+-- IClusterOpHandler.cs
+-- ITickableClusterOpHandler.cs
+-- ListenerRecordReplayController.cs
+-- NodeOpMasterTranslator.cs
+-- NodeOpSlaveTranslator.cs
+-- OrchestrationObserverTranslator.cs
+-- Handlers/
|   +-- PreviewClusterOpHandler.cs
+-- Orchestration/
|   +-- OrchestrationMessages.cs
+-- Payloads/
    +-- OrchestrationPayloadDtos.cs
```

### Namespaces and Types

| File | Namespace | Types |
|------|-----------|-------|
| `OrchestrationMessages.cs` | `Hrot.NED.Descriptors.Orchestration` | `ClusterState` (enum), `ClusterOpType` (enum), `NodeOpType` (enum), `ClusterStateTopic` (struct), `AssetInventoryTopic` (struct), `ClusterOpRequest` (struct), `ClusterOpStatus` (struct), `NodeOpCommand` (struct), `NodeOpStatus` (struct), `NodeHeartbeat` (struct), `OrchestratorContextTopic` (struct) |
| `OrchestrationPayloadDtos.cs` | `Hrot.Network.Orchestration` | `StrictStringEnumConverter` (class), `OrchestrationJsonOptions` (static class), `TransitionPayloadDto` (record), `ManageEpisodePayloadDto` (record), `ArchivePayloadDto` (record), `SeekReplayPayloadDto` (record), `StepTimePayloadDto` (record), `SetTimeScalePayloadDto` (record), `NodeTransitionPayloadDto` (record), `NodeEpisodePayloadDto` (record), `NodePrefetchPayloadDto` (record), `FileManifestEntry` (record), `DiagnosticDumpPayloadDto` (record) |
| `ClusterOpMasterTranslator.cs` | `Hrot.Network.Orchestration` | `ClusterOpMasterTranslator` (sealed class) |
| `ClusterOpEgressTranslator.cs` | `Hrot.Common.Orchestration` | `ClusterOpEgressTranslator` (sealed class, `IDisposable`) |
| `NodeOpMasterTranslator.cs` | `Hrot.Network.Orchestration` | `NodeOpMasterTranslator` (sealed class) |
| `NodeOpSlaveTranslator.cs` | `Hrot.Common.Orchestration` | `NodeOpSlaveTranslator` (sealed class, `IOrchestrationTranslator`) |
| `OrchestrationObserverTranslator.cs` | `Hrot.Common.Orchestration` | `OrchestrationObserverTranslator` (sealed class, `IDisposable`) |
| `IClusterOpHandler.cs` | `Hrot.Common.Orchestration` | `IClusterOpHandler` (interface) |
| `ITickableClusterOpHandler.cs` | `Hrot.Common.Orchestration` | `ITickableClusterOpHandler` (interface) |
| `HrotHandlerAdapter.cs` | `Hrot.Common.Orchestration` | `HrotHandlerAdapter` (sealed class, `ITickableClusterStateHandler`) |
| `ClusterStateChangedEvent.cs` | `Hrot.Common.Orchestration` | `ClusterStateChangedEvent` (struct) |
| `ListenerRecordReplayController.cs` | `Hrot.Common.Orchestration` | `ListenerRecordReplayController` (sealed class, `IRecordReplayController`) |
| `DdsIdAllocatorHelper.cs` | `Hrot.Common.Infrastructure` | `DdsIdAllocatorHelper` (static class) |
| `Handlers/PreviewClusterOpHandler.cs` | `Hrot.Common.Orchestration.Handlers` | `PreviewClusterOpHandler` (sealed class, `IClusterOpHandler`) |

---

## Public API Reference

### Namespace `Hrot.NED.Descriptors.Orchestration`

#### `ClusterState` (enum)

Represents the discrete states of the simulation cluster state machine.

| Member | Value | Meaning |
|--------|-------|---------|
| `Idle` | 0 | Cluster is idle, no scenario loaded |
| `LoadingEdit` | 10 | Transitioning into edit mode |
| `OperatingEdit` | 11 | Edit mode active |
| `UnloadingEdit` | 12 | Leaving edit mode |
| `LoadingPreview` | 20 | Entering dry-run/preview mode |
| `OperatingPreview` | 21 | Preview/dry-run active |
| `UnloadingPreview` | 22 | Leaving preview mode, rewinding state |
| `LoadingLive` | 30 | Transitioning into live simulation |
| `OperatingLive` | 31 | Live simulation active |
| `UnloadingLive` | 32 | Leaving live simulation |
| `LoadingReplay` | 40 | Transitioning into replay mode |
| `OperatingReplay` | 41 | Replay active |
| `UnloadingReplay` | 42 | Leaving replay mode |
| `Degraded` | 99 | Cluster is in an error / degraded state |

#### `ClusterOpType` (enum)

Discriminator for cluster-level DDS operation requests.

| Member | Value |
|--------|-------|
| `TransitionState` | 1 |
| `SaveScenario` | 2 |
| `LoadZone` | 3 |
| `TakeCheckpoint` | 4 |
| `CollectCheckpoint` | 5 |
| `ExportArchive` | 6 |
| `ImportArchive` | 7 |
| `ManageEpisode` | 8 |
| `ReplaySeek` | 9 |
| `PauseTime` | 10 |
| `ResumeTime` | 11 |
| `PrefetchScenario` | 12 |
| `CancelOperation` | 13 |
| `StepTime` | 14 |
| `SetTimeScale` | 15 |
| `DumpDiagnostics` | 16 |

#### `NodeOpType` (enum)

Discriminator for per-node 2PC operation commands.

| Member | Value |
|--------|-------|
| `PrepareState` | 1 |
| `CommitState` | 2 |
| `AbortTransaction` | 3 |
| `TakeSnapshot` | 4 |
| `RestoreSnapshot` | 5 |
| `PrepareZone` | 7 |
| `CommitZone` | 8 |
| `PrepareLive` | 9 |
| `FinalizeLive` | 10 |
| `PrepareReplay` | 11 |
| `FinalizeReplay` | 12 |
| `NodeReplaySeek` | 13 |
| `UploadChunk` | 14 |
| `SerializeLocal` | 15 |
| `CleanupTempFiles` | 16 |
| `StartEpisode` | 20 |
| `StopEpisode` | 21 |
| `ReplayEpisode` | 22 |
| `ForgetEpisode` | 23 |
| `LoadEpisodeAssets` | 24 |
| `PrefetchFiles` | 25 |
| `PrepareEdit` | 26 |
| `FinalizeEdit` | 27 |
| `CollectDiagnostics` | 28 |

#### DDS Topic Structs

All structs are decorated with `[DdsTopic]`, `[DdsIdlFile]`, and `[DdsQos]` attributes
from `CycloneDDS.Schema`.

**`ClusterStateTopic`** -- DDS topic `"ClusterState"`, QoS: Reliable, TransientLocal, KeepLast(1)

| Field | Type | Description |
|-------|------|-------------|
| `CurrentState` | `ClusterState` | Current cluster state |
| `ExerciseId` | `Guid` | Active exercise identifier |
| `StateStartWallTicks` | `long` | Wall-clock ticks when state was entered |
| `TransactionEpoch` | `int` | Monotonic epoch counter for 2PC |

**`AssetInventoryTopic`** -- DDS topic `"AssetInventory"`, QoS: Reliable, TransientLocal, KeepLast(1)

| Field | Type | Description |
|-------|------|-------------|
| `NodeId` | `int` | Key; 0 = singleton orchestrator |
| `LocalScenariosJson` | `string` | JSON `string[]` of local scenario names |
| `LocalExercisesJson` | `string` | JSON `string[]` of local exercise names |
| `ArchivedExercisesJson` | `string` | JSON `string[]` of NAS-archived exercises |
| `UnarchivedLocalExercisesJson` | `string` | JSON `string[]` of local-only exercises |

**`ClusterOpRequest`** -- DDS topic `"ClusterOpRequest"`, QoS: Reliable, Volatile

| Field | Type | Description |
|-------|------|-------------|
| `RequestId` | `Guid` | Unique request correlation identifier |
| `OperationType` | `ClusterOpType` | Operation discriminator |
| `PayloadJson` | `string` | JSON-serialised operation payload |

**`ClusterOpStatus`** -- DDS topic `"SysOpStatus"`, QoS: Reliable, TransientLocal

| Field | Type | Description |
|-------|------|-------------|
| `RequestId` | `Guid` | Correlates with `ClusterOpRequest.RequestId` |
| `StatusCode` | `int` | `OrchestrationStatusCode` cast to int |
| `ResultJson` | `string` | Optional JSON result payload |

**`NodeOpCommand`** -- DDS topic `"NodeOpCommand"`, QoS: Reliable, Volatile, KeepAll

| Field | Type | Description |
|-------|------|-------------|
| `TargetNodeId` | `int` | Key; addressed node ID |
| `TransactionId` | `Guid` | 2PC transaction identifier |
| `Operation` | `NodeOpType` | Operation discriminator |
| `PayloadJson` | `string` | JSON-serialised node-level payload |

**`NodeOpStatus`** -- DDS topic `"NodeOpStatus"`, QoS: Reliable, Volatile, KeepAll

| Field | Type | Description |
|-------|------|-------------|
| `TransactionId` | `Guid` | Correlates with `NodeOpCommand.TransactionId` |
| `Operation` | `NodeOpType` | Echoed operation discriminator |
| `NodeId` | `int` | Reporting node's ID |
| `StatusCode` | `int` | `OrchestrationStatusCode` cast to int |
| `IsParticipating` | `bool` | Whether this node participates in the operation |
| `ResultJson` | `string` | Optional JSON result payload |

**`NodeHeartbeat`** -- DDS topic `"NodeHeartbeat"`, QoS: BestEffort, TransientLocal, KeepLast(1)

| Field | Type | Description |
|-------|------|-------------|
| `NodeId` | `int` | Key; node identifier |
| `SubsystemName` | `string` | Human-readable subsystem label |
| `LocalClusterState` | `ClusterState` | Node's local view of cluster state |
| `WallTicksUtc` | `long` | UTC wall-clock ticks at publish time |
| `CpuUsagePercent` | `float` | CPU load (informational) |
| `RamUsedBytes` | `long` | RAM usage (informational) |
| `SimTickAdvancing` | `bool` | Whether the sim loop is actively ticking |
| `SubsystemsJson` | `string` | JSON subsystem status details |

**`OrchestratorContextTopic`** -- DDS topic `"OrchestratorContext"`, QoS: Reliable, TransientLocal, KeepLast(1)

| Field | Type | Description |
|-------|------|-------------|
| `CurrentState` | `ClusterState` | Current cluster state |
| `ExerciseId` | `Guid` | Active exercise identifier |
| `TransactionEpoch` | `int` | Monotonic epoch counter |
| `ScenarioId` | `string` | Active scenario identifier |
| `ArchiveBasePath` | `string` | NAS base path for archives |
| `RequiredNodeIdsJson` | `string` | JSON `int[]` of required node IDs |
| `StateStartWallTicks` | `long` | Wall-clock ticks when state was entered |
| `ActiveEpisodesJson` | `string` | JSON `Guid[]` of active episode IDs |

---

### Namespace `Hrot.Network.Orchestration`

#### `StrictStringEnumConverter` (sealed class)

Forwarding wrapper over `Fdp.Core.Serialization.Converters.StrictStringEnumConverter`.
Rejects numeric enum values when deserialising JSON.  Retained so that
`[JsonConverter(typeof(StrictStringEnumConverter))]` attributes on existing DTOs
continue to compile.

```csharp
public sealed class StrictStringEnumConverter
    : Fdp.Core.Serialization.Converters.StrictStringEnumConverter
{
    public StrictStringEnumConverter() : base() { }
}
```

#### `OrchestrationJsonOptions` (static class)

Shared `JsonSerializerOptions` for all orchestration payload round-trips.

| Member | Type | Description |
|--------|------|-------------|
| `Default` | `JsonSerializerOptions` | Delegates to `FdpJsonOptionsRegistry.DefaultRelaxed`; enforces string enums, rejects integer enum values, suppresses nulls |

#### Payload DTO Records

All records use `System.Text.Json` attributes for serialisation.

**`TransitionPayloadDto`** -- for `ClusterOpType.TransitionState`

| Property | Type | JSON Name |
|----------|------|-----------|
| `TargetState` | `ClusterState?` | `"TargetState"` |
| `ScenarioId` | `string?` | `"ScenarioId"` |
| `ExerciseId` | `Guid` | `"ExerciseId"` |
| `TimeMode` | `string?` | `"TimeMode"` |

**`ManageEpisodePayloadDto`** -- for `ClusterOpType.ManageEpisode`

| Property | Type | JSON Name |
|----------|------|-----------|
| `IsStart` | `bool` | `"IsStart"` |
| `EpisodeId` | `Guid?` | `"EpisodeId"` |
| `ScenarioId` | `string?` | `"ScenarioId"` |

**`ArchivePayloadDto`** -- for `ExportArchive` / `ImportArchive`

| Property | Type | JSON Name |
|----------|------|-----------|
| `ExerciseId` | `Guid` | `"ExerciseId"` |

**`SeekReplayPayloadDto`** -- for `ClusterOpType.ReplaySeek`

| Property | Type | JSON Name |
|----------|------|-----------|
| `TargetWallTicks` | `long` | `"TargetWallTicks"` |

**`StepTimePayloadDto`** -- for `ClusterOpType.StepTime`

| Property | Type | JSON Name |
|----------|------|-----------|
| `FixedDelta` | `float` | `"FixedDelta"` |

**`SetTimeScalePayloadDto`** -- for `ClusterOpType.SetTimeScale`

| Property | Type | JSON Name |
|----------|------|-----------|
| `TimeScale` | `float` | `"TimeScale"` |

**`NodeTransitionPayloadDto`** -- node-level transition payload, carried in `NodeOpCommand.PayloadJson`

| Property | Type | JSON Name |
|----------|------|-----------|
| `TargetState` | `ClusterState?` | `"TargetState"` |
| `ScenarioId` | `string?` | `"ScenarioId"` |
| `ExerciseId` | `Guid` | `"ExerciseId"` |

**`NodeEpisodePayloadDto`** -- for `StartEpisode` / `StopEpisode`

| Property | Type | JSON Name |
|----------|------|-----------|
| `IsStart` | `bool` | `"IsStart"` |
| `EpisodeId` | `Guid?` | `"EpisodeId"` |
| `ScenarioId` | `string?` | `"ScenarioId"` |

**`NodePrefetchPayloadDto`** -- for `PrefetchFiles`

| Property | Type | JSON Name |
|----------|------|-----------|
| `ScenarioId` | `string?` | `"ScenarioId"` |

**`FileManifestEntry`** (sealed record) -- returned in `NodeOpStatus.ResultJson` for `SerializeLocal` / `CollectDiagnostics`

| Property | Type | Description |
|----------|------|-------------|
| `SourceUnc` | `string` | UNC path of the file on the originating node |
| `RelativeDest` | `string` | Relative destination path under NAS base directory |

**`DiagnosticDumpPayloadDto`** (record) -- for `ClusterOpType.DumpDiagnostics`

| Property | Type | JSON Name |
|----------|------|-----------|
| `TransactionId` | `Guid` | `"transaction_id"` |
| `RequestedAt` | `DateTime` | `"requested_at"` |
| `TargetNodeIds` | `int[]?` | `"target_node_ids"` |
| `DumpEvents` | `bool` | `"dump_events"` |
| `DumpEntities` | `bool` | `"dump_entities"` |
| `DumpArchitecture` | `bool` | `"dump_architecture"` |
| `DumpLogs` | `bool` | `"dump_logs"` |
| `EventProviders` | `string[]?` | `"event_providers"` |
| `UseMarkdownWrapper` | `bool` | `"use_markdown"` |
| `MaxAgeHours` | `float` | `"max_age_hours"` (default 24) |
| `SeverityThreshold` | `int` | `"severity_threshold"` |

#### `ClusterOpMasterTranslator` (sealed class)

ACL translator for the Orchestrator (master) side of cluster-level operations.

```
Namespace: Hrot.Network.Orchestration
```

**Constructor**

```csharp
public ClusterOpMasterTranslator(
    DdsReader<ClusterOpRequest>     requestReader,
    DdsWriter<ClusterOpStatus>      statusWriter,
    FdpEventBus                     bus,
    JsonSerializerOptions?          jsonOptions = null,
    DdsWriter<AssetInventoryTopic>? inventoryWriter = null,
    DdsWriter<ClusterStateTopic>?   clusterStateWriter = null)
```

| Parameter | Description |
|-----------|-------------|
| `requestReader` | DDS reader for inbound `ClusterOpRequest` samples |
| `statusWriter` | DDS writer for `ClusterOpStatus` replies |
| `bus` | The `FdpEventBus` used for both ingress and egress |
| `jsonOptions` | Optional custom JSON options; defaults to `OrchestrationJsonOptions.Default` |
| `inventoryWriter` | Optional writer for `AssetInventoryTopic` egress |
| `clusterStateWriter` | Optional writer for `ClusterStateTopic` egress |

**Public Methods**

| Method | Description |
|--------|-------------|
| `Tick()` | One frame: reads `ClusterOpRequest` DDS samples, dispatches typed bus intents; drains `ClusterOpCompletedEvent`, `StorageOpCompletedEvent`, `ClusterStateTransitionedEvent`, and `AssetInventoryUpdateEvent` from bus and writes DDS replies. |

**Handled `ClusterOpType` values and emitted bus events**

| `ClusterOpType` | Emitted Bus Event |
|----------------|-------------------|
| `TransitionState` | `TransitionStateIntent` |
| `ManageEpisode` | `ManageEpisodeIntent` |
| `ReplaySeek` | `SeekReplayIntent` |
| `CancelOperation` | `CancelOperationIntent` |
| `ExportArchive` | `ExecuteStorageOpIntent` (Export) |
| `ImportArchive` | `ExecuteStorageOpIntent` (Import) |
| `SaveScenario` | `ExecuteStorageOpIntent` (SaveScenario) |
| `TakeCheckpoint` | `TakeCheckpointIntent` |
| `LoadZone` | `LoadZoneIntent` |
| `PauseTime` | `PauseTimeIntent` |
| `ResumeTime` | `ResumeTimeIntent` |
| `StepTime` | `StepTimeIntent` |
| `SetTimeScale` | `SetTimeScaleIntent` |
| `DumpDiagnostics` | `ExecuteDiagnosticDumpIntent` |

#### `NodeOpMasterTranslator` (sealed class)

ACL translator for the Orchestrator's node-op fan-out.

```
Namespace: Hrot.Network.Orchestration
```

**Constructors**

```csharp
// Factory-based constructor
public NodeOpMasterTranslator(
    Func<int, DdsWriter<NodeOpCommand>> commandWriterFactory,
    DdsReader<NodeOpStatus>             statusReader,
    FdpEventBus                         bus,
    JsonSerializerOptions?              jsonOptions = null)

// Dictionary-based convenience constructor
public NodeOpMasterTranslator(
    Dictionary<int, DdsWriter<NodeOpCommand>> commandWriters,
    DdsReader<NodeOpStatus>                   statusReader,
    FdpEventBus                               bus,
    JsonSerializerOptions?                    jsonOptions = null)
```

**Public Methods**

| Method | Description |
|--------|-------------|
| `Tick()` | One frame: drains `ExecuteNodeOpIntent` from bus, serialises payload, writes `NodeOpCommand` to per-node DDS writer; reads `NodeOpStatus` from DDS and publishes `NodeOpCompletedEvent` on bus. |

**Payload serialisation mapping**

| Domain Payload Type | Wire DTO |
|--------------------|----------|
| `CommitStatePayload` | serialised as-is |
| `ReplaySeekPayload` | serialised as-is |
| `AbortTransactionPayload` | serialised as-is |
| `EditLoadHandlerPayload` | `NodeTransitionPayloadDto` |
| `EpisodeHandlerPayload` | `NodeEpisodePayloadDto` |
| `PrefetchHandlerPayload` | `NodePrefetchPayloadDto` |
| `ArchiveHandlerPayload` | `NodeTransitionPayloadDto` (ExerciseId only) |
| `DiagnosticDumpPayloadDto` / other | serialised by runtime type |

---

### Namespace `Hrot.Common.Orchestration`

#### `ClusterOpEgressTranslator` (sealed class, `IDisposable`)

Client-side (ExCon) egress translator.  The only class in the ExCon cluster-op
egress stack permitted to call `JsonSerializer`.

**Constructor**

```csharp
public ClusterOpEgressTranslator(FdpEventBus bus, DdsParticipant participant)
```

**Public Methods**

| Method | Description |
|--------|-------------|
| `Tick()` | Drains all queued typed intent events from bus and writes `ClusterOpRequest` DDS messages. Call once per frame after bus `SwapBuffers`. |
| `Dispose()` | Disposes the internal `DdsWriter<ClusterOpRequest>`. |

**Handled bus events and emitted `ClusterOpType`**

| Bus Event | `ClusterOpType` |
|-----------|----------------|
| `PauseTimeIntent` | `PauseTime` |
| `ResumeTimeIntent` | `ResumeTime` |
| `StepTimeIntent` | `StepTime` |
| `SetTimeScaleIntent` | `SetTimeScale` |
| `TransitionStateIntent` | `TransitionState` |
| `ManageEpisodeIntent` | `ManageEpisode` |
| `ExecuteStorageOpIntent` | `ExportArchive` / `ImportArchive` / `SaveScenario` |
| `TakeCheckpointIntent` | `TakeCheckpoint` |
| `SeekReplayIntent` | `ReplaySeek` |
| `CancelOperationIntent` | `CancelOperation` |
| `ExecuteDiagnosticDumpIntent` | `DumpDiagnostics` |

#### `NodeOpSlaveTranslator` (sealed class, `IOrchestrationTranslator`)

ACL translator for the SimHost (slave) side.

**Constructor**

```csharp
public NodeOpSlaveTranslator(
    DdsReader<NodeOpCommand>   commandReader,
    DdsWriter<NodeOpStatus>    statusWriter,
    DdsWriter<NodeHeartbeat>   heartbeatWriter,
    FdpEventBus                bus,
    int                        nodeId,
    JsonSerializerOptions?     jsonOptions = null)
```

**Public Methods**

| Method | Description |
|--------|-------------|
| `Tick()` | One frame: reads `NodeOpCommand` for this node's ID, publishes `ExecuteNodeOpIntent`; drains `NodeHeartbeatEvent` from bus and writes `NodeHeartbeat`; drains `NodeOpCompletedEvent` and writes `NodeOpStatus`. |
| `Update()` | Alias for `Tick()` implementing `IOrchestrationTranslator`. |
| `Dispose()` | Disposes `_commandReader`, `_statusWriter`, `_heartbeatWriter`. |

**Internal helper (exposed for tests)**

```csharp
internal static object? DeserializeNodePayload(NedNodeOpType operation, string? payloadJson)
```

Deserialises `payloadJson` to a typed domain payload object based on the operation
discriminator.  Returns `null` for operations that carry no payload.

#### `OrchestrationObserverTranslator` (sealed class, `IDisposable`)

Read-only multi-topic subscriber that republishes DDS data as bus events for
`ClusterUiCache` and monitoring subsystems.

**Constructor**

```csharp
public OrchestrationObserverTranslator(DdsParticipant participant, FdpEventBus bus)
```

**Public Methods**

| Method | Description |
|--------|-------------|
| `Tick()` | Polls all seven DDS topics and publishes translated events. Call before `FdpEventBus.SwapBuffers`. |
| `Dispose()` | Disposes all seven `DdsReader` instances. |

**Translation table**

| DDS Topic | Bus Event |
|-----------|-----------|
| `ClusterStateTopic` | `ClusterStateUpdateEvent` |
| `AssetInventoryTopic` | `AssetInventoryUpdateEvent` |
| `NodeHeartbeat` | `NodeHeartbeatEvent` |
| `SwitchTimeModeWireDto` | `SwitchTimeModeEvent` (unmanaged) |
| `ClusterOpStatus` | `ClusterOpCompletedEvent` |
| `NodeOpCommand` | `ExecuteNodeOpIntent` (promiscuous) |
| `NodeOpStatus` | `NodeOpCompletedEvent` |

#### `IClusterOpHandler` (interface)

Implemented by each per-subsystem component that participates in the 2PC protocol.
Lives entirely in the Hrot application layer; no `FDP.*` project may implement it.

```csharp
public interface IClusterOpHandler
{
    bool CanHandle(NodeOpType op);
    Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct);
    void Commit(NodeOpCommand cmd, EntityRepository? repo);
    void Abort(NodeOpCommand cmd, EntityRepository? repo);
}
```

| Method | Description |
|--------|-------------|
| `CanHandle(op)` | Returns `true` when this handler owns the given operation type |
| `PrepareAsync(cmd, ct)` | Async preparation; must not mutate ECS state; returns `null` on success or error string on failure |
| `Commit(cmd, repo)` | Commits the prepared command from the main thread; may mutate ECS via `repo` |
| `Abort(cmd, repo)` | Rolls back resources allocated during `PrepareAsync` |

`repo` is `null` for no-ECS subsystems (ExCon, CGF skeleton).

#### `ITickableClusterOpHandler` (interface, extends `IClusterOpHandler`)

Optional extension for handlers that produce deferred async ACKs.

```csharp
public interface ITickableClusterOpHandler : IClusterOpHandler
{
    void DrainDeferredAcks();
}
```

`ClusterSlave.Tick()` calls `DrainDeferredAcks` each frame on every registered
handler that implements this interface, allowing background I/O completions to
publish their `NodeOpStatus` ACKs.

#### `HrotHandlerAdapter` (sealed class, `ITickableClusterStateHandler`)

Migration shim that wraps `IClusterOpHandler` to satisfy
`Fdp.Toolkit.Orchestration.IClusterStateHandler`.

```csharp
public sealed class HrotHandlerAdapter : Fdp.Toolkit.Orchestration.ITickableClusterStateHandler
```

**Constructor**

```csharp
public HrotHandlerAdapter(IClusterOpHandler inner, EntityRepository? repo = null)
```

**Properties**

| Property | Type | Description |
|----------|------|-------------|
| `InnerHandler` | `IClusterOpHandler` | The wrapped Hrot-layer handler |

**Methods**

| Method | Description |
|--------|-------------|
| `CanHandle(FdpNodeOpType)` | Delegates to `_inner.CanHandle` with enum cast |
| `PrepareAsync(ExecuteNodeOpIntent, ct)` | Converts to `NodeOpCommand`, delegates to `_inner.PrepareAsync` |
| `Commit(ExecuteNodeOpIntent, repo)` | Delegates with `NodeOpCommand` conversion |
| `Abort(ExecuteNodeOpIntent, repo)` | Delegates with `NodeOpCommand` conversion |
| `DrainDeferredAcks()` | Forwards to `ITickableClusterOpHandler.DrainDeferredAcks` if inner implements it |

#### `ClusterStateChangedEvent` (struct)

Bus event published when the cluster state machine transitions.
Event ID: 7001.  Data policy: `NoRecord`.

```csharp
[EventId(7001)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ClusterStateChangedEvent
{
    public ClusterState Previous;
    public ClusterState Next;
}
```

Published by `ClusterSlave` after a `CommitState` command is processed.
Subscribers receive it via `FdpEventBus` without any dependency on DDS or the
orchestration ACL.

#### `ListenerRecordReplayController` (sealed class, `IRecordReplayController`)

No-op implementation of `IRecordReplayController` for listener/instructor nodes
(IG, ExCon) that participate in the cluster handshake but do not record or replay
ECS frame data.

**Constructor**

```csharp
public ListenerRecordReplayController(string nodeName = "Listener")
```

**Properties / Methods**

| Member | Behaviour |
|--------|-----------|
| `PrepareRecordingAsync(exerciseId, dir)` | Logs and returns `Task.CompletedTask` |
| `FinalizeRecordingAsync(maxNetworkId)` | Logs and returns `Task.CompletedTask` |
| `PrepareReplayAsync(exerciseId, dir)` | Sets `_replayActive = true`, returns `Task.CompletedTask` |
| `SeekToTimeAsync(ticks)` | Returns `Task.FromResult(default(GlobalTime))` |
| `ProcessPlaybackTick(currentTime)` | No-op |
| `TeardownReplayAsync()` | Clears `_replayActive`, returns `Task.CompletedTask` |
| `IsReplayActive` | Tracks whether `PrepareReplayAsync` has been called without a matching `TeardownReplayAsync` |
| `ActiveMaxNetworkId` | Always `0` |
| `ActiveReplayDurationSeconds` | Always `0f` |
| `ActiveRecordingStartWallTicks` | Always `0` |
| `GetCurrentReplayTime()` | Returns `default(GlobalTime)` |

---

### Namespace `Hrot.Common.Infrastructure`

#### `DdsIdAllocatorHelper` (static class)

Startup guard that waits for the remote DDS ID allocator server hosted by
`Hrot.Orchestrator` to announce a publication match before the local node proceeds.

```csharp
public static class DdsIdAllocatorHelper
```

**Methods**

```csharp
public static void EnsureRouting(DdsParticipant participant, DdsIdAllocator idAllocator)
```

| Parameter | Description |
|-----------|-------------|
| `participant` | Active DDS participant (used for diagnostics context only) |
| `idAllocator` | The `DdsIdAllocator` whose publication match is awaited |

Behaviour:
- Polls `idAllocator.HasPublicationMatch` every 50 ms.
- Logs a warning after 5 s if no match.
- Throws `InvalidOperationException` after 30 s if still no match.
- Returns immediately if `idAllocator` is `null`.

---

### Namespace `Hrot.Common.Orchestration.Handlers`

#### `PreviewClusterOpHandler` (sealed class, `IClusterOpHandler`)

Implements the dry-run snapshot / rewind protocol for the `LoadingPreview` /
`UnloadingPreview` cluster state transitions.

**Constructor**

```csharp
public PreviewClusterOpHandler(EntityRepository? liveRepo)
```

Pass `null` for `liveRepo` in no-ECS subsystems (ExCon, IG, CGF skeleton); the
handler will participate in 2PC ACKs but skip snapshot I/O.

**Public Methods**

| Method | Description |
|--------|-------------|
| `CanHandle(op)` | Returns `true` for `NodeOpType.PrepareState` only |
| `PrepareAsync(cmd, ct)` | No async work; returns `null` (success) immediately |
| `Commit(cmd, repo)` | Calls `LoadingPreviewCommit` or `UnloadingPreviewCommit` based on `TargetState` in `PayloadJson`; no-op for all other targets |
| `Abort(cmd, repo)` | Disposes and clears the in-progress snapshot |
| `TriggerLoadingPreview()` | Directly triggers snapshot without 2PC; for offline editor adapters |
| `TriggerUnloadingPreview()` | Directly triggers rewind without 2PC; for offline editor adapters |

**Internal Test Accessor**

```csharp
internal EntityRepository? TestHook_Snap
```

Returns the current in-memory snapshot, or `null` when no dry-run is active.

---

## Dependencies

### Project References

| Project | Purpose |
|---------|---------|
| `FDP/Engine/Fdp.Core` | `FdpEventBus`, `EntityRepository`, `FdpLog<T>`, `GlobalTime`, `IRecordReplayController`, serialisation helpers |
| `FDP/Toolkits/Fdp.Toolkits` | `Fdp.Toolkit.Orchestration` (ClusterSlave, ClusterMaster, intent events, handler interfaces), `Fdp.Toolkit.Time` types |
| `FDP/Network/Fdp.Network.Cyclone` | `DdsReader<T>`, `DdsWriter<T>`, `DdsParticipant`, `DdsIdAllocator` |
| `Hrot/Engine/Hrot.Core` | `Hrot.NED.Descriptors.Orchestration` wire types (this assembly consumes them from `OrchestrationMessages.cs` included via Hrot.Core) |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `CycloneDDS.NET` | 0.2.2 | CycloneDDS C# binding; provides `CycloneDDS.Schema`, `CycloneDDS.Runtime` |

### InternalsVisibleTo

The assembly exposes its `internal` members to:

- `Hrot.SimHost.Tests`
- `Hrot.Editor.Tests`

---

## Usage Examples

### Example 1: Wiring the Orchestrator (master) translators

```csharp
// Inside Hrot.Orchestrator startup, after DDS participant and bus are available.

var participant = new DdsParticipant();
var bus = new FdpEventBus();

// Cluster-level ingress + egress (Orchestrator receives ClusterOpRequest, writes ClusterOpStatus)
var clusterTranslator = new ClusterOpMasterTranslator(
    requestReader:      new DdsReader<ClusterOpRequest>(participant),
    statusWriter:       new DdsWriter<ClusterOpStatus>(participant),
    bus:                bus,
    inventoryWriter:    new DdsWriter<AssetInventoryTopic>(participant),
    clusterStateWriter: new DdsWriter<ClusterStateTopic>(participant));

// Node-op fan-out (Orchestrator writes NodeOpCommand, reads NodeOpStatus)
var nodeWriters = new Dictionary<int, DdsWriter<NodeOpCommand>>
{
    [1] = new DdsWriter<NodeOpCommand>(participant),
    [2] = new DdsWriter<NodeOpCommand>(participant),
};
var nodeOpMaster = new NodeOpMasterTranslator(
    commandWriters: nodeWriters,
    statusReader:   new DdsReader<NodeOpStatus>(participant),
    bus:            bus);

// Tick both translators once per frame (before or after bus SwapBuffers
// depending on whether the Orchestrator is the bus producer or consumer).
void OrchestratorFrame()
{
    bus.SwapBuffers();
    clusterTranslator.Tick();
    nodeOpMaster.Tick();
}
```

### Example 2: Wiring a SimHost (slave) node

```csharp
// Inside Hrot.SimHost startup.

var participant = new DdsParticipant();
var bus = new FdpEventBus();
const int NodeId = 1;

// Ensure Orchestrator DDS ID allocator is reachable before starting.
var idAllocator = new DdsIdAllocator(participant);
DdsIdAllocatorHelper.EnsureRouting(participant, idAllocator);

// Slave translator handles inbound NodeOpCommand and outbound NodeHeartbeat/NodeOpStatus.
var slaveTranslator = new NodeOpSlaveTranslator(
    commandReader:   new DdsReader<NodeOpCommand>(participant),
    statusWriter:    new DdsWriter<NodeOpStatus>(participant),
    heartbeatWriter: new DdsWriter<NodeHeartbeat>(participant),
    bus:             bus,
    nodeId:          NodeId);

// Register domain handlers via the migration adapter.
var handlers = new List<Fdp.Toolkit.Orchestration.IClusterStateHandler>
{
    new HrotHandlerAdapter(new PreviewClusterOpHandler(liveRepo: myEntityRepo)),
    new HrotHandlerAdapter(myLoadHandler, repo: myEntityRepo),
};

// Per-frame update.
void SimHostFrame()
{
    bus.SwapBuffers();
    slaveTranslator.Tick();
    // ClusterSlave.Tick() is called separately and processes ExecuteNodeOpIntent events.
}
```

### Example 3: Observer translator for ExCon / UI monitoring

```csharp
// Inside ExCon or a monitoring subsystem startup.

var participant = new DdsParticipant();
var bus = new FdpEventBus();

// Read-only: observe all seven orchestration DDS topics.
var observer = new OrchestrationObserverTranslator(participant, bus);

// Egress: let the ExCon UI send cluster commands via bus events.
var egress = new ClusterOpEgressTranslator(bus, participant);

// No-op record/replay controller for this listener node.
var rrController = new ListenerRecordReplayController(nodeName: "ExCon");

void ExConFrame()
{
    // 1. Tick the observer BEFORE SwapBuffers so translated events appear in this frame.
    observer.Tick();

    bus.SwapBuffers();

    // 2. Tick the egress AFTER SwapBuffers so intents published last frame are sent.
    egress.Tick();

    // 3. ClusterUiCache.Update() processes bus events (ClusterStateUpdateEvent, etc.).
    clusterUiCache.Update();
}

// Cleanup on shutdown.
observer.Dispose();
egress.Dispose();
```

### Example 4: Sending a state-transition command from ExCon

```csharp
// Publish a TransitionStateIntent onto the bus; ClusterOpEgressTranslator
// will serialize it to ClusterOpRequest DDS on the next Tick().

bus.PublishManaged(new TransitionStateIntent
{
    TransactionId   = Guid.NewGuid(),
    TargetState     = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
    ScenarioId      = "HILL_ATTACK_001",
    ExerciseId      = Guid.NewGuid(),
    TimeMode        = "RealTime",
    TargetWallTicks = 0,
});
```

### Example 5: Implementing and registering a custom node-op handler

```csharp
// Custom handler for taking snapshots at LoadingLive.
public sealed class MySnapshotHandler : IClusterOpHandler
{
    public bool CanHandle(NodeOpType op) =>
        op == NodeOpType.PrepareLive || op == NodeOpType.FinalizeLive;

    public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
    {
        // Async I/O -- do not touch ECS here.
        return Task.FromResult<string?>(null); // success
    }

    public void Commit(NodeOpCommand cmd, EntityRepository? repo)
    {
        // Main-thread commit; may mutate ECS via repo.
    }

    public void Abort(NodeOpCommand cmd, EntityRepository? repo)
    {
        // Roll back PrepareAsync resources.
    }
}

// Wrap and register:
var adapter = new HrotHandlerAdapter(new MySnapshotHandler(), repo: myRepo);
clusterSlave.RegisterHandler(adapter);
```

---

## Best Practices

### 1. Call order within a frame

The translator `Tick()` methods are not symmetric -- the ordering relative to
`FdpEventBus.SwapBuffers()` matters:

- **Observer / slave ingress** translators (`OrchestrationObserverTranslator`,
  `NodeOpSlaveTranslator`) must be called **before** `SwapBuffers` so that the
  events they publish are available to consumers in the same frame.

- **Egress** translators (`ClusterOpEgressTranslator`, `NodeOpMasterTranslator`,
  `ClusterOpMasterTranslator` egress drain) must be called **after**
  `SwapBuffers` so that intents published in the previous frame are dispatched.

### 2. JSON serialisation

Always use `OrchestrationJsonOptions.Default` (delegates to
`FdpJsonOptionsRegistry.DefaultRelaxed`) for all DDS payload round-trips.  This
enforces string-based enum serialisation and rejects integer enum values, preventing
silent wire-format mismatches as new enum members are added to `ClusterState` or
`ClusterOpType`.

### 3. Null-safety for `EntityRepository`

`IClusterOpHandler.Commit` and `Abort` receive a nullable `EntityRepository?`.
Handlers for no-ECS subsystems (IG, ExCon) **must** tolerate a `null` repo.
`PreviewClusterOpHandler` demonstrates the correct pattern: check for `null` and
log a warning rather than throwing.

### 4. `HrotHandlerAdapter` lifecycle

The adapter is a temporary migration shim scheduled for removal after all handlers
have migrated to implement `IClusterStateHandler` directly (see code comment
referencing G0404/G0405 milestones).  Do not build new handlers against
`IClusterOpHandler`; implement `Fdp.Toolkit.Orchestration.IClusterStateHandler`
directly.

### 5. `DdsIdAllocatorHelper.EnsureRouting` at startup

Call `EnsureRouting` during node startup before entering the main loop.  If
`Hrot.Orchestrator` is not reachable within 30 seconds the method throws, which
prevents the node from operating with an unrouted ID allocator.  Do not suppress
this exception.

### 6. `ListenerRecordReplayController` for non-simulation nodes

Any node that participates in the cluster 2PC but does not record or replay ECS
frame data (IG, ExCon, CGF skeleton) should use `ListenerRecordReplayController`
rather than leaving `IRecordReplayController` unimplemented.  The controller
tracks `IsReplayActive` correctly, which gates the Live-from-Replay branch in
`ReferenceReplayLoadHandler`.

### 7. `NodeOpCommand` is keyed by `TargetNodeId`

The DDS QoS for `NodeOpCommand` is KeepAll + Volatile with a per-node key.
`NodeOpSlaveTranslator` applies a client-side filter (`cmd.TargetNodeId == _nodeId`)
after reading; do not rely on QoS filtering alone because
`OrchestrationObserverTranslator` reads all commands promiscuously (for 2PC
history tracking).

### 8. Enum cast pattern

The assembly uses explicit integer casts for all NED-to-FDP and FDP-to-NED enum
conversions (e.g. `(FdpClusterState)(int)s.Data.CurrentState`).  This pattern
deliberately has no compile-time type coupling between the NED descriptors and the
FDP toolkit enums, allowing them to evolve independently.  The values are
kept in sync by the `// keep in sync` comment in `OrchestrationMessages.cs`.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.Core` (via `Hrot.NED`) | Provides `Hrot.NED.Descriptors.Orchestration` wire types (`ClusterState`, `NodeOpType`, DDS topic structs) that this assembly translates |
| `FDP/Toolkits/Fdp.Toolkits` | Provides `Fdp.Toolkit.Orchestration` (ClusterMaster, ClusterSlave, all typed intent events, `IClusterStateHandler`, `OrchestrationStatusCode`) that this assembly bridges to DDS |
| `FDP/Engine/Fdp.Core` | Provides `FdpEventBus`, `EntityRepository`, `FdpLog<T>`, `IRecordReplayController`, `GlobalTime`, `FdpJsonOptionsRegistry` |
| `FDP/Network/Fdp.Network.Cyclone` | Provides CycloneDDS C# wrappers (`DdsReader<T>`, `DdsWriter<T>`, `DdsParticipant`, `DdsIdAllocator`) |
| `Hrot.Network.NED` | Sibling assembly that defines additional NED wire types; see `docs/projects/Hrot/Network/Hrot.Network.NED.md` |
| `Hrot.Orchestrator` | Host process for `ClusterOpMasterTranslator` and `NodeOpMasterTranslator`; owns the `ClusterMaster` instance |
| `Hrot.SimHost` | Host process for `NodeOpSlaveTranslator`; owns `ClusterSlave` and `IClusterOpHandler` registrations |
| `Hrot.Editor` | Host process for `PreviewClusterOpHandler` and `ListenerRecordReplayController` in offline mode |
| `Hrot.IG` (Instructor GUI) | Uses `OrchestrationObserverTranslator` and `ClusterOpEgressTranslator` for read/write cluster access without direct Orchestrator coupling |
| `Hrot.SimHost.Tests` / `Hrot.Editor.Tests` | Test assemblies with `InternalsVisibleTo` access; exercise internal helpers such as `NodeOpSlaveTranslator.DeserializeNodePayload` and `PreviewClusterOpHandler.TestHook_Snap` |
