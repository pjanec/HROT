# BATCH-04 Report: Phase 5 — Autonomous Multi-Computer Unit Tests

**Date:** 2026-04-02  
**Batch:** BATCH-04  
**Tasks:** TC3-P5-T01, TC3-P5-T02, TC3-P5-T03, TC3-P5-T04, TC3-P5-T05  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)

---

## Summary

| Metric | Value |
|--------|-------|
| Tests before batch | 118 |
| Tests added | 18 |
| Tests after batch | **136** |
| Tests passing | **136** |
| Tests failing | 0 |
| Production files changed | 0 |

---

## Files Created

| # | File | Tests | Task |
|---|------|-------|------|
| 1 | `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncOffsetTests.cs` | 6 | TC3-P5-T01 |
| 2 | `FDP/Toolkits/FDP.Toolkit.Time.Tests/PauseBarrierSyncTests.cs` | 4 | TC3-P5-T02 |
| 3 | `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepSimTimeAccuracyTests.cs` | 4 | TC3-P5-T03 |
| 4 | `FDP/Toolkits/FDP.Toolkit.Time.Tests/FullCycleMultiComputerSim.cs` | 2 | TC3-P5-T04 |
| 5 | `FDP/Toolkits/FDP.Toolkit.Time.Tests/ClockSkewDriftTests.cs` | 2 | TC3-P5-T05 |

---

## Test Results Snippet

```
Passed!  - Failed: 0, Passed: 136, Skipped: 0, Total: 136, Duration: 849 ms
```

---

## Deviations from Instructions

### 1. `SlaveSyncController` constructor — `nodeId:` named parameter

**Instruction said:** `new SlaveSyncController(bus, nodeId: 1, ...)`  
**Actual parameter name:** `localNodeId`  
**Fix applied:** Changed all occurrences to positional arguments or `localNodeId:`.

### 2. `master.Update()` does not initiate pause

**Instruction said (in several places):**
```csharp
masterBus.SwapBuffers(); master.Update(); // "SwitchToDeterministic; emits SwitchTimeModeEvent"
masterBus.SwapBuffers();
```
**Actual:** `master.Update()` only dispatches to `UpdateContinuous`/`UpdateBarrierPending`/`UpdateStepping` — it never calls `SwitchToDeterministic` internally.  
**Fix applied:** Replaced with the correct sequence in all affected tests (PauseBarrierSyncTests, LockstepSimTimeAccuracyTests, FullCycleMultiComputerSim):
```csharp
master.SwitchToDeterministic(new HashSet<int> { ... });
masterBus.SwapBuffers();  // SwitchTimeModeEvent → masterBus.current
// consume & relay to slave bus, slaveBus.SwapBuffers()
// advance ticks, master.Update(), slave.Update()
```

### 3. Step relay sequence — missing `SwapBuffers` calls

**Instruction said:**
```csharp
master.Step(delta);
var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>(); // no swap first
```
**Actual:** Managed events also use double-buffering. `SwapBuffers()` is required before `ConsumeManaged<T>()` returns non-empty data.  
**Fix applied:** Added `SwapBuffers()` calls at each stage of the relay chain:
```csharp
master.Step(delta);
masterBus.SwapBuffers();   // intent → masterBus.current
var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
foreach (var i in intents) slaveBus.PublishManaged(i);
slaveBus.SwapBuffers();    // intent → slaveBus.current
slave.Update();
slaveBus.SwapBuffers();    // ACK → slaveBus.current
var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
foreach (var a in acks) masterBus.PublishManaged(a);
masterBus.SwapBuffers();   // ACK → masterBus.current
master.Update();
```

### 4. `CreateMasterSlave` helper — ref parameters in lambda

**Instruction provided** a `CreateMasterSlave(ref long masterTicks, ref long slaveTicks, ...)` helper that captured the ref params in lambdas (`() => masterTicks`). C# does not allow capturing ref parameters in closures.  
**Fix applied:** Removed the `CreateMasterSlave` helper entirely. All tests create `MasterSyncController` and `SlaveSyncController` inline using local variables directly captured by lambdas.

### 5. `Assert.Equal(T, T, string)` overload — does not exist in xunit v2

**Instruction said:**
```csharp
Assert.Equal(TimeMode.Continuous, slave.GetMode(), "message");
```
**Actual:** xunit v2 `Assert.Equal<T>` does not have a string-message overload (it has `IEqualityComparer<T>` as 3rd arg).  
**Fix applied:** Changed to `Assert.True(slave.GetMode() == TimeMode.Continuous, "message")`.

### 6. `NtpHandshake` in `PauseBarrierSyncTests` — implicit `ClientNodeId = SlaveNodeId`

The instructions' `NtpHandshake` helper used `ClientNodeId = SlaveNodeId` (const = 1) unconditionally. Tests 3 and 4 use slave2 with `nodeId = 2`; the response would have been silently discarded by `DrainTimeSyncResponses` (`response.ClientNodeId != _localNodeId`).  
**Fix applied:** Added `int nodeId = SlaveNodeId` optional parameter to the `NtpHandshake` helper. Tests 3 and 4 pass `nodeId: 2` for slave2.

---

## Issues Encountered

None beyond those listed as deviations. All issues were identified through careful reading of the production source (`MasterSyncController.cs`, `SlaveSyncController.cs`, `FdpEventBus.cs`) and the existing reference test `UnifiedControllerE2ETests.cs` before writing any code.

---

## Verification

Build: **0 errors, 4 pre-existing warnings** (unrelated to new files).  
Test run: **136/136 passed** in 849 ms.
