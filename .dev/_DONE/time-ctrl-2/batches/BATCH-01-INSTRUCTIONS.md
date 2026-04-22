# BATCH-01: Phase 1 (Fix Core Lockstep) + Phase 2 (Smooth SimTime UI)

**Batch Number:** BATCH-01  
**Tasks:** TC2-P1-T1, TC2-P1-T2, TC2-P2-T1, TC2-P2-T2, TC2-P2-T3 (stretch)  
**Phase:** Phase 1 + Phase 2  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch covers **two independent feature areas**:

1. **Phase 1** — Fix a real lockstep bug: `MasterSyncController.SwitchToDeterministic` silently discards the slave roster passed at call time and uses the empty construction-time set instead. Lockstep therefore never actually waits for any slave ACKs. Fix it.
2. **Phase 2** — Eliminate the 1 Hz visual stutter in the `ClusterScenarioPanel` time display by injecting a local `ITimeController` into `ClusterUiCache`.

### Required Reading (IN ORDER)

1. **Design Document:** [`.dev/time-ctrl-2/DESIGN.md`](../DESIGN.md) — read sections §3 (Feature A) and §4 (Feature B) in full
2. **Task Details:** [`.dev/time-ctrl-2/TASK-DETAIL.md`](../TASK-DETAIL.md) — read TC2-P1-T1, TC2-P1-T2, TC2-P2-T1, TC2-P2-T2, TC2-P2-T3
3. **Developer Workflow:** [`.github/skills/developer/SKILL.md`](../../../.github/skills/developer/SKILL.md)

### Source Code Locations

| Area | Path |
|------|------|
| MasterSyncController (to fix) | `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` |
| MasterSyncController tests | `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs` |
| OrchestratorSubsystem | `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` |
| ClusterUiCache (to modify) | `Hrot.ClusterRunner/Services/ClusterUiCache.cs` |
| ClusterUiCache tests | `Hrot.ClusterRunner.Tests/ClusterUiCacheTests.cs` |
| ITimeController interface | `FDP/ModuleHost/ModuleHost.Core/Time/ITimeController.cs` |

### Report Submission

**Submit your report to:**  
`.dev/time-ctrl-2/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/time-ctrl-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

`MasterSyncController` is the time authority in Orchestrator. It synchronises all cluster nodes during lockstep (Deterministic) mode. The current code has a silent bug: the slave roster passed at call time to `SwitchToDeterministic` is discarded, so `_pendingAcks` is always empty — lockstep gates on nothing.

`ClusterUiCache` is a CQRS read-model that the UI panels read for time state. It currently only receives data from 1 Hz DDS messages, causing visible stutter in the Time Control panel on the master (Orchestrator) node.

---

## 🎯 Batch Objectives

1. ✅ `MasterSyncController.SwitchToDeterministic` correctly replaces `_expectedSlaves` with the runtime set.
2. ✅ Stale DT-003 comment removed from `OrchestratorSubsystem`.
3. ✅ `ClusterUiCache` accepts optional `ITimeController?` injection and reads sim-time from it when available.
4. ✅ `OrchestratorSubsystem.Initialize` wires `_masterSync` into the `_uiCache`.
5. *(Stretch)* ✅ SimHost and IG subsystems also wire time controllers into their caches.

---

## ✅ Tasks

### Task 1: Fix MasterSyncController.SwitchToDeterministic (TC2-P1-T1)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P1-T1](../TASK-DETAIL.md#tc2-p1-t1--fix-mastersynccollrollerswitchtodeterministic)

**What to change:**

At the very start of `SwitchToDeterministic(HashSet<int> slaveNodeIds)`, before the barrier calculation:

```csharp
_expectedSlaves.Clear();
if (slaveNodeIds != null)
    _expectedSlaves.UnionWith(slaveNodeIds);
```

Also update the doc comment on the `slaveNodeIds` parameter — replace the "Accepted for API compatibility…" text with:
> "The roster of slave node IDs that must ACK every step during lockstep. Replaces any prior slave set."

**Tests to add in `MasterSyncControllerTests.cs`:**

- `MasterSyncController_RuntimeSlaveSet_BlocksUntilRuntimeAcks`  
  Create controller with empty slaves. Call `SwitchToDeterministic({1,2})`. Verify `Step()` does NOT advance frame number on the second call until ACKs arrive from 1 and 2.

- `MasterSyncController_RuntimeSlaveSet_StepAdvancesAfterAcks`  
  Continue from above: publish `FrameStepCompletedEvent` for nodes 1 and 2, call `Update()`. Verify next `Step()` advances `FrameNumber`.

- `MasterSyncController_RuntimeSlaveSet_SecondCallReplacesFirstSet`  
  Call `SwitchToDeterministic` twice (second time with `{3}`). Verify only ACK from node 3 unblocks — nodes 1 and 2 are no longer expected.

Full success conditions are in [TASK-DETAIL.md § TC2-P1-T1-SC1/SC2/SC3](../TASK-DETAIL.md#tc2-p1-t1--fix-mastersynccollrollerswitchtodeterministic).

---

### Task 2: Remove DT-003 comment (TC2-P1-T2)

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P1-T2](../TASK-DETAIL.md#tc2-p1-t2--update-orchestratorsubsystem-construction-and-comment)

Remove the `// Note (DT-003): SwitchToDeterministic ignores slaveNodeIds…` comment block. The call itself (`_masterSync.SwitchToDeterministic(slaveIds)`) must stay unchanged.

