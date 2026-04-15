# BATCH-03 REPORT — hexag-2 Phase 3: Sever TimeControlRequested C# Event

**Status:** COMPLETE  
**Tasks covered:** HEXAG2-DEBT-005, HEXAG2-S010, HEXAG2-S011  

---

## Summary

BATCH-03 eliminates the `ClusterMaster.TimeControlRequested` C# event and
replaces the direct field toggle of `_isPaused` with bus-mediated typed intents,
completing the hexagonal architecture boundary for time control.

---

## Changes Made

### HEXAG2-DEBT-005 — Fix hard-coded path in ExConSubsystemClusterTests

**File:** `Hrot/Runner/Hrot.ClusterRunner.Tests/ExConSubsystemClusterTests.cs`

- Replaced 4-level relative `".."` path with a `FindWorkspaceRoot()` helper that
  walks up the directory tree until `IOS-IG-SimHost.sln` is found.
- New path: `Path.Combine(FindWorkspaceRoot(), "Hrot", "Subsystems", "Hrot.ExCon", "ExConSubsystem.cs")`

### HEXAG2-S010 — Sever _unhandledRequestCallback from ClusterOpMasterTranslator

**File:** `Hrot/Network/Hrot.Network.Orchestration/ClusterOpMasterTranslator.cs`

S010 was implemented in BATCH-03 prep (building on the moved translator from
BATCH-02). The translator now publishes typed intents directly to the bus for all
4 time-control ops (`PauseTime`, `ResumeTime`, `StepTime`, `SetTimeScale`)
without any callback. `TryParseFloat` helper added for StepTime/SetTimeScale
plain-float payload format.

**File:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`

- Removed `unhandledRequestCallback: _clusterMaster.HandleClusterOpRequest`
  argument from `ClusterOpMasterTranslator` constructor call.

### HEXAG2-S011 — Eliminate ClusterMaster.TimeControlRequested C# event

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

- Added `using Fdp.Toolkit.Time.Domain;`
- Deleted `public event Action<ClusterOpType, string>? TimeControlRequested;`
- Replaced `TimeControlRequested?.Invoke(...)` in `ProcessSingleClusterOpRequest`
  with a `switch` that publishes `SlaveNodeSetUpdatedEvent`, `PauseTimeIntent`,
  `ResumeTimeIntent`, `StepTimeIntent`, `SetTimeScaleIntent` to the bus.
- Added private helpers `ParseStepDelta(string?, float)` and
  `ParseTimeScale(string?, float)` for payload parsing.

**File:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`

- Removed `private bool _isPaused;` field.
- Removed `internal bool IsPausedForTest => _isPaused;` property.
- Removed the entire `_clusterMaster.TimeControlRequested += (op, payload) => { ... }` 
  subscription block (was 30+ lines).

**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs`

- Added intent drain loops at the START of `Update()`:
  - `SlaveNodeSetUpdatedEvent` — updates the slave roster for the next pause
  - `PauseTimeIntent` — calls `SwitchToDeterministic(freshSlaves)`
  - `ResumeTimeIntent` — calls `SwitchToContinuous()`
  - `StepTimeIntent` — calls `Step(ev.DeltaSeconds)`
  - `SetTimeScaleIntent` — calls `SetTimeScale(ev.TimeScale)`

**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Domain/TimeLocalEvents.cs`

- Added `SlaveNodeSetUpdatedEvent { IReadOnlySet<int> SlaveNodeIds }` struct.

### Test Updates

**`ClusterMasterTimeControlTests.cs`**
- Deleted `TimeControlRequested_FiresOnPauseTime` (event no longer exists).
- Updated class doc comment.
- `TimeControlRequested_BypassesTransactionHistory` retained (no event hookup needed).

**`OrchestratorSubsystemTests.cs`**
- Replaced 3 occurrences of `IsPausedForTest` with `UiCacheForTest!.IsPaused`.
- `TimeControlRequested_PauseTime_SetsIsPaused`: calls `Update()` 3 times to
  accommodate the 3-frame bus pipeline latency (Tick→SwapBuffers→MasterSync→
  SwapBuffers→UiCache).

**`TimeControlIntegrationTests.cs`** and **`SimTimeSyncIntegrationTests.cs`**
- Replaced all `IsPausedForTest` references with `UiCacheForTest!.IsPaused`.
- `PumpUntil` loops naturally handle the 3-frame latency.

### New Tests Added

**`FDP/Toolkits/Fdp.Toolkits.Tests/Time/MasterSyncControllerTests.cs`** — 2 new tests:
- `MasterSyncController_DrainsPauseTimeIntent_SwitchesToDeterministic`
- `MasterSyncController_DrainsResumeTimeIntent_SwitchesToContinuous`

**`Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterOpMasterTranslatorTests.cs`** — 4 new tests:
- `ClusterOpMasterTranslator_PauseTime_PublishesIntentToBus`
- `ClusterOpMasterTranslator_ResumeTime_PublishesIntentToBus`
- `ClusterOpMasterTranslator_StepTime_PublishesIntentWithDelta`
- `ClusterOpMasterTranslator_SetTimeScale_PublishesIntentWithScale`

---

## Build and Test Results

- **Build:** `dotnet build IOS-IG-SimHost.sln -v q` → 0 errors, pre-existing warnings only.
- **Hrot.Orchestrator.Tests:** 94/94 passed.
- **Hrot.ClusterRunner.Tests:** 214/214 passed.
- **Fdp.Toolkits.Tests:** 730/731 passed (1 pre-existing failure in `PhysicsQueryActionNode` unrelated to this batch).

---

## Notes

- The 3-frame bus pipeline latency for `IsPaused` via `HandleClusterOpRequest`:
  Frame 1: ClusterMaster.Tick() writes `PauseTimeIntent` to WRITE buffer.
  Frame 2: SwapBuffers promotes to READ; MasterSyncController.Update() drains it
           and writes `SwitchTimeModeEvent{Deterministic}` to WRITE buffer.
  Frame 3: SwapBuffers promotes to READ; ClusterUiCache.Update() drains it,
           sets `IsPaused = true`.
- `ParseStepDelta` in `ClusterMaster` uses JSON `{"FixedDelta":X}` format (direct
  injection path), while `TryParseFloat` in `ClusterOpMasterTranslator` uses
  plain float string (DDS NED message path). These are by design.
