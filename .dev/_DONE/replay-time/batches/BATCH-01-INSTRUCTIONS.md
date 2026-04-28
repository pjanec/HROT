# BATCH-01: Pull Model Infrastructure + Atomic Snap-and-Pause

**Batch Number:** BATCH-01  
**Tasks:** RT-001 (verify), RT-002, RT-003, RT-004, RT-005, RT-006  
**Phase:** Phase 1 (Pull Model Infrastructure) + Phase 2 (Atomic Snap-and-Pause)  
**Estimated Effort:** 7-9 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` - How to work with batches
2. **Design Document:** `.dev/replay-time/DESIGN.md` - Full architecture (read Phase 1 and Phase 2 sections)
3. **Task Definitions:** `.dev/replay-time/TASK-DETAIL.md` - See RT-001 through RT-006 (lines 11-199)

### Source Code Location
- **Primary Work Area 1:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`
- **Primary Work Area 2:** `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs`
- **Primary Work Area 3:** `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs`
- **Primary Work Area 4:** `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`
- **Primary Work Area 5:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs`
- **Test Projects:**
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
  - `FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj`

### Build & Test Commands
```powershell
# Build (check for errors):
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# Run tests for this batch:
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build -v normal
dotnet test FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj --no-build -v normal
```

### Report Submission
**When done, submit your report to:**
`.dev/replay-time/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/replay-time/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch lays the foundational infrastructure for the replay-time pull model. Instead of the old push model where external code set `ExtraFramesThisTick` to advance the playback cursor, the new model lets `PlaybackTickSystem` *pull* the current wall-clock time directly from the kernel's `ITimeController` and advance to wherever the master clock says it should be.

Phase 2 refactors `SlaveSyncController` to support instant snap-and-pause when a `SwitchTimeModeEvent(Deterministic)` arrives with a barrier wall tick that has already elapsed.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks strictly in order. Do not skip ahead.**

1. **RT-001 (verify):** Read the code → confirm DONE → note in report
2. **RT-002:** Implement → Write tests → **ALL tests pass** ✅
3. **RT-003:** Implement → Rewrite old tests → **ALL tests pass** ✅
4. **RT-004:** Implement → Verify integration → **ALL tests pass** ✅
5. **RT-005:** Implement → Verify existing tests → **ALL tests pass** ✅
6. **RT-006:** Implement → Write new tests → **ALL tests pass** ✅

**DO NOT** move to the next task until all tests (including all previous tasks) pass.

---

## Tasks

### RT-001: Verify `GetTimeController()` Already Exists (NO CODE CHANGE)

**File:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-001-expose-gettimecontroller-on-modulehostkernel) — RT-001

`GetTimeController()` is already present at line 1088. Read it and confirm it:
- Returns `_timeController!` (throws `InvalidOperationException` if `!_initialized`)
- Is guarded by the existing `_initialized` flag
- Return type is `ITimeController`

If it satisfies all success conditions in TASK-DETAIL.md RT-001 (T1a, T1b), mark RT-001 as done in your report and proceed immediately to RT-002.

If there is any gap (e.g., wrong guard logic), fix it now.

---

### RT-002: `ReplayModule` Constructor Accepts `ITimeController`

**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-002-replaymodule-constructor-accepts-itimecontroller) — RT-002

**What to change:**
1. Add `ITimeController timeController` parameter to `ReplayModule(string filePath, EntityRepository repo, Action? afterSeek = null)`.
2. Store it as `private readonly ITimeController _timeController`.
3. Throw `ArgumentNullException` if `timeController` is null.
4. In `RegisterSystems`, pass `_timeController` to the `PlaybackTickSystem` constructor (after RT-003 changes it to accept one).

**Order of operations:** RT-003 changes `PlaybackTickSystem`'s constructor. It's fine to update `ReplayModule` first and break `PlaybackTickSystem`'s call temporarily — it will be fixed in RT-003. Build will fail between RT-002 and RT-003; that is expected. Just proceed immediately to RT-003.

