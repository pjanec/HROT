# BATCH-04: Checkpoint Event Preservation (Phase 5)

**Batch Number:** BATCH-04
**Tasks:** TASK-S501, TASK-S502, TASK-S503, TASK-S504
**Phase:** Phase 5 — Checkpoint Event Preservation
**Estimated Effort:** 3-4 hours
**Priority:** HIGH
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 (all complete)

---

## Onboarding & Workflow

### Developer Instructions

Phase 5 wires `EventAccumulator` event history into the binary checkpoint pipeline.
After this batch, a checkpoint `.fdp` file will contain all events that were live on
the simulation bus at the time of snapshotting — so when the checkpoint is loaded
back, systems that consume events will observe them as if the simulation had just
produced them.

The changes span 4 tightly-coupled tasks.  Complete them **in order** (S501 → S502 →
S503 → S504); each task depends on the previous one.

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Task definitions:** `.dev/cgf-scn-2/TASK-DETAIL.md` — see tasks S501–S504
3. **Design document:** `.dev/cgf-scn-2/DESIGN.md` — Phase 5 section
4. **Previous review:** `.dev/cgf-scn-2/reviews/BATCH-03-REVIEW.md`

### Source Code Locations

| Area | Path |
|------|------|
| FDP event-bus | `FDP/Engine/Fdp.Core/FdpEventBus.cs` |
| Native stream interface | `FDP/Engine/Fdp.Core/INativeEventStream.cs` |
| Managed stream | `FDP/Engine/Fdp.Core/ManagedEventStream.cs` |
| Managed stream info | `FDP/Engine/Fdp.Core/IManagedEventStreamInfo.cs` |
| Recorder | `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs` |
| Checkpoint I/O worker | `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs` |
| Checkpoint handler | `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceCheckpointHandler.cs` |
| Event accumulator | `FDP/Engine/Fdp.Core/EventAccumulator.cs` |
| HrotNodeContext | `Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeContext.cs` |
| HrotNodeBuilder | `Hrot/Engine/Hrot.Common/Infrastructure/HrotNodeBuilder.cs` |
| NodeBootstrapper | `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` |
| SimHostApp | `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` |
| Test: Fdp.Core.Tests | `FDP/Engine/Fdp.Core.Tests/` |
| Test: Hrot.SimHost.Tests | `Hrot/Subsystems/Hrot.SimHost.Tests/` |

### Build & Test Commands

```powershell
# Build everything
dotnet build IOS-IG-SimHost.sln --no-restore

# Run relevant test projects
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
```

**Pre-existing failures to ignore:** 7 failures in `Fdp.Toolkits.Tests` (struct size tests,
NavigationIntentBridge, PhysicsQueryActionNode, FireProcessingSystem) — always present,
unrelated to this work.

### Report Submission

When done, submit your report to:
`.dev/cgf-scn-2/reports/BATCH-04-REPORT.md`

If you have questions, create:
`.dev/cgf-scn-2/questions/BATCH-04-QUESTIONS.md`

---

## Context

Phases 1–4 fixed scenario serialization (NoSave tagging, InlineArray support, custom
translators, genesis intents).  Phase 5 addresses the **checkpoint pipeline**: currently
`CheckpointIOWorker.WriteCheckpointFile` serializes a snapshot of ECS state but omits
any events that were live on the bus.  After loading, systems that react to events
(e.g. weapon fire, passenger embarking) would not replay those events.

The fix uses the existing `EventAccumulator` (already captures per-frame event history)
to inject accumulated events into the snapshot's bus before writing it to disk.  The
recorder is then told to read from the *Current* (read-side) buffer instead of the
*Pending* (write-side) buffer.

---

## Tasks

### TASK-S501: Add `PopulateCurrentStreams` / `PopulateCurrentManagedStreams` to `FdpEventBus`

**Task Definition:** `.dev/cgf-scn-2/TASK-DETAIL.md#task-s501`

**Files to modify:**
- `FDP/Engine/Fdp.Core/IManagedEventStreamInfo.cs` — extend interface
- `FDP/Engine/Fdp.Core/ManagedEventStream.cs` — implement new interface member
- `FDP/Engine/Fdp.Core/FdpEventBus.cs` — add two new methods

#### 1. Extend `IManagedEventStreamInfo`

Add `IList CurrentEvents { get; }` to the interface:

```csharp
public interface IManagedEventStreamInfo
{
    int TypeId { get; }
    Type EventType { get; }
    IList PendingEvents { get; }
    IList CurrentEvents { get; }   // READ (front) buffer
}
```