No code logic change needed. No new tests for this cleanup task — the existing integration test `PauseResume_SimTimeFreezes_ThenAdvances` (in `Hrot.ClusterRunner.Integration.Tests`) must continue to pass.

---

### Task 3: Add ITimeController injection to ClusterUiCache (TC2-P2-T1)

**File:** `Hrot.ClusterRunner/Services/ClusterUiCache.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P2-T1](../TASK-DETAIL.md#tc2-p2-t1--add-itimecontroller-injection-to-clusteruicache)

Changes:
1. Add `private readonly ITimeController? _localTimeController;` field.
2. Add `private double _networkSimTime;` backing field.
3. Update constructor: `public ClusterUiCache(DdsParticipant participant, ITimeController? localTimeController = null)`.
4. Change `MasterSimTime` to a computed property reading from `_localTimeController` first, `_networkSimTime` as fallback.
5. In `DrainTimePulse()`, assign to `_networkSimTime` instead of `MasterSimTime`.

**Required namespace:** `ModuleHost.Core.Time` (for `ITimeController`).

**Tests to add in `ClusterUiCacheTests.cs`:**

- `ClusterUiCache_MasterSimTime_ReadsFromLocalController_WhenInjected`
- `ClusterUiCache_MasterSimTime_FallsBackToNetwork_WhenNoController`
- `ClusterUiCache_MasterSimTime_IgnoresNetworkPulse_WhenControllerInjected`

Use a `private sealed class FakeTimeController : ITimeController` mock in the test file (see Appendix in TASK-DETAIL.md for the exact stub).

Existing test `ClusterUiCache_UpdatesTimeScaleFromTimePulse` (or similar) must NOT regress.

---

### Task 4: Wire MasterSyncController into OrchestratorSubsystem's UI cache (TC2-P2-T2)

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P2-T2](../TASK-DETAIL.md#tc2-p2-t2--wire-mastersynccollroller-into-orchestratorsubsystems-ui-cache)

In `Initialize()`, ensure `_masterSync` is created **before** `_uiCache`. Then pass it:

```csharp
_uiCache = new ClusterUiCache(_participant, _masterSync);
```

`MasterSyncController` implements `ITimeController`, so this compiles directly.

No regression in integration test `PauseResume_SimTimeFreezes_ThenAdvances`.

---

### Task 5 (Stretch): Wire slave controllers into SimHost and IG UI caches (TC2-P2-T3)

**Files:** `Hrot.SimHost/Services/SimHostSubsystem.cs`, `Hrot.IG/Services/IgSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P2-T3](../TASK-DETAIL.md#tc2-p2-t3--wire-slave-controllers-into-simhost-and-ig-ui-caches-stretch)

This is a **stretch goal**. Only implement if time allows and the `ModuleHostKernel` is accessible at the `_uiCache` construction point. If implemented, all SimHost/IG subsystem tests must pass.

---

## 🧪 Test-Driven Task Progression

> ⚠️ **MANDATORY WORKFLOW — follow exactly:**

1. **Write the test first** (or alongside the production change).
2. **Confirm the test FAILS** before the fix (Red).
3. **Implement the production change.**
4. **Confirm the test PASSES** (Green).
5. **Run the full test suite for the affected project** and confirm no regressions:
   - `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj`
   - `dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj`
6. **Move to next task only after Green + no regressions.**

Tests must check **correctness of values and behavior**, not just string matching or compilation.

---

## 📊 Report Requirements

Submit `.dev/time-ctrl-2/reports/BATCH-01-REPORT.md` covering:

### A. Task Completion Summary

| Task ID | Status | Notes |
|---------|--------|-------|
| TC2-P1-T1 | ✅/⚠️/❌ | ... |
| TC2-P1-T2 | ✅/⚠️/❌ | ... |
| TC2-P2-T1 | ✅/⚠️/❌ | ... |
| TC2-P2-T2 | ✅/⚠️/❌ | ... |
| TC2-P2-T3 | ✅/⚠️/❌ (stretch) | ... |

### B. Test Results

Paste the final `dotnet test` output for each test project.

### C. Developer Insights

Answer these questions:
1. **What issues were encountered?** (compilation errors, test failures, unexpected behaviour)
2. **What weak points were spotted in the codebase?** (things that surprised you or look fragile)
3. **What design decisions were made beyond the spec?** (any choices you made that TASK-DETAIL didn't specify)
4. **Is `MasterSyncController` a valid `ITimeController`?** (Does it implement the interface? Can it be passed directly to `ClusterUiCache`? Note any adapter needed.)
5. **Stretch goal outcome:** Did you implement TC2-P2-T3? Why or why not?

### D. Scope / Deviation Notes

Any deviations from the task specification with justification.
