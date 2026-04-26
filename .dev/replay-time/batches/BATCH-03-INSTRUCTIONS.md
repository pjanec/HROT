# BATCH-03: Seek Time Payload Propagation

**Batch Number:** BATCH-03  
**Tasks:** RT-010, RT-011, RT-012, RT-013, RT-014, RT-015  
**Phase:** Phase 4 (Seek Time Payload Propagation)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (complete), BATCH-02 (complete)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Design Document:** `.dev/replay-time/DESIGN.md` — Phase 4 section
3. **Task Definitions:** `.dev/replay-time/TASK-DETAIL.md` — RT-010 through RT-015
4. **Previous Reports:** `.dev/replay-time/reports/BATCH-01-REPORT.md`, `BATCH-02-REPORT.md`

### Source Code Locations
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` — RT-010
- `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` — RT-011
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` — RT-012
- `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs` — RT-012
- `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs` — RT-012
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` — RT-013
- `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` — RT-014
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` — RT-015

### Build & Test Commands
```powershell
# Build (check for errors):
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# Run Hrot.Orchestrator tests:
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj -v normal

# Run Fdp.Toolkits tests (for RT-014):
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj -v normal
```

**Pre-existing test failures to ignore (not introduced by this batch):**
- `Fdp.Toolkit.Combat.Tests` (5) — unmanaged struct size assertions
- `Fdp.Toolkit.Geographic.Tests.SimTransformBridgeSystemTests` (5) — rotation math
- `Fdp.Toolkit.Physics.Tests.PhysicsQueryActionNodeTests` (1)
- `Fdp.Toolkit.Replication.Tests.IdAllocationTests` (2)
- `ClusterMasterFanOutTests.PayloadJson_PopulatedFromClusterOpRequest` (1) — pre-existing

### Report Submission
`.dev/replay-time/reports/BATCH-03-REPORT.md`

---

## Context

Phase 4 wires the `GlobalTime` from a completed seek all the way back to the orchestrator master clock. Currently, when nodes finish a `NodeReplaySeek`, the ACK contains no time data. Phase 4:
1. Defines `ReplaySeekResult` — the ACK payload type carrying `RestoredTime`.
2. Changes `SeekToTimeAsync` return type to `Task<GlobalTime>` so implementations can return the landed time.
3. Updates three implementations: ECS (real time from repo), Listener/CGF (default).
4. Updates `ReferenceReplayLoadHandler` to return `new ReplaySeekResult(restoredTime)`.
5. Adds `SnapAndPause` to `MasterSyncController` — atomically snaps clock and switches to Deterministic.
6. Hooks `SnapAndPause` into `ClusterMaster.ConsumeNodeOpStatuses` so the master clock is synchronized after every seek.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

Complete tasks strictly in order. After each: build → run relevant tests → confirm passing → move on.

1. **RT-010:** Add `ReplaySeekResult` struct → build passes ✅
2. **RT-011:** Change `SeekToTimeAsync` return type → confirm compile errors in implementations ✅
3. **RT-012:** Fix 3 implementations → build passes ✅
4. **RT-013:** `ReferenceReplayLoadHandler` returns `ReplaySeekResult` → build passes ✅
5. **RT-014:** Add `SnapAndPause` to `MasterSyncController` → unit tests pass ✅
6. **RT-015:** Wire `SnapAndPause` into `ClusterMaster` → integration tests pass ✅

---

## Tasks

### RT-010: Define `ReplaySeekResult` Payload Struct

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-010-define-replayseekresult-payload-struct)

Add the following record struct immediately after `ReplaySeekPayload`:

```csharp
public readonly record struct ReplaySeekResult(GlobalTime RestoredTime);
```