Also add a default implementation (return empty) in `ManagedEventStreamInfo`:

```csharp
public IList CurrentEvents { get; set; } = Array.Empty<object>();
```

#### 2. Implement in `ManagedEventStream<T>`

`ManagedEventStream<T>` has `_front` (read/current) and `_back` (write/pending).
Add:

```csharp
public IList CurrentEvents => (IList)_front;
```

#### 3. Add methods to `FdpEventBus`

Add immediately after `PopulatePendingManagedStreams`:

```csharp
/// <summary>
/// Populates the provided list with active native event streams that have Current events.
/// Current = read buffer (populated after SwapBuffers).
/// Zero-allocation if list capacity is sufficient.
/// </summary>
public void PopulateCurrentStreams(List<INativeEventStream> target)
{
    target.Clear();
    foreach (var kvp in _nativeStreams)
    {
        var stream = kvp.Value;
        if (stream.GetRawBytes().Length > 0)
        {
            target.Add(stream);
        }
    }
}

/// <summary>
/// Populates the provided list with active managed event streams that have Current events.
/// Current = front buffer (populated after SwapBuffers).
/// Zero-allocation if list capacity is sufficient.
/// </summary>
public void PopulateCurrentManagedStreams(List<IManagedEventStreamInfo> target)
{
    target.Clear();
    foreach (var kvp in _managedStreams)
    {
        var streamObj = kvp.Value;
        if (streamObj is IManagedEventStreamInfo info && info.CurrentEvents.Count > 0)
        {
            target.Add(info);
        }
    }
}
```

**Why `GetRawBytes()`:** `NativeEventStream<T>.GetRawBytes()` reads from `_readBuffer`
(the Current/read buffer), while `GetPendingBytes()` reads from `_writeBuffer` (the
Pending/write buffer).  After `SwapBuffers()` the just-written events move into
`_readBuffer`, so `GetRawBytes()` is exactly the Current view we need.

#### Tests for S501

New file: `FDP/Engine/Fdp.Core.Tests/FdpEventBusCurrentStreamsTests.cs`

Write at least these test cases:

- **S501-T1** `PopulateCurrentStreams_EmptyBus_ReturnsEmptyList` — fresh bus, no events
- **S501-T2** `PopulateCurrentStreams_AfterPublishAndSwap_ReturnsStream` — publish a
  native event, call `bus.SwapBuffers()`, then assert list contains 1 stream with
  `GetRawBytes().Length > 0`
- **S501-T3** `PopulateCurrentStreams_BeforeSwap_ReturnsEmptyList` — publish a native
  event but do NOT swap; assert list is empty (event is still in Pending buffer)
- **S501-T4** `PopulateCurrentManagedStreams_AfterPublishAndSwap_ReturnsStream` — same
  pattern for a managed event type

---

### TASK-S502: `RecorderSystem.WriteEvents` — `serializeReadBuffer` flag

**Task Definition:** `.dev/cgf-scn-2/TASK-DETAIL.md#task-s502`

**File to modify:** `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`

#### Changes

1. Add `bool serializeReadBuffer = false` parameter to `WriteEvents`:

```csharp
private void WriteEvents(BinaryWriter writer, FdpEventBus? eventBus, bool serializeReadBuffer = false)
```

2. Inside `WriteEvents`, branch on the flag:
   - Native streams: when `serializeReadBuffer`, call `eventBus.PopulateCurrentStreams(_cachedNativeStreams)`
     instead of `eventBus.PopulatePendingStreams(_cachedNativeStreams)`.
     In the foreach loop use `stream.GetRawBytes()` instead of `stream.GetPendingBytes()`.
   - Managed streams: when `serializeReadBuffer`, call `eventBus.PopulateCurrentManagedStreams(_cachedManagedStreams)`
     instead of `eventBus.PopulatePendingManagedStreams(_cachedManagedStreams)`.
     In the foreach loop use `streamInfo.CurrentEvents` instead of `streamInfo.PendingEvents`.
   - Default (`serializeReadBuffer = false`) must be **identical to current behavior** — no
     behavioral change for existing callers.

3. Propagate the flag to `RecordKeyframe`:

```csharp
public void RecordKeyframe(EntityRepository repo, BinaryWriter writer,
    long wallClockTicks, FdpEventBus? eventBus = null, bool serializeReadBuffer = false)
{
    // ...existing code...
    WriteEvents(writer, eventBus, serializeReadBuffer);
    // ...
}
```

