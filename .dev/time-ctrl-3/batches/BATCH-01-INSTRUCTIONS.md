# BATCH-01: NTP Message Types, TimeConfig Extensions & MasterSyncController Fixes

**Batch Number:** BATCH-01  
**Tasks:** TC3-P1-T01, TC3-P1-T02, TC3-P2-T01, TC3-P2-T02, TC3-P2-T03, TC3-P2-T04  
**Phase:** Phase 1 (Messages + Config) and Phase 2 (MasterSyncController Fixes)  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch introduces the DDS message types required for the NTP-style two-way handshake
and fixes two longstanding bugs in `MasterSyncController` that cause the 200 ms pause-barrier
drift and the per-step simulation-time divergence.

**Start here — read every document before touching code:**

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Design Document:** `.dev/time-ctrl-3/DESIGN.md` — Full architecture. Pay close
   attention to §3 (message types), §4 (master fixes including the critical §4.4 physical-clock
   barrier fix), and §2.4 (clock concepts table).
3. **Task Details:** `.dev/time-ctrl-3/TASK-DETAIL.md` — Exact instructions and success
   conditions for every task in this batch.
4. **Onboarding:** `.dev/time-ctrl-3/ONBOARDING.md` — Project layout, build commands.

### Source Code Locations

| Area | Path |
|------|------|
| **Primary — messages** | `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` |
| **Primary — config** | `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeConfig.cs` |
| **Primary — master ctrl** | `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` |
| **Tests** | `FDP/Toolkits/FDP.Toolkit.Time.Tests/` |

### Report Submission

When done, write your report to:
`.dev/time-ctrl-3/reports/BATCH-01-REPORT.md`

If you need to raise questions before coding:
`.dev/time-ctrl-3/questions/BATCH-01-QUESTIONS.md`

---

## Context

The current `MasterSyncController` has two independent bugs:

1. **Constructor**: `_totalWallTicks` is never initialised, defaulting to `0`. While this does
   not directly break barrier issuance (barrier now uses `_getTick()` — see Bug B-04 fix), it
   means the continuous-mode accumulation starts from zero instead of the physical OS baseline,
   corrupting `TotalWallTicks` in the `GlobalTime` state object and in `SeedState`.

2. **`Step()`**: `TargetSimTime = 0` is hardcoded, so slaves accumulate their own drifted time
   instead of snapping to the master's authoritative value on each step.

3. **`SwitchToDeterministic` + `UpdateBarrierPending`** (Bug B-04 — critical): The barrier is
   computed from `_totalWallTicks + Lookahead`. During lockstep, `Step()` advances
   `_totalWallTicks` *synthetically* (faster or slower than real time). After step→resume→pause,
   `_totalWallTicks` permanently diverges from the physical OS clock. The slave evaluates the
   barrier against `SyncedWallTicks` (physical clock), so the second pause fires at the wrong
   moment. **Fix: always compute barrier as `_getTick() + Lookahead`** and evaluate it via
   `_getTick()` on the master side.

The two new DDS message structs (`TimeSyncRequest` / `TimeSyncResponse`) must be defined now
because subsequent batches depend on them.

---

## Tasks

### TC3-P1-T01 — Add TimeSyncRequest and TimeSyncResponse

