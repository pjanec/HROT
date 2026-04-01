# BATCH-02: Unified Master Controller — MasterSyncController

**Batch Number:** BATCH-02  
**Tasks:** TCU-MC001, TCU-T001  
**Phase:** Phase 2 — Unified Master Controller  
**Estimated Effort:** 6–8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (wire DTOs, domain message types must exist)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch creates `MasterSyncController` — the unified replacement for `MasterTimeController` + `SteppedMasterController` + `DistributedTimeCoordinator`. The controller is a self-contained state machine that handles Continuous, BarrierPending, and Stepping modes internally using the domain message types created in BATCH-01. You are NOT wiring it into the Orchestrator yet (that is Phase 5). You are NOT removing old classes yet (that is Phase 5).

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.2 Unified Master Controller (read every word)
2. **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — read TCU-MC001 and TCU-T001 in full
3. **Previous Review:** `.dev/time-ctrl-unif/reviews/BATCH-01-REVIEW.md` — context on domain message types
4. **Existing code to study (DO NOT MODIFY):**
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterTimeController.cs` — existing continuous impl
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedMasterController.cs` — existing stepping impl
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs` — mode switching
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/ISteppableTimeController.cs` — interface to implement
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeConfig.cs` — config container (includes `LookaheadWallTicks`)
   - `FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs` — domain types from BATCH-01
   - `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` — wire DTOs from BATCH-01
5. **Existing tests for reference:**
   - `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterTimeControllerTests.cs`
   - `FDP/Toolkits/FDP.Toolkit.Time.Tests/SteppedMasterControllerTests.cs`

### Source Code Location

- **New controller:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` (NEW FILE)
- **New tests:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs` (NEW FILE)
- **FDP solution:** `FDP/FDP.sln`
- **Time tests csproj:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj`

### Report Submission

**When done, submit your report to:**  
`.dev/time-ctrl-unif/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev/time-ctrl-unif/questions/BATCH-02-QUESTIONS.md`

---

## Context

`MasterSyncController` is the heart of the unification. It replaces three separate classes with one state machine (`Continuous → BarrierPending → Stepping → Continuous`). The key insight: by keeping state alive across mode transitions (no object swap), PLL warm-up is preserved and time jumps are eliminated.

The controller communicates solely through `FdpEventBus` (no direct DDS coupling). It:
- Publishes `SwitchTimeModeEvent` when mode transitions are requested
- Publishes `AdvanceFrameIntent` when `Step()` is called
- Publishes `TimePulseDescriptor` ~1 Hz in Continuous/BarrierPending modes  
- Consumes `FrameStepCompletedEvent` to track slave ACKs

