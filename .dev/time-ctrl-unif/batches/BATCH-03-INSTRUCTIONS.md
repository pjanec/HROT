# BATCH-03: Unified Slave Controller — SlaveSyncController

**Batch Number:** BATCH-03  
**Tasks:** TCU-SC001, TCU-T002  
**Phase:** Phase 3 — Unified Slave Controller  
**Estimated Effort:** 6–8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (domain message types), BATCH-02 (MasterSyncController as reference impl)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch creates `SlaveSyncController` — the unified replacement for `SlaveTimeController` + `SteppedSlaveController` + `SlaveTimeModeListener`. The key design goal is **PLL continuity**: the JitterFilter and virtual wall ticks must NEVER be reset across mode transitions. This controller is purely a consumer of time signals from the Orchestrator — it must NEVER publish `TimePulseDescriptor`.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.3 Unified Slave Controller (read every word), §4.6 TimePulse Source
2. **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — read TCU-SC001 and TCU-T002 in full
3. **Previous Reviews:** `.dev/time-ctrl-unif/reviews/BATCH-01-REVIEW.md`, `.dev/time-ctrl-unif/reviews/BATCH-02-REVIEW.md`
4. **Existing code to study (DO NOT MODIFY):**
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeController.cs` — existing continuous impl with PLL
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedSlaveController.cs` — existing stepping impl
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs` — existing mode-switch listener
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/ITimeController.cs` — interface to implement (NOT ISteppableTimeController)
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/JitterFilter.cs` — the PLL filter (understand its API)
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` — reference for constructor patterns
   - `FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs` — domain types from BATCH-01
   - `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` — wire DTOs from BATCH-01

### Source Code Location

- **New controller:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` (NEW FILE)
- **New tests:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` (NEW FILE)
- **FDP solution:** `FDP/FDP.sln`
- **Time tests csproj:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj`

### Report Submission

**When done, submit your report to:**  
`.dev/time-ctrl-unif/reports/BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev/time-ctrl-unif/questions/BATCH-03-QUESTIONS.md`

---

## Context

`SlaveSyncController` implements the same three-mode state machine as `MasterSyncController` but:
1. It never governs time — it follows the master's authoritative clock.
2. It uses a PLL (`JitterFilter`) to slew its virtual wall clock toward the master's `TimePulseDescriptor`.
3. In Stepping mode it waits for `AdvanceFrameIntent` and replies with `FrameStepCompletedEvent`.
4. It NEVER publishes `TimePulseDescriptor` — this is the key constraint that eliminates the `continuousControllerFactory` workaround in SimHost.

**Related Tasks:**
- [TCU-SC001](../docs/TASK-DETAIL.md#tcu-sc001--slavesynccontroller) — Implementation spec
- [TCU-T002](../docs/TASK-DETAIL.md#tcu-t002--unit-tests-slavesynccontroller) — Test coverage spec

---

## 🎯 Batch Objectives

1. `SlaveSyncController.cs` exists, compiles, and implements `ITimeController`.
2. All ten success conditions from TCU-SC001 have corresponding passing tests.
3. Additional edge-case tests per TCU-T002.
4. All 99+ pre-existing tests continue passing.

---

## ✅ Tasks

### Task 1: Implement SlaveSyncController (TCU-SC001)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` (NEW FILE)  
**Task Definition:** See [TCU-SC001 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-sc001--slavesynccontroller) — the full internal state, behaviour rules, and all constraints are specified there. Read that section in its entirety.

**Key design rules (summary — full details in TASK-DETAIL.md):**

**State machine (same structure as master):**
```
Continuous ──SwitchTimeModeEvent(Deterministic)──► BarrierPending ──barrier crossed──► Stepping
Stepping   ──SwitchTimeModeEvent(Continuous)─────────────────────────────────────────► Continuous
```