See full spec: [TASK-DETAIL.md §TC3-P1-T01](../TASK-DETAIL.md#tc3-p1-t01--add-timesyncrequestresponse-dds-structs)

Key points:
- Add both structs at the **end** of `TimeMessages.cs`, after all existing structs.
- EventId 108 = `TimeSyncRequest`, EventId 109 = `TimeSyncResponse`.
- Both must have `[MessagePackObject]`, `[DdsTopic]`, `[EventId]`, and field attributes.
- Use `[DdsKey]` on `ClientNodeId` in both structs.

Tests to write in `TimeMessagesTests.cs`:
- `TimeSyncRequest_RoundTrip_PreservesAllFields`
- `TimeSyncResponse_RoundTrip_PreservesAllFields`
- `TimeSyncRequest_FdpEventBus_PublishConsume_RoundTrip`
- `TimeSyncResponse_FdpEventBus_PublishConsume_RoundTrip`

### TC3-P1-T02 — Add TimeConfig NTP properties

See full spec: [TASK-DETAIL.md §TC3-P1-T02](../TASK-DETAIL.md#tc3-p1-t02--add-timeconfig-properties-for-ntp-sync)

Add three properties to `TimeConfig`:
- `MaxRttTicks` — default `(long)(0.2 * Stopwatch.Frequency)`
- `SyncRefreshIntervalTicks` — default `Stopwatch.Frequency`
- `SyncCorrectionWeight` — default `0.1`

Tests (can go in a new `TimeConfigTests.cs` or `TimeControllerFactoryTests.cs`):
- `TimeConfig_Default_MaxRttTicks_IsApproximately200ms`
- `TimeConfig_Default_SyncRefreshIntervalTicks_Is1Second`
- `TimeConfig_Default_SyncCorrectionWeight_IsPointOne`

### TC3-P2-T01 — Fix constructor: `_totalWallTicks = now`

See full spec: [TASK-DETAIL.md §TC3-P2-T01](../TASK-DETAIL.md#tc3-p2-t01--fix-mastersynccollroller-constructor-initialise-_totalwallticks)

One-line add after `_lastTickSample = now`:
```csharp
_totalWallTicks = now;
```
Also add the constructor debug log (`[TC3][Master] Initialized...`).

Tests in `MasterSyncControllerTests.cs`:
- `MasterSyncController_Constructor_TotalWallTicks_InitialisedToNow`
- `MasterSyncController_SwitchToDeterministic_BarrierIsAbsoluteNowPlusLookahead`
- `MasterSyncController_BarrierFix_SlaveEntersStepping_AfterLookahead`

### TC3-P2-T02 — Fix `Step()`: `TargetSimTime = _totalTime`

See full spec: [TASK-DETAIL.md §TC3-P2-T02](../TASK-DETAIL.md#tc3-p2-t02--fix-mastersynccollrollerstep-populate-targetsimtime)

Change the hardcoded `TargetSimTime = 0` to `TargetSimTime = _totalTime` (after the increment).

Tests in `MasterSyncControllerTests.cs`:
- `MasterSyncController_Step_TargetSimTime_IsPopulated`
- `MasterSyncController_Step_TargetSimTime_Accumulates`
- `MasterSyncController_Step_SlaveSnapsToMasterSimTime`

### TC3-P2-T03 — Add debug logging

See full spec: [TASK-DETAIL.md §TC3-P2-T03](../TASK-DETAIL.md#tc3-p2-t03--add-debug-logging-to-mastersynccollroller)

Add `FdpLog<MasterSyncController>.Debug(...)` calls at constructor, `SwitchToDeterministic`,
`Step()`, and inside `UpdateStepping` on each individual ACK removal.

Format strings are in DESIGN.md §2.5.

Tests:
- Existing tests must remain green (no behaviour change)
- `MasterSyncController_Step_EmitsDebugLog` (if a test `FdpLog` sink is available)

### TC3-P2-T04 — Physical clock barrier fix (CRITICAL)

See full spec: [TASK-DETAIL.md §TC3-P2-T04](../TASK-DETAIL.md#tc3-p2-t04--fix-switchtodeterministic-and-updatebarrierpending-to-use-the-physical-clock)

**Part 1 — `SwitchToDeterministic`:**
```csharp
// BEFORE:
long barrierWallTicks = _totalWallTicks + _config.LookaheadWallTicks;
// AFTER:
long barrierWallTicks = _getTick() + _config.LookaheadWallTicks;
```

**Part 2 — `UpdateBarrierPending`:**
```csharp
// BEFORE:
if (_totalWallTicks >= _pendingBarrierWallTicks)
// AFTER:
if (_getTick() >= _pendingBarrierWallTicks)
```

Tests in `MasterSyncControllerTests.cs`:
- `MasterSyncController_SwitchToDeterministic_BarrierBasedOnPhysicalClock`
- `MasterSyncController_SwitchToDeterministic_BarrierCorrectAfterStepping`
- `MasterSyncController_UpdateBarrierPending_UsesPhysicalClock`

---

## Test Quality Requirements (MANDATORY)

All tests must:
- **Assert specific values / state**, not just "no exception thrown"
- Use the injected `tickSource` (`Func<long>`) to control time deterministically
- Have meaningful names: `ClassName_Method_Condition_Expected`
- Cover the _logic_, not just compilation

Tests that assert only "passes compilation" or check string content without verifying
behavior will be rejected in review.

---

## Test-Driven Task Progression

**Follow this order strictly:**

1. Write the test (it must fail first — red).
2. Implement just enough code to make it pass — green.
3. Verify no existing tests broke — refactor if needed.
4. Move to the next task.

Do not start the next task until the current one is green.

---

## Build & Test Commands

```powershell
# Build the toolkit
dotnet build FDP/Toolkits/FDP.Toolkit.Time/FDP.Toolkit.Time.csproj

# Run the time toolkit tests
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj --nologo -v minimal

# Run all tests as a regression check before submitting
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj --nologo 2>&1 | Select-Object -Last 5
```

---

## Developer Insights Required in Report

Your BATCH-01-REPORT.md **must** answer these questions:

1. What issues did you encounter during implementation? How did you resolve them?
2. Did you spot any weak points in the existing codebase that are not in the current TASK-DETAIL?
3. What design decisions did you make beyond the spec (if any)?
4. Are there any edge cases you discovered that weren't mentioned?
5. Any concerns about the existing test infrastructure (FdpLog sink for TC3-P2-T03)?