**Related Tasks:**
- [TCU-MC001](../docs/TASK-DETAIL.md#tcu-mc001--mastersynccotroller) — Implementation spec
- [TCU-T001](../docs/TASK-DETAIL.md#tcu-t001--unit-tests-mastersynccotroller) — Test coverage spec

---

## 🎯 Batch Objectives

1. `MasterSyncController.cs` exists, compiles, and implements `ISteppableTimeController`.
2. All nine success conditions from TCU-MC001 have corresponding passing tests.
3. Additional edge-case tests per TCU-T001 spec.
4. All 87+ pre-existing tests continue passing.

---

## ✅ Tasks

### Task 1: Implement MasterSyncController (TCU-MC001)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` (NEW FILE)  
**Task Definition:** See [TCU-MC001 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-mc001--mastersynccotroller) — the full internal state, public API, and all behaviour rules are specified there. Read that section in its entirety before writing a single line.

**Key design rules (summary — full details in TASK-DETAIL.md):**

**State machine:**
```
Continuous ──SwitchToDeterministic()──► BarrierPending ──barrier crossed──► Stepping
Stepping   ──SwitchToContinuous()──────────────────────────────────────────► Continuous
BarrierPending ──SwitchToContinuous()──────────────────────────────────────► Continuous
```

**Critical behaviour rules:**
- `Update()` in Continuous: measure wall-clock delta → accumulate `_totalWallTicks`, `_totalTime`, `_unscaledTotalTime` → publish `TimePulseDescriptor` when `Stopwatch.GetTimestamp() - _lastPulseTicks > Stopwatch.Frequency` → return `GlobalTime`
- `Update()` in BarrierPending: same as Continuous AND check `_totalWallTicks >= _pendingBarrierWallTicks` → transition to Stepping, reset `_pendingAcks = new HashSet<int>(_expectedSlaves)`
- `Update()` in Stepping: drain `FrameStepCompletedEvent` from bus → remove node IDs from `_pendingAcks` → return current `GlobalTime` with `DeltaTime=0`
- `Step(delta)` only valid in Stepping; ignored (returns current state) if `_pendingAcks` non-empty; otherwise: increment `_frameNumber`, accumulate time, publish `AdvanceFrameIntent`, reset `_pendingAcks`
- `SwitchToDeterministic`: compute barrier wall ticks = `_totalWallTicks + _config.LookaheadWallTicks`; set mode to BarrierPending; publish `SwitchTimeModeEvent { TargetMode=Deterministic, BarrierWallTicks, FixedDelta, TimeScale }`
- `SwitchToContinuous`: idempotent if already Continuous with no pending barrier; cancel barrier; snap `SimTimeSnapshot = _totalTime`; transition to Continuous; publish `SwitchTimeModeEvent { TargetMode=Continuous, BarrierWallTicks=0, SimTimeSnapshot, TimeScale }`
- `GetMode()` returns `TimeMode.Continuous` for both Continuous AND BarrierPending states; returns `TimeMode.Deterministic` only when Stepping

**Test seam for Stopwatch:**  
The controller needs a testable tick source. Look at how existing controllers (`MasterTimeController`, `SteppedMasterController`) provide a seam — use a `Func<long>? _tickSource` constructor parameter that defaults to `Stopwatch.GetTimestamp` when null.

**Bus registration:**  
The constructor must register `FdpEventBus` for the types it publishes/consumes: `FrameStepCompletedEvent`, `AdvanceFrameIntent`, `SwitchTimeModeEvent`, `TimePulseDescriptor`.

**Constraints:**
- Must NOT import or use `DdsWriter`, `DdsReader`, or any CycloneDDS namespace.
- All DDS traffic goes through bus + translators.

---

### Task 2: Unit Tests for MasterSyncController (TCU-T001)

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs` (NEW FILE)  
**Task Definition:** See [TCU-T001 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-t001--unit-tests-mastersynccotroller) — the 9 success conditions from TCU-MC001 are listed there, plus additional edge cases.

**Required tests (from TCU-MC001 success conditions — all 9 must be present):**

1. `MasterSyncController_ContinuousMode_AdvancesTime` — construct with empty slave set; call `Update()` twice (use tick-source seam to provide artificial ticks); assert `TotalTime > 0`.
2. `MasterSyncController_SwitchToDeterministic_PublishesBarrierEvent` — call `SwitchToDeterministic`; swap bus; assert one `SwitchTimeModeEvent` with `TargetMode==Deterministic` and `BarrierWallTicks > currentWallTicks`.
3. `MasterSyncController_BarrierPending_TransitionsToStepping` — set `LookaheadWallTicks = 0`; call `SwitchToDeterministic`; call `Update()`; assert `GetMode() == TimeMode.Deterministic`.
4. `MasterSyncController_Step_PublishesAdvanceFrameIntent` — transition to Stepping; call `Step(0.016f)`; swap bus; consume `AdvanceFrameIntent`; assert `FrameID == 1`, `FixedDelta ≈ 0.016f`.
5. `MasterSyncController_Step_BlocksUntilAllAcksReceived` — two slaves; `Step(0.016f)`; verify second `Step()` returns same frame (no advance); publish `FrameStepCompletedEvent` for slave A via bus; verify still blocked; publish for slave B; call `Update()`; verify `Step()` now advances to frame 2.
6. `MasterSyncController_SwitchToContinuous_PublishesSnapshotEvent` — while in Stepping (seed `_totalTime`-equivalent state); call `SwitchToContinuous()`; swap bus; assert `SwitchTimeModeEvent.TargetMode == Continuous` and `SimTimeSnapshot ≈ seeded value`.
7. `MasterSyncController_SwitchToContinuous_IdempotentWhenAlreadyContinuous` — call `SwitchToContinuous()` twice from Continuous; assert zero/one event published on the second call (second call is a no-op).
8. `MasterSyncController_SeedState_RestoresTotalTime` — call `SeedState(new GlobalTime { TotalTime=99.0, FrameNumber=500 })`; assert `GetCurrentState().TotalTime ≈ 99.0`.
9. `MasterSyncController_PublishesTimePulse_OncePerSecond` — run ~65 frames each advancing ~16 ms via tick seam; assert exactly one `TimePulseDescriptor` published.

**Additional edge-case tests (from TCU-T001):**
- `MasterSyncController_Step_InContinuousMode_IsNoOp` — call `Step(0.016f)` while in Continuous mode; assert frame does not advance.
- `MasterSyncController_AckFromUnknownNode_IsIgnored` — in Stepping with one known slave; publish `FrameStepCompletedEvent` from an unrecognised node ID; assert `_pendingAcks` still non-empty (step still blocked).
- `MasterSyncController_TwoFullPauseCycles_WorkCorrectly` — complete Continuous→Stepping→Continuous→Stepping→Continuous cycle; assert final mode is Continuous and time has advanced correctly.

**Quality bar:**
- Every test must assert specific values (FrameID, TotalTime, mode enum value, event field values)
- Use the tick-source seam liberally to avoid real `Thread.Sleep` in tests
- Bus swap (`SwapBuffers()`) must be called before consuming events

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests before moving on:**

1. **Task 1 (TCU-MC001):** Implement `MasterSyncController.cs` → `dotnet build FDP/FDP.sln` — zero errors ✅  
2. **Task 2 (TCU-T001):** Write all tests → `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all pass ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Build succeeds with zero errors
- ✅ **ALL tests passing** (including all 87 pre-existing tests)

Do NOT ask permission to run tests, fix errors, or rebuild. Do all of that autonomously. Write the report only after everything is green.

---

## 🧪 Testing Requirements

- **Minimum:** 9 required tests + 3 edge cases = **12 tests minimum**
- **Tick seam:** Use the `Func<long>? tickSource` constructor parameter to control time in tests — do NOT use `Thread.Sleep`
- **Event consumption:** Always `SwapBuffers()` before consuming events from the bus
- **Quality:** Every test must assert specific field values or state changes — "no exception" only is not acceptable

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `MasterSyncController.cs` compiles and implements `ISteppableTimeController`
- [ ] `dotnet build FDP/FDP.sln` — zero errors
- [ ] 12+ tests in `MasterSyncControllerTests.cs` cover all 9 required + 3 edge cases
- [ ] `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all tests pass (0 failed)
- [ ] `BATCH-02-REPORT.md` submitted

---

## 📊 Report Requirements

Submit your report to `.dev/time-ctrl-unif/reports/BATCH-02-REPORT.md`.

```markdown
# BATCH-02 Report

## Completion Status
[Completed / Partially Completed]

## Test Results
[Paste final dotnet test output showing pass count]

## Developer Insights

**Q1: Issues Encountered**
What problems did you hit implementing the state machine? How did you resolve them?

**Q2: Weak Points Spotted**
What fragile areas did you notice? What could break when wiring this into the Orchestrator?

**Q3: Design Decisions Made Beyond the Spec**
What choices did you make that weren't explicitly specified? Why?

**Q4: Edge Cases Discovered**
What scenarios weren't in the instructions that you encountered?

**Q5: Suggested commit message**
Single-line message for this batch.
```

---

## ⚠️ Common Pitfalls

- Do NOT import DDS types inside `MasterSyncController` — it must be DDS-free.
- Do NOT use `Thread.Sleep` in tests — use the tick-source seam.
- Remember `GetMode()` maps both `Continuous` and `BarrierPending` internal states to `TimeMode.Continuous` externally.
- `SwitchToContinuous()` must be idempotent when already in Continuous with no pending barrier.
- `FrameStepCompletedEvent` from an unknown node must be silently ignored (no exception, no side effect on `_pendingAcks`).
- Look at how `FdpEventBus` is registered and used in existing tests before writing. See `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterTimeControllerTests.cs` for patterns.
- Domain types (`AdvanceFrameIntent`, `FrameStepCompletedEvent`) have no `[EventId]` — use `PublishManaged`/`ConsumeManaged` as established in BATCH-01.

---

## 📚 Reference Materials

- **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — §TCU-MC001, §TCU-T001
- **Design:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.2 Unified Master Controller
- **Previous BATCH-01 Review:** `.dev/time-ctrl-unif/reviews/BATCH-01-REVIEW.md`
- **Developer Skill Guide:** `.github/skills/developer/SKILL.md`