`GlobalTime` is already imported (it's used by the existing code). No new `using` is needed if `GlobalTime` is already in scope — check existing imports in the file to confirm.

**Verify:** `dotnet build` passes. No tests needed for this minimal struct (RT-011 compile-error test is the implicit proof).

---

### RT-011: Change `IRecordReplayController.SeekToTimeAsync` Return Type

**File:** `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-011-change-irecordreplaycontrollerseektotimeasync-return-type)

Change:
```csharp
Task SeekToTimeAsync(long targetWallClockTicks);
```
To:
```csharp
Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks);
```

Update the XML doc to note that the task result is the `GlobalTime` the recording landed on after seek. Listener/CGF implementations should return `default(GlobalTime)`.

**Verify:** After this change, `dotnet build` will produce `error CS` lines for the three implementations that still return `Task` or `Task.CompletedTask`. This is expected — proceed to RT-012.

---

### RT-012: Update All `SeekToTimeAsync` Implementations

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-012-update-all-seektotimeasync-implementations)

#### File 1: `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`

Current:
```csharp
public Task SeekToTimeAsync(long targetWallClockTicks) =>
    _activeReplayModule?.SeekToWallClockTicksAsync(targetWallClockTicks)
        ?? Task.CompletedTask;
```

Replace with:
```csharp
public async Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
{
    if (_activeReplayModule == null)
    {
        FdpLog<EcsRecordReplayController>.Warn(
            "[EcsRecordReplayController] SeekToTimeAsync called with no active replay module.");
        return default;
    }
    await _activeReplayModule.SeekToWallClockTicksAsync(targetWallClockTicks)
        .ConfigureAwait(false);
    return _timeController.GetCurrentState();
}
```

**Important:** `_timeController` is an `ITimeController` that was injected in BATCH-01 (RT-002/RT-004). After the seek completes (background Task.Run finishes), the `PlaybackTickSystem` will have advanced the time controller to the seek target. Use `_timeController.GetCurrentState()` to read the landed time.

If `_timeController` is not directly available in the class, use `_kernel.GetTimeController().GetCurrentState()` — whichever was actually added in BATCH-01 (check the current file).

#### File 2: `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs`

Current:
```csharp
public Task SeekToTimeAsync(long targetWallClockTicks)
{
    // body
}
```

Change return type to `Task<GlobalTime>` and return `Task.FromResult(default(GlobalTime))`.

#### File 3: `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs`

Same as Listener: change to `Task<GlobalTime>` and return `Task.FromResult(default(GlobalTime))`.

**Verify:** After these three changes, `dotnet build` passes with zero `error CS` lines.

---

### RT-013: `ReferenceReplayLoadHandler` Returns `ReplaySeekResult`

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-013-referencereplayloadhandler-returns-replayseekresult-for-nodereplayseek)

In `PrepareAsync`, the `NodeReplaySeek` branch currently:
```csharp
await _controller.SeekToTimeAsync(targetTicks).ConfigureAwait(false);

FdpLog<ReferenceReplayLoadHandler>.Info(
    "[ReferenceReplayLoadHandler] NodeReplaySeek complete (targetTicks={0}).",
    targetTicks);
```

Change to:
```csharp
GlobalTime restoredTime = await _controller.SeekToTimeAsync(targetTicks)
    .ConfigureAwait(false);

FdpLog<ReferenceReplayLoadHandler>.Info(
    "[ReferenceReplayLoadHandler] NodeReplaySeek complete (targetTicks={0}, restoredWallTicks={1}).",
    targetTicks,
    restoredTime.TotalWallTicks);