4. Propagate to `RecordDeltaFrame` the same way:

```csharp
public void RecordDeltaFrame(EntityRepository repo, uint prevTick, BinaryWriter writer,
    long wallClockTicks, FdpEventBus? eventBus = null, bool serializeReadBuffer = false)
{
    // ...existing code...
    WriteEvents(writer, eventBus, serializeReadBuffer); // was: WriteEvents(writer, eventBus);
    // ...
}
```

**Note:** Both `RecordKeyframe` and `RecordDeltaFrame` currently call `WriteEvents(writer, eventBus)`.
Change those calls to `WriteEvents(writer, eventBus, serializeReadBuffer)`.  No other call sites exist.

**Default `false` preserves all existing behavior.** All existing tests must still pass.

#### Tests for S502

No new test file required — validate via the S504 integration test (see below).
However, add a brief unit test to verify that `serializeReadBuffer = false` still reads
Pending bytes and `serializeReadBuffer = true` reads Current bytes.  Place in
`FDP/Engine/Fdp.Core.Tests/RecorderSystemReadBufferTests.cs`.

- **S502-T1** `WriteEvents_DefaultFlag_UsesPendingBuffer` — publish event, do NOT swap,
  call `RecordKeyframe(repo, bw, ticks, bus)` (no flag), deserialize bytes, assert event
  count > 0.
- **S502-T2** `WriteEvents_SerializeReadBuffer_UsesCurrentBuffer` — publish event, call
  `SwapBuffers()`, call `RecordKeyframe(repo, bw, ticks, bus, serializeReadBuffer: true)`,
  deserialize bytes, assert event count > 0.

---

### TASK-S503: Wire `EventAccumulator` into `ReferenceCheckpointHandler`

**Task Definition:** `.dev/cgf-scn-2/TASK-DETAIL.md#task-s503`

This task has multiple callers to update.  Complete in this order:
**A) HrotNodeContext** → **B) HrotNodeBuilder** → **C) ReferenceCheckpointHandler** →
**D) NodeBootstrapper** → **E) SimHostApp** → **F) Tests**

#### A. `Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeContext.cs`

Add the `EventAccumulator` property to the record:

```csharp
/// <summary>Event accumulator for checkpoint event preservation (CGF-SCN-2 S503).</summary>
public required EventAccumulator EventAccumulator { get; init; }
```

Add `using Fdp.Core;` if not already present.

#### B. `Hrot/Engine/Hrot.Common/Infrastructure/HrotNodeBuilder.cs`

In `Build()`, the local `eventAccumulator` variable already exists (Step 2).
Add it to the returned `HrotNodeContext`:

```csharp
return new HrotNodeContext
{
    World              = world,
    Kernel             = kernel,
    EventAccumulator   = eventAccumulator,   // <-- add this line
    // ...rest unchanged
};
```

#### C. `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceCheckpointHandler.cs`

1. Add `private readonly EventAccumulator _eventAccumulator;` field.
2. Change the constructor to require `EventAccumulator eventAccumulator`:

```csharp
public ReferenceCheckpointHandler(
    CheckpointIOWorker        worker,
    EntityRepository?         liveRepo,
    EventAccumulator          eventAccumulator)
{
    _worker           = worker           ?? throw new ArgumentNullException(nameof(worker));
    _liveRepo         = liveRepo;
    _eventAccumulator = eventAccumulator ?? throw new ArgumentNullException(nameof(eventAccumulator));
}
```

3. In `Commit()`, after `snap.SyncFrom(source)` and before `_worker.Enqueue(...)`, add:

```csharp
_eventAccumulator.FlushToReplica(snap.Bus, source.GlobalVersion - 1);
```

The full `Commit` method should read:

```csharp
public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
{
    if (intent.Operation != NodeOpType.TakeSnapshot) return;
    _pendingPrepares.Remove(intent.TransactionId);

    var source = repo ?? _liveRepo;
    if (source == null)
    {
        FdpLog<ReferenceCheckpointHandler>.Error(
            "[ReferenceCheckpointHandler] Commit: no EntityRepository available — " +
            "snapshot for request {0} cannot be taken.", intent.TransactionId);
        return;
    }

    var snap = new EntityRepository();
    snap.SyncFrom(source);
    _eventAccumulator.FlushToReplica(snap.Bus, source.GlobalVersion - 1);
    _worker.Enqueue(snap, intent.TransactionId);

    FdpLog<ReferenceCheckpointHandler>.Info(
        "[ReferenceCheckpointHandler] Commit: snapshot enqueued to I/O worker for request {0}.",
        intent.TransactionId);
}
```