**Critical behaviour rules:**
- Constructor: register `TimePulseDescriptor`, `SwitchTimeModeEvent`, `AdvanceFrameIntent` on bus (use `Register<T>()` for types with `[EventId]`, `PublishManaged`/`ConsumeManaged` for domain types)
- Every `Update()` starts by draining `SwitchTimeModeEvent` — check for mode transitions first
- In Continuous/BarrierPending: compute raw delta from tick source; apply PLL slew from latest `TimePulseDescriptor`; accumulate time. In BarrierPending, also check if `_virtualWallTicks >= _pendingBarrierWallTicks` → transition to Stepping
- In Stepping: drain `AdvanceFrameIntent` queue; for each intent advance `_totalTime` (snap to `TargetSimTime` if non-zero, else `+= FixedDelta * _timeScale`); increment `_frameNumber`; publish `FrameStepCompletedEvent { FrameID, NodeID=_localNodeId }`; advance `_virtualWallTicks += (long)(FixedDelta * Stopwatch.Frequency)`. If no intents: return current state with `DeltaTime=0`
- On `SwitchTimeModeEvent(Continuous)`: apply `SimTimeSnapshot` if `> 0` (snap `_totalTime`); apply `TimeScale` if carried; set mode to Continuous. **PLL state unchanged — warm restart.**
- On `SwitchTimeModeEvent(Deterministic)`: store `_pendingBarrierWallTicks`; set mode to BarrierPending; clear `_pendingIntents`
- **ABSOLUTE CONSTRAINT: NEVER publish `TimePulseDescriptor`** — not in any mode, not under any condition

**PLL behaviour (study `SlaveTimeController` carefully):**
- `JitterFilter` and `_virtualWallTicks` must survive all mode transitions unchanged
- PLL slew: update `_virtualWallTicks` toward master's wall ticks using `JitterFilter`
- The PLL only matters in Continuous/BarrierPending modes; in Stepping it is "frozen" but not reset

**Constructor parameters:**
```csharp
public SlaveSyncController(
    FdpEventBus  eventBus,
    int          localNodeId,
    TimeConfig?  config     = null,
    Func<long>?  tickSource = null)
```

**`GetMode()` mapping:** Same as master — `Continuous` and `BarrierPending` → `TimeMode.Continuous`; `Stepping` → `TimeMode.Deterministic`

---

### Task 2: Unit Tests for SlaveSyncController (TCU-T002)

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` (NEW FILE)  
**Task Definition:** See [TCU-T002 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-t002--unit-tests-slavesynccontroller) — 10 required success conditions plus additional edge cases.

**Required tests (from TCU-SC001 success conditions — all 10 must be present):**

1. `SlaveSyncController_ContinuousMode_PLLTracksTimePulse` — publish `TimePulseDescriptor { MasterWallTicks = N }`; advance local ticks 100ms; call `Update()`; assert `TotalTime > 0`.
2. `SlaveSyncController_NoTimePulseEmitted` — run 200 frames; swap bus each frame; collect all events; assert **zero** `TimePulseDescriptor` events published at any point.
3. `SlaveSyncController_BarrierPending_PLLContinuesDuringWait` — send `SwitchTimeModeEvent(Deterministic, BarrierWallTicks=veryFar)`; advance several frames; assert `GetMode() == Continuous` (barrier not crossed) and `TotalTime` still advancing.
4. `SlaveSyncController_TransitionsToStepping_WhenBarrierCrossed` — set `BarrierWallTicks` near current virtual wall ticks; call `Update()`; assert `GetMode() == Deterministic`.
5. `SlaveSyncController_Stepping_AdvancesOnAdvanceFrameIntent` — transition to Stepping; publish `AdvanceFrameIntent { FrameID=1, FixedDelta=0.016f }`; swap; call `Update()`; assert `FrameNumber==1`, `DeltaTime≈0.016f`, `TotalTime≈0.016f`.
6. `SlaveSyncController_Stepping_WaitsWithDeltaZeroWhenNoIntent` — in Stepping, call `Update()` without any intent; assert `DeltaTime==0` and `FrameNumber` unchanged.
7. `SlaveSyncController_Stepping_PublishesFrameStepCompletedEvent` — advance one intent; swap; drain `FrameStepCompletedEvent`; assert `FrameID==1` and `NodeID==_localNodeId`.
8. `SlaveSyncController_Resume_SnapsToMasterSimTime` — in Stepping with `TotalTime=3.0`; send `SwitchTimeModeEvent(Continuous, SimTimeSnapshot=4.5)`; call `Update()`; assert `GetCurrentState().TotalTime ≈ 4.5`.
9. `SlaveSyncController_Resume_PLLIsWarm_NoJitterReset` — run 50 Continuous frames to warm PLL; transition Stepping; transition back to Continuous; assert PLL error is **still near zero** (not cold-started) by checking that first Continuous `Update()` post-resume has `DeltaTime` within ±5% of pre-pause delta (i.e. no sharp slew jump).
10. `SlaveSyncController_Stepping_SnapsToTargetSimTime_WhenProvided` — publish `AdvanceFrameIntent { FrameID=5, FixedDelta=0.016f, TargetSimTime=10.0 }`; advance; assert `TotalTime ≈ 10.0`.

**Additional edge-case tests (from TCU-T002):**
- `SlaveSyncController_TwoConsecutivePauseResumeCycles_WithoutPLLReset` — two full Continuous → Stepping → Continuous cycles; no PLL reset between them; verify time advances correctly after second resume.
- `SlaveSyncController_OutOfOrderAdvanceFrameIntent_IsIgnored` — in Stepping with `FrameNumber = 5`; publish `AdvanceFrameIntent { FrameID=3 }` (less than current); call `Update()`; assert `FrameNumber` unchanged at 5 (stale intent ignored via log warning).

**Quality bar:**
- Use tick-source seam to avoid `Thread.Sleep`
- The "PLL is warm" test (test 9) is the most complex — take care to warm the PLL properly by providing realistic tick increments and at least one `TimePulseDescriptor` before pausing

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests before moving on:**

1. **Task 1 (TCU-SC001):** Implement `SlaveSyncController.cs` → `dotnet build FDP/FDP.sln` — zero errors ✅  
2. **Task 2 (TCU-T002):** Write all tests → `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all pass ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Build succeeds with zero errors
- ✅ **ALL tests passing** (including all 99 pre-existing tests)