**Test in `FDP/Toolkits/Fdp.Toolkits.Tests/Replay/ReplayModuleTests.cs`:**
- Add a test asserting `new ReplayModule(filePath, repo, timeController: null)` throws `ArgumentNullException`.
- Existing `ReplayModule_SeekToFrameAsync_IsOffMainThread` test must still compile and pass (it doesn't use the constructor directly in the relevant assertion path; update its construction call to provide a mock/stub `ITimeController`).

---

### RT-003: Refactor `PlaybackTickSystem` Smart Cursor

**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs` (REFACTOR)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-003-refactor-playbackticksystem-to-use-itimecontrollertotalwallticks) — RT-003

**What to change:**
1. Add `private readonly ITimeController _timeController` field.
2. Update the constructor: `PlaybackTickSystem(PlaybackController playback, ITimeController timeController, Action? afterSeek = null)`.
3. Mark `ExtraFramesThisTick` as `[Obsolete("Use ITimeController-based pull model.")]` — do NOT delete it yet (kept for backward compat during transition). It must have no effect on the cursor logic when a time controller is present.
4. Replace `Execute` body with the smart-cursor algorithm (details in TASK-DETAIL.md RT-003).

**Smart-cursor algorithm (full specification in TASK-DETAIL.md RT-003):**
```
1. targetTicks = _timeController.GetCurrentState().TotalWallTicks
2. currentTicks = (_playback.CurrentFrame >= 0)
                  ? _playback.GetFrameMetadata(_playback.CurrentFrame).WallClockTicks
                  : long.MinValue
3. If targetTicks <= currentTicks -> return (no advance)
4. Count how many StepForward calls it would take to reach a frame where WallClockTicks >= targetTicks.
   If the step count <= StrategyBThreshold: call StepForward in a loop (Strategy A).
   Otherwise: call _playback.SeekToWallClockTicks(repo, targetTicks) + ForceMarkAllDirty(repo) + _afterSeek?.Invoke() (Strategy B).
```

**"Count how many steps" implementation hint:**
Loop forward from `_playback.CurrentFrame + 1` through the frame metadata using `_playback.GetFrameMetadata(i).WallClockTicks`. Count consecutive frames whose `WallClockTicks <= targetTicks`. If count > `StrategyBThreshold`, use Strategy B. This is O(threshold) — only up to 3 frames need to be checked.

**Tests — rewrite `PlaybackTickSystem_StrategyA_SmallGap_UsesStepForward` and `PlaybackTickSystem_StrategyB_LargeGap_UsesSeekToFrame`:**
- Both tests currently construct `PlaybackTickSystem(playback)` without `ITimeController` and use `ExtraFramesThisTick`. Rewrite them to inject a fake/stub `ITimeController` whose `GetCurrentState()` returns a `GlobalTime` with a specific `TotalWallTicks`.
- Use the existing `.fdp` fixture files (same path as current tests use to create `PlaybackController`).
- Success conditions T3a–T3d in TASK-DETAIL.md must all be covered.

**Existing tests that reference `ExtraFramesThisTick`:**
- Lines 60-62 and 82 of `ReplayModuleTests.cs` use `sys.ExtraFramesThisTick`. Rewrite those tests to use `ITimeController`-based API instead of the obsolete property.

---

### RT-004: Wire `ITimeController` into `EcsRecordReplayController`

**File:** `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-004-wire-time-controller-through-ecsrecordreplaycontroller) — RT-004

**Exact change in `PrepareReplayAsync`:**

Before:
```csharp
_activeReplayModule = new ReplayModule(filePath, _repo, _afterSeek);
```

After:
```csharp
_activeReplayModule = new ReplayModule(filePath, _repo, _kernel.GetTimeController(), _afterSeek);
```

That is the **only** change to this file in this task.

No new tests needed: the build succeeding and the existing tests passing is the verification. If `Hrot.SimHost.Tests` has a test for `PrepareReplayAsync`, update its construction to account for the new parameter.

---

### RT-005: Extract `ApplyTimeSnap` from `SlaveSyncController.ApplyResume`

