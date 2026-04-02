# BATCH-02: Phase 3 — ExCon Lockstep Participation

**Batch Number:** BATCH-02  
**Tasks:** TC2-P3-T1, TC2-P3-T2, TC2-P3-T3, TC2-P3-T4  
**Phase:** Phase 3  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (ClusterUiCache now accepts ITimeController injection — required by TC2-P3-T3)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch gives `ExConSubsystem` its own `SlaveSyncController` so it participates in cluster lockstep (sends ACKs), and its UI time display reads from the local controller rather than from 1 Hz DDS messages.

**Before you start:** Read the BATCH-01 review (`.dev/time-ctrl-2/reviews/BATCH-01-REVIEW.md`) — BATCH-01 already wired `ITimeController?` injection into `ClusterUiCache`, which you will wire in TC2-P3-T3.

### Required Reading (IN ORDER)

1. **Design Document:** [`.dev/time-ctrl-2/DESIGN.md`](../DESIGN.md) — read section §5 (Feature C) in full
2. **Task Details:** [`.dev/time-ctrl-2/TASK-DETAIL.md`](../TASK-DETAIL.md) — TC2-P3-T1 through TC2-P3-T4
3. **BATCH-01 Review:** [`.dev/time-ctrl-2/reviews/BATCH-01-REVIEW.md`](../reviews/BATCH-01-REVIEW.md)
4. **Developer Workflow:** [`.github/skills/developer/SKILL.md`](../../../.github/skills/developer/SKILL.md)

### Source Code Locations

| Area | Path |
|------|------|
| ExConSubsystem (primary) | `Hrot.ClusterRunner/Services/ExConSubsystem.cs` |
| ExCon tests | `Hrot.ClusterRunner.Tests/ExConSubsystemTests.cs` |
| ClusterUiCache (modified in BATCH-01) | `Hrot.ClusterRunner/Services/ClusterUiCache.cs` |
| SlaveSyncController | `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` |
| TimeNetworkModule | `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` |
| ITimeController interface | `FDP/ModuleHost/ModuleHost.Core/Time/ITimeController.cs` |

### Report Submission

**Submit your report to:**  
`.dev/time-ctrl-2/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev/time-ctrl-2/questions/BATCH-02-QUESTIONS.md`

---

## Context

`ExConSubsystem` is the instructor station node in the cluster. Currently it has no time controller of its own — it only receives 1 Hz DDS `TimePulseDescriptor` messages and feeds them to `ExConLogic` via `TimePulseIngressHandler` / `TimeModeIngressHandler`.

As a result:
1. ExCon never sends `FrameStepCompletedEvent` ACKs, so the master (Orchestrator) cannot include it in the lockstep slave roster.
2. ExCon's UI time display stutters at 1 Hz.

The fix is to add a `SlaveSyncController` to `ExConSubsystem` alongside the necessary DDS translators, drive the time pipeline in `Update()`, and (optionally) remove the now-redundant ingress handlers.

**Key point on iosNodeId**: `ExConSubsystem` already derives a node-ID from its DDS participant or config. The `SlaveSyncController` needs this ID so its `FrameStepCompletedEvent` ACKs are correctly attributed. Check the existing code for how `iosNodeId` is obtained.

---

## 🎯 Batch Objectives

1. ✅ `ExConSubsystem` hosts a `SlaveSyncController` initialized with the correct node ID and translators.
2. ✅ The time pipeline is driven in `ExConSubsystem.Update()`.
3. ✅ `ExConSubsystem._uiCache` is injected with `_slaveSyncController` for smooth sim-time display.
4. ✅ Redundant `TimePulseIngressHandler` / `TimeModeIngressHandler` removed (if safe) or retained with clear comment if still needed for ExConLogic.
5. *(Stretch from BATCH-01 TD-001)* Wire slave controllers into SimHost/IG caches if accessible.

---

## ✅ Tasks