Do NOT ask permission to run tests, fix errors, or rebuild. Do all of that autonomously. Write the report only after everything is green.

---

## 🧪 Testing Requirements

- **Minimum:** 10 required tests + 2 edge cases = **12 tests minimum**
- **Tick seam:** Use `Func<long>? tickSource` to control time in tests — no `Thread.Sleep`
- **Domain types:** `AdvanceFrameIntent` and `FrameStepCompletedEvent` use `PublishManaged`/`ConsumeManaged`
- **Quality:** Every test must assert specific values or state changes

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `SlaveSyncController.cs` compiles and implements `ITimeController`
- [ ] `dotnet build FDP/FDP.sln` — zero errors
- [ ] 12+ tests in `SlaveSyncControllerTests.cs` cover all 10 required + 2 edge cases
- [ ] `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all tests pass (0 failed)
- [ ] `BATCH-03-REPORT.md` submitted

---

## 📊 Report Requirements

Submit your report to `.dev/time-ctrl-unif/reports/BATCH-03-REPORT.md`.

```markdown
# BATCH-03 Report

## Completion Status
[Completed / Partially Completed]

## Test Results
[Paste final dotnet test output]

## Developer Insights

**Q1: Issues Encountered**
What challenges did you face implementing the PLL and state machine interaction?

**Q2: Weak Points Spotted**
What areas might break when wiring into SimHost/IG/CGF?

**Q3: Design Decisions Made Beyond the Spec**
What choices did you make beyond the instructions? Why?

**Q4: Edge Cases Discovered**
What scenarios weren't covered that you encountered?

**Q5: Suggested commit message**
Single-line message for this batch.
```

---

## ⚠️ Common Pitfalls

- **ABSOLUTE CONSTRAINT:** `SlaveSyncController` must NEVER publish `TimePulseDescriptor`. Add an explicit test for this.
- PLL warm state MUST survive the Stepping→Continuous transition. Do not recreate `JitterFilter`.
- `SwitchTimeModeEvent` carries *both* Deterministic and Continuous transitions — drain and handle both correctly in `Update()`.
- `AdvanceFrameIntent` with `TargetSimTime > 0` must snap `_totalTime = TargetSimTime` (not `+= FixedDelta`).
- Out-of-order `AdvanceFrameIntent` (FrameID < current `_frameNumber`) must be silently ignored — do not crash.
- Study `SlaveTimeController.cs` carefully for the PLL implementation before writing your own.
- Look at how `FdpEventBus` `ConsumeManaged<T>()` returns `IReadOnlyList<T>` — iterate it, don't call `.ToArray()` unnecessarily.

---

## 📚 Reference Materials

- **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — §TCU-SC001, §TCU-T002
- **Design:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.3 Unified Slave Controller, §4.6 TimePulse Source
- **Previous Reviews:** `.dev/time-ctrl-unif/reviews/BATCH-01-REVIEW.md`, `.dev/time-ctrl-unif/reviews/BATCH-02-REVIEW.md`
- **Developer Skill Guide:** `.github/skills/developer/SKILL.md`