**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs` (REFACTOR)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-005-extract-applytimesnap-from-slavesynccontrollerapplyresume) — RT-005

**What to extract:** Create a private method `ApplyTimeSnap(SwitchTimeModeEvent evt)` containing all the clock-baseline assignments from `ApplyResume`:
- `_baselineSimTime` / `_baselineUnscaledTime` from `evt.SimTimeSnapshot`
- `_timeScale` from `evt.TimeScale`
- `_baselineWallTicks` from `evt.BarrierWallTicks` (or `_prevFrameStartTicks` fallback)
- `_pendingBarrierWallTicks = -1`

`ApplyResume` then becomes: `ApplyTimeSnap(evt); _mode = SlaveMode.Continuous;`

**This is a pure refactor.** All existing `SlaveSyncController` tests must pass without any modification. Do NOT change any test code for RT-005.

---

### RT-006: Instant Snap-and-Pause in `DrainModeSwitchEvents`

**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-rt-006-instant-snap-and-pause-in-drainmodeswitchevents) — RT-006

**What to change in `DrainModeSwitchEvents`:**

Currently the `Deterministic` branch is:
```csharp
if (evt.TargetMode == TimeMode.Deterministic)
{
    if (_mode != SlaveMode.Stepping)
    {
        _pendingBarrierWallTicks = evt.BarrierWallTicks;
        _mode = SlaveMode.BarrierPending;
        _pendingIntents.Clear();
        _lastAcceptedStepFrameId = -1L;
    }
}
```

Change to:
```csharp
if (evt.TargetMode == TimeMode.Deterministic)
{
    if (_mode != SlaveMode.Stepping)
    {
        if (SyncedWallTicks >= evt.BarrierWallTicks)
        {
            // Barrier has already elapsed — snap immediately and enter Stepping.
            ApplyTimeSnap(evt);
            _mode = SlaveMode.Stepping;
            _pendingIntents.Clear();
            _lastAcceptedStepFrameId = -1L;
        }
        else
        {
            _pendingBarrierWallTicks = evt.BarrierWallTicks;
            _mode = SlaveMode.BarrierPending;
            _pendingIntents.Clear();
            _lastAcceptedStepFrameId = -1L;
        }
    }
}
```

**Tests for RT-006 in `Fdp.Toolkits.Tests`:**
- T6a: Inject a `tickSource` whose current value >= `BarrierWallTicks`; assert slave immediately enters `Stepping` mode (no `BarrierPending` frame).
- T6b: Assert `GetCurrentState().TotalWallTicks` is snapped to the event's `SimTimeSnapshot` value (verify via `GetMode() == Deterministic` and `GetCurrentState()`).
- T6c: With `BarrierWallTicks` in the future, assert slave enters `BarrierPending` (existing behavior — existing tests cover this already).
- T6d: Assert `GetMode()` returns `TimeMode.Deterministic` immediately after instant snap.

---

## Quality Standards

**Code quality:**
- Do not add or remove any comments except where the spec explicitly says to. Moving code preserves its comments.
- Do not add `using` statements unless required by new type references.
- Match existing indentation and brace style.

**Test quality:**
- Tests must validate actual behavior (state transitions, method calls, return values), not just compilation.
- Use the existing `.fdp` fixture files already referenced in `ReplayModuleTests.cs` for `PlaybackController`-dependent tests.
- For `ITimeController` stubs in tests: a minimal implementation returning a `GlobalTime` with specific `TotalWallTicks` is sufficient. No mocking framework required.

**After each task:** Run the full build and all tests in the batch's test projects before moving to the next task.

---

## Success Criteria

This batch is DONE when:
- [ ] RT-001: Verified DONE (no code change needed or fix if gap found)
- [ ] RT-002: `ReplayModule` accepts and stores `ITimeController`; null throws `ArgumentNullException`
- [ ] RT-003: `PlaybackTickSystem` uses smart cursor; old `ExtraFramesThisTick` tests rewritten
- [ ] RT-004: `EcsRecordReplayController.PrepareReplayAsync` passes `_kernel.GetTimeController()` to `ReplayModule`
- [ ] RT-005: `ApplyTimeSnap` extracted; all existing `SlaveSyncController` tests pass
- [ ] RT-006: Instant snap path implemented; T6a-T6d tests written and passing
- [ ] Solution builds: `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` with zero `error CS` lines
- [ ] All tests in `Fdp.Toolkits.Tests` and `Fdp.ModuleHost.Tests` pass

---

## Developer Insights (Required in Report)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** For RT-003's smart-cursor, did you discover any edge cases in the "count frames ahead" logic (e.g., recordings with non-monotonic wall ticks, recordings where all frames share the same tick)?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** Are there any weak points in the existing `SlaveSyncController` or `PlaybackTickSystem` codebase you noticed?

**Q5:** What is your suggested commit message for this batch?
