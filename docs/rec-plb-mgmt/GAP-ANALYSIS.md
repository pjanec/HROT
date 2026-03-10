# Gap Analysis & Open Questions
## Distributed Drill Management System (Rec/Plb/Mgmt)

> **Purpose:** This document maps what currently exists in the codebase against what the
> [design-talk.md](./design-talk.md) requires. It lists every gap, every discovered issue,
> and every open question that must be answered before implementation can start.

> **Update (rev 2):** All Q-1 … Q-13 answered. GAP-3, ISSUE-3, ISSUE-7 resolved.
> GAP-5, GAP-6, ISSUE-2 updated to reflect architectural decisions.
> See [DESIGN.md](./DESIGN.md) for the updated architecture.

---

## Table of Contents

1. [Existing Infrastructure Inventory](#1-existing-infrastructure-inventory)
2. [Gap List](#2-gap-list)
3. [Issues & Concerns Discovered in the Design Talk](#3-issues--concerns-discovered-in-the-design-talk)
4. [Open Questions — Must Answer Before Implementation](#4-open-questions--must-answer-before-implementation)

---

## 1. Existing Infrastructure Inventory

Below is the relevant existing code that the new system will build upon.

### 1.1 Recording & Playback

| Asset | Location | State | Notes |
|-------|----------|-------|-------|
| `AsyncRecorder` | `FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` | ✅ Exists | Double-buffered, LZ4, 32MB buffer. Writes `.fdp` + `.fdp.meta.json`. |
| `RecorderSystem` | `FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs` | ✅ Exists | Writes delta frames keyed by `repo.GlobalVersion`; single filter: `MinRecordableId`. |
| `PlaybackController` | `FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackController.cs` | ✅ Exists | Binary-search frame index (`FrameMetadata[]`). Has `SeekToFrame(idx)` + `SeekToTick(tick)`. |
| `PlaybackSystem` | `FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackSystem.cs` | ✅ Exists | Applies decompressed frames to `EntityRepository`. |
| `RecordingModule` (NetworkDemo) | `FDP/Examples/Fdp.Examples.NetworkDemo/Modules/RecordingModule.cs` | ✅ Exists (Demo) | Thin wrapper; no DrillId, no multi-recorder support. |
| `FlightRecorderExample` | `FDP/Kernel/Fdp.Kernel/FlightRecorder/FlightRecorderExample.cs` | ✅ Exists (Example only) | — |

### 1.2 ECS Core

| Asset | Location | State | Notes |
|-------|----------|-------|-------|
| `EntityRepository` | `FDP/Kernel/Fdp.Kernel/EntityRepository.cs` | ✅ Exists | `GlobalVersion`, `SimulationTime`, `SoftClear()`. `SyncFrom()` in `EntityRepository.Sync.cs` handles full repo clone (unmanaged + managed). |
| `EntityRepository.SyncFrom()` | `FDP/Kernel/Fdp.Kernel/EntityRepository.Sync.cs` | ✅ Exists | Copies all component tables from a source repo; supports `BitMask256` filter and `excludeTypes`. Used by `DoubleBufferProvider.Update()`. This IS the snapshot API. |
| `NativeChunkTable<T>` | `FDP/Kernel/Fdp.Kernel/NativeChunkTable.cs` | ✅ Exists | `CopyChunkToBuffer()`, `RestoreChunkFromBuffer()` per chunk. Full-repo clone handled by `SyncFrom()`. |
| `ManagedComponentTable<T>` | `FDP/Kernel/Fdp.Kernel/ManagedComponentTable.cs` | ✅ Exists | `SyncDirtyChunks()` performs deep clone via `FdpAutoSerializer.DeepClone()` when `ComponentTypeRegistry.NeedsClone(typeId)` is true. |
| `ComponentType` | `FDP/Kernel/Fdp.Kernel/ComponentType.cs` | ✅ Exists | `SetSnapshotable()`, `_needsClone` flags. Clone path IS exercised by `ManagedComponentTable.SyncDirtyChunks()`. |
| `FdpEventBus` | (within `EntityRepository`) | ✅ Exists | In-process events; NOT snapshotted. In-flight DDS messages are handled by the supplement file approach (see GAP-5). |

### 1.3 Time & Deterministic Stepping

| Asset | Location | State | Notes |
|-------|----------|-------|-------|
| `TimePulseDescriptor` | `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | ✅ Exists | Has `MasterWallTicks` — key field for replay sync. |
| `FrameOrderDescriptor` / `FrameAckDescriptor` | Same file | ✅ Exists | Deterministic stepping protocol fully designed. |
| `SwitchTimeModeEvent` | Same file | ✅ Exists | Broadcasts mode switch (Continuous ↔ Deterministic). |
| `MasterTimeController` | `FDP/Toolkits/FDP.Toolkit.Time/Controllers/` | ✅ Exists | Normal continuous master. |
| `SlaveTimeController` | Same | ✅ Exists | PLL-based slave. |
| `SteppedMasterController` | Same | ✅ Exists | Deterministic batch master. |
| `SteppedSlaveController` | Same | ✅ Exists | Deterministic batch slave. |
| `SwitchableTimeController` | Same | ✅ Exists | Can swap between controller implementations at runtime. |
| `DistributedTimeCoordinator` | Same | ✅ Exists | Overall coordinator. |

### 1.4 Module Host

| Asset | Location | State | Notes |
|-------|----------|-------|-------|
| `IModule` | `FDP/ModuleHost/ModuleHost.Core/Abstractions/IModule.cs` | ✅ Exists | `RegisterSystems()`, `Tick()`, `ExecutionPolicy`. |
| `SystemPhase` | Same | ✅ Exists | `Input`, `BeforeSync`, `Simulation`, `PostSimulation`, `Export`. |
| `ISnapshotProvider` | Same | ✅ Exists | GDB=`DoubleBufferProvider`, SoD=`OnDemandProvider`, Shared=`SharedSnapshotProvider`. `DoubleBufferProvider.Update()` calls `_replica.SyncFrom(_liveWorld, _mask)` — this IS the pattern to reuse for checkpoints. |
| `ModuleHostKernel` | `FDP/ModuleHost/ModuleHost.Core/ModuleHostKernel.cs` | ✅ Exists | Runs modules; no DSM state hook needed (use internal `FdpEventBus` event instead). |

### 1.5 DDS / Networking

| Asset | Location | State | Notes |
|-------|----------|-------|-------|
| `SubsystemStatusAnnounce` | `Bagira.DDS.DataModel/Runner/SubsystemStatusAnnounce.cs` | ✅ Exists | Startup-only waiting-room protocol. `NodeId`, `SubsystemName`, `Ready`. |
| `WaitingRoomCoordinator` | `Bagira.Runner/Services/WaitingRoomCoordinator.cs` | ✅ Exists | Polls peers until all discovered. Timeout = 30s. |
| Simulation descriptors | `Bagira.DDS.DataModel/SimDescriptors.cs` | ✅ Exists | `GeoSpatial`, `GeoSpatialDR`, `EntityDamage`. |
| Entity CRUD messages | `Bagira.DDS.DataModel/GenericMessages.cs` | ✅ Exists | `CreateEntityRequest`, `UpdateEntityDescriptorRequest`, etc. |
| Mission control | `Bagira.DDS.DataModel/MissionMessages.cs` | ✅ Exists | `MissionControlRequest/Ack`. |

### 1.6 Application Layer

| Asset | Location | State | Notes |
|-------|----------|-------|-------|
| `SimHostApp.cs` | `Bagira.SimHost/SimHostApp.cs` | ✅ Exists | ModuleHost kernel setup for SimHost. |
| `IgApplication.cs` | `Bagira.IG/IgApplication.cs` | ✅ Exists | ModuleHost kernel setup for IG. |
| `IosLogic.cs` / `IosMock.cs` | `Bagira.IOS/` | ✅ Exists | IOS business logic; no DSM UI or SysOp awareness. |

---

## 2. Gap List

### GAP-1: Wall-Clock Timestamps Missing from Frame Headers

**Severity: HIGH (blocks distributed replay seek)**

`RecorderSystem.RecordDeltaFrame()` writes `repo.GlobalVersion` as the frame tick.
`PlaybackController.BuildFrameIndex()` reads this as `FrameMetadata.Tick`.

The `PlaybackController.SeekToTick(ulong tick)` searches by ECS tick, not by wall-clock time.

The design requires `SeekToWallClockTicks(long wallTicks)` so that the replay master
can broadcast a `TimePulseDescriptor.MasterWallTicks` and all slaves seek to exactly
the right moment independently of how fast they were running during recording.

**What to add:**
- `RecorderSystem.RecordDeltaFrame()`: write `DateTime.UtcNow.Ticks` (8 bytes) into
  every frame header alongside the existing ECS tick.
- `FrameMetadata` struct: add `long WallClockTicks` field.
- `PlaybackController.BuildFrameIndex()`: read and populate `WallClockTicks`.
- `PlaybackController.SeekToWallClockTicks(long wallTicks)`: binary search by
  `WallClockTicks` in the frame index.
- `FdpConfig.FORMAT_VERSION`: bump to reflect the new frame header format.
- All existing `.fdp` test files and test recordings become incompatible
  (see **Q-1** for backward-compatibility strategy).

---

### GAP-2: No Entity Query Filter in RecorderSystem / AsyncRecorder

**Severity: HIGH (blocks Story recording)**

`RecorderSystem` offers `MinRecordableId` as its only entity filter.
Story recording needs to record **only entities with a given `StoryTag`**.

**What to add:**
- `RecorderSystem.EntityFilter: Predicate<int>?` — if set, called per entity ID;
  entity is skipped if predicate returns false.
- `AsyncRecorder`: expose a `SetEntityFilter(Predicate<int>)` method that passes
  through to its internal `RecorderSystem`.
- Thread-safety: filter itself should not close over mutable state; the
  `StoryEntityFilter` implementation should read from the `EntityRepository` safely
  at the point `RecordDeltaFrame` is called on the recorder's background thread.

---

### GAP-3: No High-Level Repository Snapshot API

> **STATUS: RESOLVED** — `EntityRepository.SyncFrom()` already exists in
> `EntityRepository.Sync.cs` and handles the full snapshot use-case. No new kernel API
> is needed.

**Resolution:** Create a new `EntityRepository` using the live repo’s schema, then call
`destRepo.SyncFrom(liveRepo)`. This is exactly the pattern used by
`DoubleBufferProvider.Update()` in `ModuleHost.Core/Providers/DoubleBufferProvider.cs`.

- Unmanaged tables: fast `NativeChunkTable` memcpy (~2 ms for typical worlds)
- Managed tables: `ManagedComponentTable.SyncDirtyChunks()` deep-clones via
  `FdpAutoSerializer.DeepClone()` when `ComponentTypeRegistry.NeedsClone(typeId)` is true
- Entity index: copied via the same `SyncFrom` path

Checkpoint handler calls `destRepo.SyncFrom(liveRepo)` on the main thread at `BeforeSync`
(no pause), then passes `destRepo` to a background task for LZ4 compression + disk write.

---

### GAP-4: No Drill State Machine (DSM)

**Severity: HIGH — the entire feature set depends on this**

There is NO `DSMState`, `SystemStateTopic`, `SysOpRequest`, `SysOpStatus`,
`NodeOpCommand`, `NodeOpStatus`, `DrillMaster`, or `DrillSlave`
anywhere in the codebase.

`SubsystemStatusAnnounce` + `WaitingRoomCoordinator` serve a related but different
purpose (startup peer discovery, not runtime DSM). They should NOT be repurposed.

**What to add:** See [DESIGN.md §2](./DESIGN.md#2-dds-message-schema) and §5–6.

---

### GAP-5: FdpEventBus State Not Captured During Snapshot (In-Flight Messages)

**Severity: MEDIUM**

> **DESIGN DECISION MADE:** Snapshots are taken **without pausing** the simulation.
> The Drain-and-Quiesce protocol (which required pausing the TimeController) is replaced
> by an **async in-flight supplement file** approach.

**Resolution:** After `SyncFrom()` returns on the main thread, a background thread
watches the DDS ingress queue for ~50 ms and captures any messages that were in-flight
at snapshot time. These are written to a `.dds_supplement.bin` file alongside the `.fdp`.

During restore:
1. Load `.fdp` → `destRepo.SyncFrom` equivalent via `PlaybackSystem.ApplyFrame()`
2. Replay `.dds_supplement.bin` messages into the restored repo

This achieves causal consistency without any simulation pause. The DDS ingress system
does NOT need to be decoupled from the `ModuleHostKernel.RunOnce()` call.

---

### GAP-6: IModule Has No DSM State Change Hooks

**Severity: MEDIUM**

> **DESIGN DECISION MADE:** No `IModule` interface changes. DSM state change
> notifications use the internal `FdpEventBus`.

**Resolution:** When `DrillSlave` commits a new DSM state, it publishes:

```csharp
_eventBus.Publish(new DsmStateChangedEvent { Previous = prev, Next = next });
```

Any module needing to react (e.g. disable physics during `RunningReplay`) subscribes:

```csharp
_eventBus.Subscribe<DsmStateChangedEvent>(OnDsmStateChanged);
```

This is the least invasive approach and requires no changes to the existing `IModule`
interface or `ModuleHostKernel`.

---

### GAP-7: NodeHeartbeat Topic Does Not Exist

**Severity: MEDIUM**

`SubsystemStatusAnnounce` is published **once at startup** when `Ready=true`.
There is no ongoing health monitoring. The Master has no way to detect if SimHost
crashes mid-drill except by noticing missing `NodeOpStatus` responses (which only
exist after the DSM itself is implemented).

**What to add:** `NodeHeartbeat` DDS topic (see DESIGN.md §2.2 + §7).
The 1 Hz autonomous heartbeat must use a `System.Diagnostics.Stopwatch` (wall-clock),
NOT the ECS `SimulationTime` (which can be paused or running at non-1× speed).

---

### GAP-8: Story Infrastructure Does Not Exist

**Severity: MEDIUM (future feature)**

`StoryTag`, `StoryReplayTag`, `StoryRecorder` (filtered), `StoryPlaybackController`
(entity remapping), and `ComponentPatchMap` (entity reference byte-patching) do not
exist anywhere.

The `GlobalComponentIds` enum (used for `[ComponentId(GlobalComponentIds.XYZ)]`)
will need new ID allocations for `StoryTag` and `StoryReplayTag`.

---

### GAP-9: Battlespace Concept Does Not Exist

**Severity: MEDIUM (future feature)**

There is no `BattlespaceSpec`, no staged asset loading pattern, no `CmdSwapBattlespace`
ECS event, and no related DDS messages. The concept of "high-resolution terrain areas"
is entirely missing. The 2PC battlespace swap pattern must be built from scratch.

---

### GAP-10: DrillId Concept Does Not Exist

**Severity: MEDIUM**

`AsyncRecorder` takes a `filePath` string and includes a `Timestamp` in
`RecordingMetadata`, but there is no `DrillId` (GUID) that semantically links
all recordings from the same drill run, checkpoints taken during that run,
and the `SystemStateTopic`.

`RecordingMetadata` needs a `Guid DrillId` field.
The `AsyncRecorder` constructor (or its factory) must receive the `DrillId` from
the `DrillSlave` (which gets it from `NodeOpCommand.PayloadJson`).

---

### GAP-11: IOS Has No DSM Awareness or SysOp UI

**Severity: LOW (UI layer, can be stubbed initially)**

`IosLogic.cs` / `IosMock.cs` have no knowledge of `DSMState`, `SysOpRequest`, or
the drill lifecycle. A minimal IOS integration (even a command-line mock) is
needed for end-to-end integration testing of the DSM.

---

### GAP-12: PlaybackController Seek Is Frame-Index Based, Not Wall-Clock Based

**Severity: HIGH (subset of GAP-1)**

`PlaybackController.SeekToTick(ulong tick)` performs a linear scan for the first
frame whose `FrameMetadata.Tick >= targetTick`. This does NOT use binary search —
it scans from the beginning of the frame index.

For large recordings (hours of drill at 60 Hz = ~216 000 frames/hour), seeking
to a late point in the recording will take O(n) time scanning the index in memory.

**Two separate fixes needed:**
1. Store and search by `WallClockTicks` in frame headers (GAP-1).
2. Change the linear scan to a binary search (the existing `BuildFrameIndex()`
   produces a sorted list, so `List<T>.BinarySearch()` is directly applicable).

---

### GAP-13: No Archive / Cold Storage Support

**Severity: LOW (future feature)**

No export/import pipeline, no token-bucket upload orchestrator, no `drill_manifest.json`
schema. The functionality of finishing a recording and moving it to a shared NAS does
not exist.

---

### GAP-14: WaitingRoomCoordinator Is Startup-Only, Not Integrated with DSM Standby

**Severity: LOW (integration/structural)**

`WaitingRoomCoordinator` currently blocks the startup thread until peers are found,
then exits. After its use, systems start ticking with no shared DSM context.

The natural integration point is: after `WaitingRoomCoordinator` completes and
`DrillMaster` has built its initial `ActiveNodes` roster, the Master publishes
`SystemStateTopic(Standby)` to signal that the system is now ready for IOS commands.

---

## 3. Issues & Concerns Discovered in the Design Talk

### ISSUE-1: "Always Recording" During Pause Requires Wall-Clock Stamping

The design talk states:
> *"if simulation is paused, the ECS won't have physics changes, but it will have
> Event changes (UI clicks, tactical graphics drawn). The recorder will capture these."*

**Problem:** `RecorderSystem` writes frames triggered by ECS ticks (`repo.GlobalVersion`).
If `SimulationTime` is paused, `GlobalVersion` stops incrementing, so the recorder stops
producing frames — **even though DDS network events might still be arriving**.

**Resolution needed:** The recorder must continue operating on wall-clock heartbeats,
not only on ECS tick advancement. Alternatively, the `TimeController.Pause()` must
still advance `GlobalVersion` at a reduced rate to signal "ECS is alive but paused."

---

### ISSUE-2: Entity Reference Corruption During Story Replay

> **DESIGN DECISION MADE:** `ComponentPatchMap` is populated via **runtime reflection**
> using `Marshal.OffsetOf` at component registration time. No source generator.

When `StoryPlaybackController` injects recorded component data into new ghost entities,
any struct field of type `Entity` still holds the old recorded entity index.

**Resolution:** At `ComponentTypeRegistry.Register<T>()` time:
```csharp
typeof(T).GetFields()
    .Where(f => f.FieldType == typeof(Entity))
    .Select(f => (int)Marshal.OffsetOf<T>(f.Name))
```
Populates `ComponentPatchMap.EntityFieldByteOffsets` (computed once at startup, zero
runtime cost during actual story replay).

For **managed (class) ECS components**: if the managed type contains `Entity`-typed
fields, it must implement `IEntityRefPatchable`; otherwise `Register<T>()` throws
`NotSupportedException` at startup to catch incompatible types early.

---

### ISSUE-3: Managed Component Tables Cannot Be memcpy'd

> **STATUS: RESOLVED** — `ManagedComponentTable.SyncDirtyChunks()` already handles
> deep cloning of managed components via `FdpAutoSerializer.DeepClone()` when
> `ComponentTypeRegistry.NeedsClone(typeId)` is true. No new mechanism is needed.
> `EntityRepository.SyncFrom()` (in `EntityRepository.Sync.cs`) calls this path
> automatically, so the checkpoint/snapshot flow handles managed components correctly.

---

### ISSUE-4: Thundering Herd on Archive Export Is Real at Scale

If the system has 20 nodes and each finishes a 10 GB recording simultaneously,
naively copying to a shared NAS produces 200 GB of concurrent writes.

The token-bucket solution (DESIGN.md §12.1) addresses this, but the
`N_concurrent` parameter must be tunable from `config.json` and should default
conservatively (2–3). The Master must also handle node-level upload failures
gracefully (log and continue) rather than failing the entire export.

---

### ISSUE-5: Split-Brain After Master Crash Between Prepare and Commit

If the Master sends `NodeOpCommand(PrepareBattlespace)` to all nodes and 4/5 succeed,
but the Master crashes before sending `CommitBattlespace`:

- The 4 prepared nodes have staged (but uncommitted) terrain data in RAM.
- The new Master (after failover) publishes the last known `SystemStateTopic`
  (which still shows the pre-transition state).
- The prepared nodes detect a stale epoch and free their staged payloads.
- System returns to the pre-transition state cleanly.

However: **there is currently only ONE designated master** (the `Bagira.Orchestrator`
process). There is no Master failover mechanism. If the Orchestrator crashes,
the system has no way to elect a new one.

This is a significant architectural limitation. The design talk does not address
Master high-availability.

---

### ISSUE-6: DDS QoS Selection for NodeOpCommand / NodeOpStatus

> **RESOLVED (see Q-4):** `Reliable + Volatile`, `HistoryDepth=1`. Per-command-type
> timeout configurable in Orchestrator config. Late-joiner context delivered via the
> separate `OrchestratorContextTopic` (TransientLocal), not via command history.

---

### ISSUE-7: RecordingMetadata Schema Versioning

> **RESOLVED:** Bump `FdpConfig.FORMAT_VERSION`. No migration path. Old `.fdp` files
> recorded before the bump fail validation and must be re-recorded. This is accepted.
> The development team will update any fixture `.fdp` files used in unit tests.

---

### ISSUE-8: Story "Pause" Is Semantically Different from DSM Pause

The design talk specifies that pausing a single story must NOT pause the global DSM
clock. Instead, it freezes story entities by removing `CanMove` / `CanShoot`
capability flags.

This means the `PauseTime` `SysOpRequest` is a **global** operation across all nodes,
while story-level "pause" is a **targeted entity-component mutation**. These must NEVER
be confused in the IOS UI or in the command handlers.

---

### ISSUE-9: Dry Run Checkpoint Must Be Per-Node

When `LoadingDryRun` triggers `TakeSnapshot`, each node stores its own
`EntityRepository` snapshot (via `SyncFrom`) locally in RAM. If `UnloadingDryRun`
is triggered, the Master sends `NodeOpCommand(RestoreSnapshot)` and each node
calls `liveRepo.SyncFrom(snap)` from its own cached snapshot.

This means the RAM holding the dry-run snapshot on each node must survive the full
`RunningDryRun` phase (which could be minutes long). For large worlds (100k+ entities
across many component types), the snapshot could be several hundred MB per node.
This must be measured.

---

## 4. Open Questions — Must Answer Before Implementation

### Q-1: How to handle `.fdp` file backward compatibility after adding wall-clock timestamps?

> **RESOLVED:** Hard-break. Bump `FdpConfig.FORMAT_VERSION`. Old files fail loading —
> no migration tool. This is accepted; development team updates fixture files in tests.

---

### Q-2: Which node runs DrillMaster — SimHost or a dedicated BBroker?

> **RESOLVED:** Dedicated `Bagira.Orchestrator` project. `Bagira.Runner` is just a
> shell; `Bagira.Orchestrator` is a subsystem registered with the Runner and runs as a
> **separate process**. Neither SimHost nor IG are the master.

---

### Q-3: How does the IOS join the DSM? Does it have a DrillSlave?

> **RESOLVED:** IOS has a **lightweight slave** (`IosDrillSlaveModule`) with no ECS.
> It participates like any other node (heartbeat, `NodeOpStatus` replies, reacts to
> DSM state changes) but skips any `IDsmHandler` that touches `EntityRepository`.

---

### Q-4: What DDS QoS should NodeOpCommand and NodeOpStatus use?

> **RESOLVED:** `Reliable + Volatile`, `HistoryDepth=1`. Per-command-type timeout is
> configurable in the Orchestrator config (map of `NodeOpType` → `TimeoutSeconds`).
> If a node misses a command within the timeout, the transaction is aborted.

---

### Q-5: Should `ReplaySeek` be a SysOp or a new DSM sub-state?

> **RESOLVED:** Seek is a SysOp. No new DSM state. The Orchestrator freezes
> `TimePulseDescriptor` during the seek. Per-node-type timeout is configurable.

---

### Q-6: How are the story `.fdp` files differentiated from global recordings?

> **RESOLVED:**
> - Path: `/archives/{DrillId}/stories/{StoryId}_node{N}.fdp`
> - `ForgetStory` → **immediate file delete** on each node.
> - If the drill ends (`UnloadingLive`) while a story recording is still active
>   (IOS never called `StopStory`), the **partial recording is auto-deleted** by each
>   node during its `FinalizeLive` handler. Story cleanup is the node's responsibility,
>   not the Orchestrator's.

---

### Q-7: How is `ComponentPatchMap` populated — Source Generator or runtime reflection?

> **RESOLVED:** Runtime reflection via `Marshal.OffsetOf` at component registration
> time. No source generator. Computed once at startup; zero cost during replay.

---

### Q-8: Can `FdpEventBus` in-flight events be deterministically drained before snapshot?

> **RESOLVED:** The Drain-and-Quiesce protocol is **not used**. Snapshots are taken
> without pausing. In-flight DDS messages are captured asynchronously in a `.dds_supplement.bin`
> file (background thread watches ingress for ~50 ms after `SyncFrom()` returns).
> This replaces the need to drain `FdpEventBus` state or decouple DDS ingress from the
> main ECS tick. See GAP-5 for the full resolution.

---

### Q-9: What scenario format do the different nodes use for `SaveScenario`?

> **RESOLVED:** Scenario saving is **deferred / not implemented** in the initial
> delivery. The `SysOpRequest(SaveScenario)` handler will be a no-op stub.

---

### Q-10: How does the system handle a node that joins mid-drill (late joiner)?

> **RESOLVED:** The Orchestrator publishes `OrchestratorContextTopic` (TransientLocal,
-> HistoryDepth=1) whenever the drill context changes. A late-joining node reads this
-> topic immediately on connect, learns the current DSM state, DrillId, scenario, and
> required node list, then executes its internal join procedure (load assets, sync, etc.).
> Late joining IS supported — it is not prohibited.

---

### Q-11: Is Master high-availability needed (active-standby failover)?

> **RESOLVED:** Master HA is **out of scope**. Master (Orchestrator) crash = drill
> terminates. This is accepted. The Orchestrator runs as a separate process so its
> crash does not kill SimHost/IG. Slave nodes self-transition to `Degraded` on detecting
> Orchestrator heartbeat loss (> 5s silence).

---

### Q-12: "Live from RunningEdit" — Is LoadingLive valid from RunningEdit?

-> **RESOLVED:** `RunningEdit → LoadingLive` is a **valid transition**. The DSM table
-> and Mermaid diagram have been updated in DESIGN.md to reflect this.
>
> Additionally: transitioning through Standby and back to a new `LoadingX` state MUST
> NOT force a full asset reload. Assets in RAM are retained; only assets that differ
> between old and new scenario are unloaded/reloaded (asset caching across Standby).

---

### Q-13: How long can a SnapshotBuffer (Dry Run RAM) be held?

> **RESOLVED:** No upper bound on dry-run `RunningDryRun` duration. The snapshot
> (`EntityRepository` in RAM) is held for as long as needed. For large scenarios,
> async disk save is acceptable as an additional safety net (for recovery if the process
> crashes during `RunningDryRun`), but is not required in the initial implementation.

---

*End of Gap Analysis.*
