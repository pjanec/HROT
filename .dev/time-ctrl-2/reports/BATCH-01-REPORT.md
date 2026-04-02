# BATCH-01 Completion Report

**Batch:** BATCH-01  
**Tasks:** TC2-P1-T1, TC2-P1-T2, TC2-P2-T1, TC2-P2-T2, TC2-P2-T3 (stretch)  
**Date:** 2026-04-02

---

## A. Task Completion Summary

| Task ID    | Status | Notes |
|------------|--------|-------|
| TC2-P1-T1  | ✅     | Fixed `SwitchToDeterministic`; updated 2 regressing existing tests; added 3 new tests. All 73 FDP.Toolkit.Time.Tests pass. |
| TC2-P1-T2  | ✅     | Removed `// Note (DT-003):` comment block from `OrchestratorSubsystem.Update()`. |
| TC2-P2-T1  | ✅     | Added `ITimeController?` injection to `ClusterUiCache`; computed `MasterSimTime`; added 3 new tests. |
| TC2-P2-T2  | ✅     | Reordered `Initialize()` so `_masterSync` is created before `_uiCache`; passed `_masterSync` to `ClusterUiCache`. |
| TC2-P2-T3  | ⚠️ (not attempted) | Stretch goal deferred — `ModuleHostKernel` access at `_uiCache` construction point was not verified within scope. |

---

## B. Test Results

### FDP.Toolkit.Time.Tests

```
Passed!  - Failed:     0, Passed:    73, Skipped:     0, Total:    73, Duration: 853 ms
```

All 73 tests pass including 3 new TC2-P1-T1 tests:
- `MasterSyncController_RuntimeSlaveSet_BlocksUntilRuntimeAcks`
- `MasterSyncController_RuntimeSlaveSet_StepAdvancesAfterAcks`
- `MasterSyncController_RuntimeSlaveSet_SecondCallReplacesFirstSet`

### Hrot.ClusterRunner.Tests

```
Passed!  - Failed:     0, Passed:   191, Skipped:     0, Total:   191, Duration: 18 s
```

All 191 tests pass including 3 new TC2-P2-T1 tests:
- `ClusterUiCache_MasterSimTime_ReadsFromLocalController_WhenInjected`
- `ClusterUiCache_MasterSimTime_FallsBackToNetwork_WhenNoController`
- `ClusterUiCache_MasterSimTime_IgnoresNetworkPulse_WhenControllerInjected`

---

## C. Developer Insights

### 1. What was the root cause of the bug?

`MasterSyncController` was designed with two separate sets: `_expectedSlaves` (fixed at construction) and `_pendingAcks` (re-armed after each `Step()`). `SwitchToDeterministic(slaveNodeIds)` accepted the runtime roster as a parameter but never applied it — `_pendingAcks` was always re-armed from the construction-time `_expectedSlaves`, which was always empty in `OrchestratorSubsystem`. The fix adds two lines at the start of `SwitchToDeterministic` to clear `_expectedSlaves` and populate it from the call-time argument.

### 2. Were there any existing tests that relied on the buggy behavior?

Yes — two tests were written against the buggy behavior:

- **`MasterSyncController_Step_BlocksUntilAllAcksReceived`**: Created the controller with `{1, 2}` at construction, but passed `new HashSet<int>()` to `SwitchToDeterministic`. This relied on the bug to get blocking behavior. Fixed by passing `{1, 2}` to `SwitchToDeterministic` instead.

- **`MasterSyncController_AckFromUnknownNode_IsIgnored`**: Same pattern — created with `{1}`, called `SwitchToDeterministic(new HashSet<int>())`. Fixed by passing `{1}` to `SwitchToDeterministic`.

All other existing tests passed an empty set to both the constructor and `SwitchToDeterministic`, so they were unaffected.

### 3. How does MasterSimTime prioritization work in ClusterUiCache?

`MasterSimTime` is now a computed property:
```csharp
public double MasterSimTime =>
    _localTimeController != null
        ? _localTimeController.GetCurrentState().TotalTime
        : _networkSimTime;
```

The network-sourced value (`_networkSimTime`) is still maintained in `DrainTimePulse()`, it just isn't exposed via `MasterSimTime` when a controller is present. This means the network fallback path works correctly when no controller is injected (e.g. in ExCon/SimHost/IG nodes that don't yet have TC2-P2-T3 implemented).

### 4. Why was the OrchestratorSubsystem.Initialize() order significant?

`ClusterUiCache` now takes an optional `ITimeController?` in its constructor. To pass `_masterSync`, it must be instantiated first. The original order was: `_uiCache` → (event subscription) → `_masterSync`. The reordered sequence is: `_masterSync` → `_uiCache(_participant, _masterSync)` → event subscription. The S0503 event handler still captures `_masterSync` via a closure, which is safe since the subscription lambda executes on the update thread (after `Initialize` returns).

### 5. Manual acceptance check (TC2-P2-T2-SC2)

After the wiring change, the `MasterSimTime` property on the Orchestrator's `ClusterUiCache` is now sourced from `MasterSyncController.GetCurrentState().TotalTime`, which is updated every frame in `Update()`. The expected visual result on the Time Control panel is that the sim-time counter advances smoothly at the render frame rate (60 Hz) rather than in 1-second steps. This is not covered by automated tests (no render output) but is the primary user-visible benefit.

### 6. TC2-P2-T3 Stretch Goal — Decision to Defer

SimHost and IG subsystems use a `ModuleHostKernel` for time control. Whether `_kernel.GetTimeController()` is accessible at the `_uiCache` construction point requires examination of `SimHostSubsystem.cs` and `IgSubsystem.cs`, and potentially changes to kernel initialization order. This was deferred to avoid scope creep and unverified changes outside the specified batch boundary.