return new ReplaySeekResult(restoredTime);
```

Note: the method `PrepareAsync` already has `return null` at the end for the `PrepareLive` branch; the `NodeReplaySeek` branch needs to `return` before falling through. Check the existing control flow carefully. The method returns `Task<object?>` so returning `ReplaySeekResult` (a value type) is fine (implicit boxing).

**Verify:** `dotnet build` passes.

---

### RT-014: Add `SnapAndPause` Method to `MasterSyncController`

**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-014-add-snapandpause-method-to-masterysynccontroller)

Add the following public method to `MasterSyncController` after `SwitchToDeterministic`:

```csharp
/// <summary>
/// Atomically snaps the master clock to <paramref name="targetWallTicks"/> /
/// <paramref name="targetSimTime"/> and enters Deterministic (lockstep) mode.
/// Unlike <see cref="SwitchToDeterministic"/>, no future-barrier window is used —
/// the mode switch is instantaneous and the published <see cref="SwitchTimeModeEvent"/>
/// carries a <c>BarrierWallTicks</c> already in the past so slaves apply the snap
/// immediately via the instant-snap path in <c>SlaveSyncController</c>.
/// </summary>
/// <param name="targetWallTicks">Wall-clock tick value to snap to.</param>
/// <param name="targetSimTime">Simulation time (seconds) to snap to.</param>
/// <param name="slaveNodeIds">Slave roster for ACK tracking during subsequent steps.</param>
public void SnapAndPause(long targetWallTicks, double targetSimTime, HashSet<int> slaveNodeIds)
{
    _totalWallTicks    = targetWallTicks;
    _totalTime         = (float)targetSimTime;
    _mode              = MasterMode.Stepping;
    _pendingAcks       = new HashSet<int>();

    _expectedSlaves.Clear();
    if (slaveNodeIds != null)
        _expectedSlaves.UnionWith(slaveNodeIds);

    _eventBus.Publish(new SwitchTimeModeEvent
    {
        TargetMode       = TimeMode.Deterministic,
        BarrierWallTicks = _getTick(),
        SimTimeSnapshot  = _totalTime,
        TimeScale        = _timeScale,
        FixedDelta       = _config.FixedDeltaSeconds,
    });

    _lastTickSample = _getTick();
}
```

**Notes:**
- `_totalTime` is `float` in `MasterSyncController`. The parameter `targetSimTime` is `double` (matching `GlobalTime.TotalTime`). Cast with `(float)targetSimTime`.
- `_pendingBarrierWallTicks` does NOT need to be reset because `_mode = Stepping` bypasses `UpdateBarrierPending` entirely.
- The `BarrierWallTicks = _getTick()` is intentionally in the past (current real tick); by the time slaves receive this event, `SyncedWallTicks >= BarrierWallTicks`, triggering the instant-snap path added in RT-006.

**New tests in `Fdp.Toolkits.Tests` (look for existing `MasterSyncController` test class first):**
- `SnapAndPause_SetsWallTicksAndSimTime` (T14a):
  1. Create `MasterSyncController` with a manual tick source.
  2. Call `SnapAndPause(12345L, 99.5, new HashSet<int>())`.
  3. Assert `GetCurrentState().TotalWallTicks == 12345L`.
- `SnapAndPause_SwitchesToDeterministicMode` (T14b):
  1. Same setup.
  2. Call `SnapAndPause`.
  3. Assert `GetMode() == TimeMode.Deterministic`.
- `SnapAndPause_PublishesOneSwitchTimeModeEvent_WithTargetSimTime` (T14c):
  1. Call `SnapAndPause(t, s, slaves)`.
  2. Swap bus buffers.
  3. Read `SwitchTimeModeEvent` from bus.
  4. Assert exactly one event, `TargetMode == Deterministic`, `SimTimeSnapshot == (float)s`.
- `SnapAndPause_UpdateKeepsControllerInStepping` (T14d):
  1. Call `SnapAndPause`.
  2. Call `Update()`.
  3. Assert `GetMode() == TimeMode.Deterministic`.

Look for existing `MasterSyncControllerTests.cs` in `FDP/Toolkits/Fdp.Toolkits.Tests/`. Add the new tests to the existing test class (do not create a new file unless it doesn't exist).

---

### RT-015: Master Clock Snap in `ConsumeNodeOpStatuses` After Seek

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (UPDATE — 2 changes)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-015-master-clock-snap-in-consumernodeopstatuses-after-seek)

#### Change 1: Extend `BusTransitionAckTracker` with `SeekResult` field

In `ClusterMaster`, the nested class `BusTransitionAckTracker` currently has:
```csharp
private sealed class BusTransitionAckTracker
{
    public Guid RequestId;
    public int  Expected;
    public int  Received;
    public bool HasFailure;
    public OrchestrationStatusCode  FailureCode;
}
```

Add:
```csharp
public ReplaySeekResult? SeekResult;
```

#### Change 2: Add `_masterSync` field and setter to `ClusterMaster`

After the existing `private ReplayMasterModule? _replayMasterModule;` field declaration, add:
```csharp
private MasterSyncController? _masterSync;
```

After the existing `SetReplayMasterModule` method, add:
```csharp
/// <summary>
/// Wires the <see cref="MasterSyncController"/> used by <see cref="ConsumeNodeOpStatuses"/>
/// to snap the master clock after a seek completes.
/// </summary>
public void SetMasterSync(MasterSyncController sync)
{
    _masterSync = sync ?? throw new ArgumentNullException(nameof(sync));
}
```

You need to add the appropriate `using` for `MasterSyncController` namespace — check the existing `using` statements and add the one for `Fdp.Toolkit.Time.Controllers` or the correct namespace where `MasterSyncController` lives.

#### Change 3: Update ACK accumulation loop in `ConsumeNodeOpStatuses`

In `ConsumeNodeOpStatuses`, the transition ACK block currently contains:
```csharp
if (ev.StatusCode.IsError())
{
    tracker.HasFailure  = true;
    tracker.FailureCode = ev.StatusCode;
}
tracker.Received++;
```

Add seek result capture after the error check:
```csharp
if (ev.StatusCode.IsError())
{
    tracker.HasFailure  = true;
    tracker.FailureCode = ev.StatusCode;
}
if (tracker.SeekResult == null &&
    ev.ResultPayload is ReplaySeekResult sr &&
    sr.RestoredTime.TotalWallTicks != 0)
{
    tracker.SeekResult = sr;
}
tracker.Received++;
```

#### Change 4: Call `SnapAndPause` when finalizing a seek

In the same block, when `tracker.Received >= tracker.Expected`, add the `SnapAndPause` call BEFORE `PublishOpStatus`:
```csharp
if (tracker.Received >= tracker.Expected)
{
    _pendingBusTransitionAcks.Remove(ev.TransactionId);
    if (tracker.SeekResult.HasValue)
    {
        var sr2 = tracker.SeekResult.Value;
        _masterSync?.SnapAndPause(
            sr2.RestoredTime.TotalWallTicks,
            sr2.RestoredTime.TotalTime,
            new HashSet<int>(_roster.ActiveNodes.Keys));
    }
    var aggregated = tracker.HasFailure ? null : TryAggregate(ev.TransactionId);
    PublishOpStatus(tracker.RequestId,
        tracker.HasFailure ? tracker.FailureCode : OrchestrationStatusCode.Success,
        aggregated);

    // Broadcast the new cluster state across the bus so UI panels update
    PublishClusterState(_currentDsmState);
}
```

**New tests in `Hrot.Orchestrator.Tests`** (add to `ClusterMasterSeekTests.cs`):
- `ReplaySeek_OnAllNodesAck_WithSeekResult_CallsSnapAndPause` (T15a/T15b):
  1. Create `MasterSyncController` with fake tick source.
  2. Create `ClusterMaster`, call `SetMasterSync(masterSync)`.
  3. Bootstrap one SimHost node (same pattern as `ClusterMasterSeekTests` from BATCH-02).
  4. Send a `ReplaySeek` request; tick; get the fanned-out `txId`.
  5. Publish `NodeOpCompletedEvent { TransactionId = txId, NodeId = node1, StatusCode = Success, ResultPayload = new ReplaySeekResult(new GlobalTime { TotalWallTicks = 9999L, TotalTime = 5.0f }) }`.
  6. Tick; swap buffers.
  7. Assert `masterSync.GetCurrentState().TotalWallTicks == 9999L`.
  8. Assert `masterSync.GetMode() == TimeMode.Deterministic`.
- `ReplaySeek_OnAllNodesAck_WithDefaultResult_DoesNotCallSnapAndPause` (T15d):
  1. Same as above but ACK `ResultPayload = new ReplaySeekResult(default(GlobalTime))`.
  2. Verify `GetCurrentState().TotalWallTicks != 9999L` (master time unchanged).
- `ReplaySeek_NonSeekTransition_DoesNotCallSnapAndPause` (T15c):
  1. Create a regular `TransitionState` operation (not ReplaySeek).
  2. Verify master time controller is unchanged after completion.

**How to capture `txId` for the test:**
After calling `Tick`, read the bus for `ExecuteNodeOpIntent` events. Each fan-out operation publishes an `ExecuteNodeOpIntent` with the `TransactionId`. Capture it from there.

---

## Key Technical Clarification: `_timeController` in `EcsRecordReplayController`

In BATCH-01 (RT-002/RT-004), `EcsRecordReplayController.PrepareReplayAsync` was updated to pass `_kernel.GetTimeController()` to the `ReplayModule` constructor. The TASK-DETAIL for RT-012 says:

> After the task completes, read `_repo.GetSingletonUnmanaged<GlobalTime>()` and return it.

However, `_repo.GetSingletonUnmanaged<GlobalTime>()` may not be available or accessible in `EcsRecordReplayController`. Check the actual class for what's available:
- If `_timeController` (or `_kernel.GetTimeController()`) is available: use `_timeController.GetCurrentState()`
- If `_repo.GetSingletonUnmanaged<GlobalTime>()` compiles: use that instead

Check the current state of `EcsRecordReplayController.cs` before choosing the approach. Either is correct — the ECS repo singleton and the time controller's current state should both reflect the post-seek clock position.

---

## Quality Standards

- Match existing code style in all files.
- `MasterSyncController` uses `float _totalTime` — cast the `double targetSimTime` parameter with `(float)`.
- Do not change unrelated code.
- All new tests must use real objects (no mocking frameworks) as per existing test patterns.

---

## Success Criteria

This batch is DONE when:
- [ ] RT-010: `ReplaySeekResult(GlobalTime RestoredTime)` struct added to `NodeOpPayloads.cs`
- [ ] RT-011: `IRecordReplayController.SeekToTimeAsync` returns `Task<GlobalTime>`
- [ ] RT-012: All 3 implementations updated; `EcsRecordReplayController` returns actual time; Listener/CGF return `default`
- [ ] RT-013: `ReferenceReplayLoadHandler.NodeReplaySeek` branch returns `new ReplaySeekResult(restoredTime)`
- [ ] RT-014: `MasterSyncController.SnapAndPause` added; T14a-T14d tests pass
- [ ] RT-015: `BusTransitionAckTracker.SeekResult`, `ClusterMaster._masterSync`, `SetMasterSync`, seek result capture, and `SnapAndPause` call all implemented; T15a-T15d tests pass
- [ ] All existing tests that were passing before this batch still pass
- [ ] Solution builds with zero `error CS` lines

---

## Developer Insights (Required in Report)

**Q1:** For RT-012: what method did you use to get the landed `GlobalTime` in `EcsRecordReplayController.SeekToTimeAsync`? Why?

**Q2:** For RT-014: why does `SnapAndPause` set `BarrierWallTicks = _getTick()` (current real time) rather than `targetWallTicks`? What would break if you used `targetWallTicks`?

**Q3:** Any edge cases encountered with `ResultPayload` casting in `ConsumeNodeOpStatuses`? The payload comes in as `object?` — how does boxing/unboxing behave for `ReplaySeekResult` (a value type)?

**Q4:** Suggested commit message.