### Task 1: Add SlaveSyncController + translators to ExConSubsystem (TC2-P3-T1)

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P3-T1](../TASK-DETAIL.md#tc2-p3-t1--add-slavesynccontroller-and-translators-to-exconsubsystem)

Add these private fields:
```csharp
private FdpEventBus?           _timeEventBus;
private SlaveSyncController?   _slaveSyncController;
private IDescriptorTranslator? _timeModeTranslator;
private IDescriptorTranslator? _slaveLockstepTranslator;
private IDescriptorTranslator? _timePulseIngressTranslator;
```

In `Initialize()`, after derivation of `iosNodeId`:
```csharp
_timeEventBus             = new FdpEventBus();
_slaveSyncController      = new SlaveSyncController(_timeEventBus, iosNodeId, TimeConfig.Default);
_timeModeTranslator       = TimeNetworkModule.CreateDescriptorTranslator(_participant, _timeEventBus);
_slaveLockstepTranslator  = TimeNetworkModule.CreateSlaveLockstepTranslator(_participant, _timeEventBus, iosNodeId);
_timePulseIngressTranslator = TimeNetworkModule.CreateTimePulseIngressTranslator(_participant, _timeEventBus);
```

**Required namespaces** (add if missing):
- `FDP.Toolkit.Time.Controllers` (for `SlaveSyncController`, `TimeConfig`)
- `FDP.Toolkit.Time` (for `TimeNetworkModule`)
- `Fdp.Kernel` (for `FdpEventBus`)
- `Fdp.Interfaces` (for `IDescriptorTranslator`)

**Tests (in `ExConSubsystemTests.cs`):**
- Existing `Initialize_DoesNotThrow` must still pass.
- New test `ExCon_Initialize_CreatesSlaveTimeController`: after `Initialize()`, assert a `TestHook_SlaveSyncController` property (internal) is not null and is a `SlaveSyncController`.

Add `internal SlaveSyncController? TestHook_SlaveSyncController => _slaveSyncController;` to `ExConSubsystem`.

---

### Task 2: Drive time pipeline in ExConSubsystem.Update (TC2-P3-T2)

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P3-T2](../TASK-DETAIL.md#tc2-p3-t2--drive-time-pipeline-in-exconsubsystemupdate)

In `Update(float deltaTime)`, **before** `_clusterSlave?.Tick()`, add:
```csharp
// Time sync pipeline: ingest DDS → advance controller → egress ACKs → swap bus.
_timeModeTranslator?.PollIngress(null!, null!);
_slaveLockstepTranslator?.PollIngress(null!, null!);
_timePulseIngressTranslator?.PollIngress(null!, null!);
_slaveSyncController?.Update();
_slaveLockstepTranslator?.ScanAndPublish(null!);
_timeEventBus?.SwapBuffers();
```

**Tests:**
- Existing `Update_MultipleFrames_Succeeds` must still pass (if it exists).
- New test `ExCon_Update_DoesNotThrow_WithTimePipeline`: call `Initialize(HeadlessConfig())`, then `Update(0.016f)` × 30 — assert no exception.

---

### Task 3: Wire SlaveSyncController into ExCon's UI cache (TC2-P3-T3)

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P3-T3](../TASK-DETAIL.md#tc2-p3-t3--wire-slavesynccontroller-into-excons-ui-cache)

Ensure `_slaveSyncController` is created **before** `_uiCache` in `Initialize()`. Then:
```csharp
_uiCache = new ClusterUiCache(_participant, _slaveSyncController);
```

**Tests:**
- New test `ExCon_UiCache_MasterSimTime_AdvancesWithController`: after `Initialize(HeadlessConfig())` + 100 × `Update(0.016f)`, assert `TestHook_SlaveSyncController.GetCurrentState().TotalTime > 0`.  
  *(The slave PLL runs continuously in Continuous mode, so TotalTime should advance over 100 frames.)*

---

### Task 4: Remove redundant time ingress handlers (TC2-P3-T4)

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P3-T4](../TASK-DETAIL.md#tc2-p3-t4--remove-redundant-time-ingress-handlers-from-exconsubsystem)

Inspect `ExConLogic.OnTimePulse` and `ExConLogic.OnTimeMode`. If these callbacks are **purely for UI/display** (no game-logic side effects), remove:
- `_timePulseHandler` field and instantiation.
- `_timeModeHandler` field and instantiation.
- Their `_ingressDisposables.Add(...)` calls.

If they are still needed for non-display logic (e.g. pausing internal ExCon logic, updating simulation state), **keep them** but add a comment:
```csharp
// Retained: OnTimePulse/OnTimeMode are used for ExConLogic game-state purposes,
// not just display. Time display is handled by SlaveSyncController → ClusterUiCache.
```

**Success conditions:**
- All existing `ExConSubsystem` unit tests pass.
- `FullLifecycle_Headless_CompletesCleanly` integration test passes (if it exists).
- No compile errors.

---

### Task 5 (Stretch — TD-001): Wire slave controllers into SimHost / IG UI caches

**Files:** `Hrot.SimHost/Services/SimHostSubsystem.cs`, `Hrot.IG/Services/IgSubsystem.cs`  
**Task Detail:** See [TASK-DETAIL.md § TC2-P2-T3](../TASK-DETAIL.md#tc2-p2-t3--wire-slave-controllers-into-simhost-and-ig-ui-caches-stretch)

Only attempt if the `ModuleHostKernel` (or equivalent) exposes a `GetTimeController()` method at the `_uiCache` construction point. If implemented, all SimHost/IG tests must pass. Document your findings either way in the report.

---

## 🧪 Test-Driven Task Progression

> ⚠️ **MANDATORY WORKFLOW — follow exactly:**

1. **Write the test first** (or alongside the production change).
2. **Confirm the test FAILS** before the implementation (Red).
3. **Implement the production change.**
4. **Confirm the test PASSES** (Green).
5. **Run the full test suite for the affected project** and confirm no regressions:
   - `dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj`
6. **Move to the next task only after Green + no regressions.**

Tests must check **correctness of values and behavior**, not just string matching or compilation.

---

## 📊 Report Requirements

Submit `.dev/time-ctrl-2/reports/BATCH-02-REPORT.md` covering:

### A. Task Completion Summary

| Task ID | Status | Notes |
|---------|--------|-------|
| TC2-P3-T1 | ✅/⚠️/❌ | ... |
| TC2-P3-T2 | ✅/⚠️/❌ | ... |
| TC2-P3-T3 | ✅/⚠️/❌ | ... |
| TC2-P3-T4 | ✅/⚠️/❌ | ... |
| TD-001 (stretch) | ✅/⚠️/❌ | ... |

### B. Test Results

Paste the final `dotnet test` output for `Hrot.ClusterRunner.Tests`.

### C. Developer Insights

Answer these questions:
1. **What issues were encountered?** (compilation errors, unexpected API shapes, namespace mismatches)
2. **How was `iosNodeId` obtained?** (describe the exact source: participant ID, config value, or other)
3. **What `TimeNetworkModule.CreateTimePulseIngressTranslator` signature was found?** If not present, how did you handle it?
4. **Were `TimePulseIngressHandler`/`TimeModeIngressHandler` retained or removed?** Justify the decision.
5. **Stretch goal (TD-001) outcome:** Did you implement SimHost/IG wiring? Why or why not?
6. **What weak points did you spot in the codebase?** (fragile patterns, potential future issues)
7. **What design decisions were made beyond the spec?**

### D. Scope / Deviation Notes

Any deviations from the task specification with justification.