#### D. `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

Add `EventAccumulator? eventAccumulator = null` to `BuildOrchestration`'s parameter list
(add it after `ghostCreationSystem`, keeping all existing params optional):

```csharp
public ClusterSlave BuildOrchestration(
    NodeRole role,
    Fdp.ModuleHost.ModuleHostKernel kernel,
    Fdp.Core.EntityRepository world,
    int nodeId,
    CycloneDDS.Runtime.DdsParticipant? participant = null,
    string subsystemName = "SimHost",
    Fdp.Core.FdpEventBus? eventBus = null,
    Fdp.Toolkit.Scenario.ScenarioSerializer? scenarioSerializer = null,
    string localTempRoot = @"C:\FDP_Temp",
    CheckpointIOWorker? checkpointWorker = null,
    Fdp.Core.SimulationSystemGroup? simGroup = null,
    Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup? lifecycleGroup = null,
    Fdp.Toolkit.Replication.Systems.GhostCreationSystem? ghostCreationSystem = null,
    Fdp.Core.EventAccumulator? eventAccumulator = null)
```

Update the `ReferenceCheckpointHandler` construction:

```csharp
if (checkpointWorker != null)
    clusterSlave.RegisterHandler(new ReferenceCheckpointHandler(
        checkpointWorker, world, eventAccumulator ?? new Fdp.Core.EventAccumulator()));
```

Add `using Fdp.Core;` at the top of the file if not already present (check first).

#### E. `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

Find the `bootstrapper.BuildOrchestration(...)` call and add `eventAccumulator: _context.EventAccumulator`:

```csharp
_clusterSlave = bootstrapper.BuildOrchestration(
    _role, _kernel, _world, localNodeId,
    participant: ddsParticipant,
    subsystemName: "SimHost",
    eventBus: _eventBus,
    scenarioSerializer: scenarioSerializer,
    localTempRoot: nodeConfig.LocalTempRoot,
    checkpointWorker: _checkpointWorker,
    simGroup: simulationSystemGroup,
    lifecycleGroup: networkLifecycleGroup,
    ghostCreationSystem: ghostCreationSystem,
    eventAccumulator: _context.EventAccumulator);   // <-- add this
```

#### F. `Hrot/Subsystems/Hrot.SimHost.Tests/CheckpointClusterOpHandlerTests.cs`

Both construction sites need `new EventAccumulator()` added as the third argument:

- Line ~56: `new ReferenceCheckpointHandler(worker, _liveRepo)` →
  `new ReferenceCheckpointHandler(worker, _liveRepo, new EventAccumulator())`
- Line ~152: `new ReferenceCheckpointHandler(worker, liveRepo: null)` →
  `new ReferenceCheckpointHandler(worker, liveRepo: null, new EventAccumulator())`
- The `CreateHandler` helper (if present) similarly needs updating.

Add `using Fdp.Core;` if not already present.

#### Tests for S503

New test file: `Hrot/Subsystems/Hrot.SimHost.Tests/ReferenceCheckpointEventTests.cs`

- **S503-T1** `Commit_WithPublishedEvent_EventPresentInSnapshotBus` — create a live repo;
  publish a managed event on its bus (e.g. a simple struct or existing event type); capture
  frame via `accumulator.CaptureFrame(liveRepo.Bus, 1)`; create handler; call `Commit`;
  drain worker (use temp dir); assert the snapshot's bus (captured before `Enqueue` hands
  ownership to worker) has `CurrentEvents.Count > 0` for that event type.
  
  **Implementation tip:** To observe the snap's bus before it is enqueued to the worker, you
  can mock/replace `_worker.Enqueue` or — simpler — subclass `CheckpointIOWorker` to capture
  the snapshot.  Alternatively, call `FlushToReplica` directly on a fresh repo bus in a
  simpler test that bypasses the handler.

- **S503-T2** `Commit_NoEvents_CompletesWithoutError` — live repo with no events published;
  call `Commit`; drain; assert checkpoint file exists.

- **S503-T3** `Commit_NullRepo_NoCheckpointWritten` — handler constructed with `liveRepo: null`;
  call `Commit(intent, repo: null)`; drain; assert no file written (existing behavior preserved).

---

### TASK-S504: Patch `CheckpointIOWorker` to pass Event Bus

**Task Definition:** `.dev/cgf-scn-2/TASK-DETAIL.md#task-s504`

**File to modify:** `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs`

In `WriteCheckpointFile`, change the single `RecordKeyframe` call:

```csharp
// Before:
_recorderSystem.RecordKeyframe(snapshot, bw, DateTimeOffset.UtcNow.Ticks);

// After:
_recorderSystem.RecordKeyframe(snapshot, bw, DateTimeOffset.UtcNow.Ticks,
    snapshot.Bus, serializeReadBuffer: true);
```

No other changes in this file.

#### Tests for S504

Extend `FDP/Engine/Fdp.Core.Tests/CheckpointIOWorkerTests.cs` with:

- **S504-T1** `WriteCheckpointFile_WithEventOnBus_EventPresentAfterPlayback` —
  1. Create `EntityRepository snapshot`.
  2. Use `FdpEventBus.InjectIntoCurrentBySize` (or `InjectEvents`) to put a native event
     into `snapshot.Bus`'s Current buffer.
  3. Enqueue snapshot to worker; drain worker.
  4. Load the `.fdp` file via `PlaybackSystem` (or `RecorderSystem` reader if available);
     OR manually: decompress the LZ4 blob, create a `BinaryReader`, use
     `FdpAutoSerializer` / recorder reader to verify event section is non-empty.
  5. Assert event count > 0.
  
  **Simpler approach if PlaybackSystem is complex:** Parse the raw bytes manually:
  skip the FDPC magic header (12 bytes), decompress, then use `BinaryReader` — after
  the GlobalVersion (8 bytes), frame type (1 byte), wallClockTicks (8 bytes),
  destroyCount (4 bytes = 0), you find the event block: read `unmanagedStreamCount`
  (int); assert it is >= 1 when an event was injected.

---

## Testing Summary

| Test ID | File | Description |
|---------|------|-------------|
| S501-T1..T4 | `Fdp.Core.Tests/FdpEventBusCurrentStreamsTests.cs` | PopulateCurrentStreams / PopulateCurrentManagedStreams |
| S502-T1..T2 | `Fdp.Core.Tests/RecorderSystemReadBufferTests.cs` | WriteEvents flag |
| S503-T1..T3 | `Hrot.SimHost.Tests/ReferenceCheckpointEventTests.cs` | EventAccumulator wiring |
| S504-T1    | `Fdp.Core.Tests/CheckpointIOWorkerTests.cs` (extend) | Event round-trip |

Minimum: **10 new passing tests** across the four test files.

---

## Commit Guidance

All S501–S504 changes touch `FDP/` (submodule) and `Hrot/` (top level):

1. Commit the FDP submodule first (from `d:\Work\IOS-IG-SimHost-FDP-2\FDP`):
   ```
   git add -A
   git commit -m "feat(events): add PopulateCurrentStreams, serializeReadBuffer flag, wire EventAccumulator into ReferenceCheckpointHandler"
   ```

2. Commit top-level (from `d:\Work\IOS-IG-SimHost-FDP-2`):
   ```
   git add FDP Hrot
   git commit -m "feat(cgf-scn-2): Phase 5 — checkpoint event preservation (S501-S504)"
   ```

---

## Definition of Done

- [ ] `IManagedEventStreamInfo` has `CurrentEvents` property; `ManagedEventStream<T>` implements it
- [ ] `FdpEventBus` has `PopulateCurrentStreams` and `PopulateCurrentManagedStreams`
- [ ] `RecorderSystem.WriteEvents` / `RecordKeyframe` / `RecordDeltaFrame` accept `serializeReadBuffer`
- [ ] `ReferenceCheckpointHandler` takes `EventAccumulator`; calls `FlushToReplica` in `Commit`
- [ ] `HrotNodeContext` exposes `EventAccumulator`; `HrotNodeBuilder` sets it
- [ ] `NodeBootstrapper.BuildOrchestration` accepts and forwards `eventAccumulator`
- [ ] `SimHostApp` passes `_context.EventAccumulator`
- [ ] Test sites in `CheckpointClusterOpHandlerTests.cs` updated
- [ ] `CheckpointIOWorker.WriteCheckpointFile` passes `snapshot.Bus, serializeReadBuffer: true`
- [ ] All 10+ new tests pass
- [ ] Solution builds without errors
- [ ] Pre-existing 7 failures in `Fdp.Toolkits.Tests` still the only failures
