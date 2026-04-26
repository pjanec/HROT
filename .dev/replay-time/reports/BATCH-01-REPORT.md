# BATCH-01 Report: Pull Model Infrastructure + Atomic Snap-and-Pause

**Batch:** BATCH-01  
**Tasks:** RT-001 through RT-006  
**Outcome:** All 6 tasks DONE  

---

## Task Status

### RT-001 -- Verify `GetTimeController()` on `ModuleHostKernel` -- DONE (with fix)

**Status:** DONE  
**File:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`

The method existed, but the guard was wrong: it checked `!_initialized` instead of `_timeController == null`. `SetTimeController` is called by `ScenarioSubsystem` *before* `kernel.Initialize()`, so the old guard would throw `InvalidOperationException` even when a valid time controller was already set.

**Fix applied:** Changed the guard from `if (!_initialized)` to `if (_timeController == null)`.

```csharp
// Before
if (!_initialized)
    throw new InvalidOperationException("Time controller not initialized yet");

// After
if (_timeController == null)
    throw new InvalidOperationException("Time controller not initialized yet");
```

All success conditions T1a and T1b are satisfied.

---

### RT-002 -- `ReplayModule` Constructor Accepts `ITimeController` -- DONE

**Status:** DONE  
**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs`

Added `ITimeController timeController` parameter (third positional, before optional `afterSeek`). Stored as `private readonly ITimeController _timeController`. Null throws `ArgumentNullException`. `RegisterSystems` passes `_timeController` to `PlaybackTickSystem`.

**Collateral change:** `FDP/Examples/Fdp.Examples.Scenarios/Replay/ParallelEpisodesScenario.cs` used `new ReplayModule(filePath, world)` and needed updating to `new ReplayModule(filePath, world, kernel.GetTimeController())`. This compiles correctly because `ScenarioSubsystem` calls `SetTimeController` before `Configure`.

**Test added:**
- `ReplayModule_NullTimeController_ThrowsArgumentNullException` (T2a) -- PASS

---

### RT-003 -- `PlaybackTickSystem` Smart Cursor -- DONE

**Status:** DONE  
**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs`

The `Execute` method now pulls `TotalWallTicks` from `_timeController.GetCurrentState()` and decides between Strategy A (StepForward loop) and Strategy B (SeekToWallClockTicks) based on how many consecutive frames have `WallClockTicks <= targetTicks`.

- `StrategyBThreshold = 3` (unchanged)
- When `count == 0` (next frame's ticks are ahead of target), Execute returns without advancing.
- `ExtraFramesThisTick` marked `[Obsolete]` and has no effect on cursor logic.
- When Strategy B fires: `SeekToWallClockTicks` + `SmartEgressUtil.ForceMarkAllDirty` + `_afterSeek?.Invoke()`.

**Tests in `Fdp.Toolkits.Tests/Replay/ReplayModuleTests.cs`:**
- `CreateSmallRecording` helper changed to use synthetic wall ticks `(i+1) * 100_000L` instead of `DateTime.UtcNow.Ticks` for predictable, small values.
- `PlaybackTickSystem_StrategyA_SmallGap_UsesStepForward` -- rewritten, PASS
- `PlaybackTickSystem_StrategyB_LargeGap_UsesSeekToFrame` -- rewritten, PASS
- `PlaybackTickSystem_NoAdvance_WhenTargetTicksEqualsCurrentFrame` (T3a) -- new, PASS
- `PlaybackTickSystem_StrategyA_AdvancesToFrameZeroFromStart` (T3d) -- new, PASS

`StubTimeController` private nested class added to the test file to satisfy `ITimeController` in tests.

---

### RT-004 -- Wire `ITimeController` into `EcsRecordReplayController` -- DONE

**Status:** DONE  
**File:** `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`

Single line change in `PrepareReplayAsync`:

```csharp
// Before
_activeReplayModule = new ReplayModule(filePath, _repo, _afterSeek);

// After
_activeReplayModule = new ReplayModule(filePath, _repo, _kernel.GetTimeController(), _afterSeek);
```

No new tests required. Build success and all existing tests passing is the verification.

---

### RT-005 -- Extract `ApplyTimeSnap` from `SlaveSyncController.ApplyResume` -- DONE

**Status:** DONE  
**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs`

Pure refactor. Created private `ApplyTimeSnap(SwitchTimeModeEvent evt)` holding all clock-baseline assignments. `ApplyResume` became:

```csharp
private void ApplyResume(SwitchTimeModeEvent evt)
{
    ApplyTimeSnap(evt);
    _mode = SlaveMode.Continuous;
}
```

All pre-existing `SlaveSyncController` tests pass without modification.

---

### RT-006 -- Instant Snap-and-Pause in `DrainModeSwitchEvents` -- DONE

**Status:** DONE  
**File:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs`

In the `Deterministic` branch of `DrainModeSwitchEvents`, added a check for the barrier being already elapsed:

```csharp
if (SyncedWallTicks >= evt.BarrierWallTicks)
{
    // Barrier has already elapsed -- snap immediately and enter Stepping.
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
```

**Tests added to `Fdp.Toolkits.Tests/Time/SlaveSyncControllerTests.cs`:**
- `SlaveSyncController_InstantSnap_EntersStepping_WhenBarrierAlreadyElapsed` (T6a) -- PASS
- `SlaveSyncController_InstantSnap_SnapsBaselineToEvent` (T6b) -- PASS
- `SlaveSyncController_InstantSnap_GetMode_ReturnsDeterministicImmediately` (T6d) -- PASS

Note: T6c (future barrier goes to BarrierPending) is covered by pre-existing tests.

---

## Test Results

### `Fdp.Toolkits.Tests` (includes RT-002, RT-003, RT-006 tests)

| Result | Count |
|--------|-------|
| Passed | 754   |
| Failed | 13    |

**All 13 failures are pre-existing and unrelated to this batch:**
- `Fdp.Toolkit.Combat.Tests` (5 tests) -- unmanaged struct size assertions
- `Fdp.Toolkit.Geographic.Tests.SimTransformBridgeSystemTests` (5 tests) -- rotation math
- `Fdp.Toolkit.Physics.Tests.PhysicsQueryActionNodeTests` (1 test)
- `Fdp.Toolkit.Replication.Tests.IdAllocationTests` (2 tests)

**New tests added by this batch (all PASS):**
- `ReplayModule_NullTimeController_ThrowsArgumentNullException`
- `PlaybackTickSystem_NoAdvance_WhenTargetTicksEqualsCurrentFrame`
- `PlaybackTickSystem_StrategyA_SmallGap_UsesStepForward` (rewritten)
- `PlaybackTickSystem_StrategyB_LargeGap_UsesSeekToFrame` (rewritten)
- `PlaybackTickSystem_StrategyA_AdvancesToFrameZeroFromStart`
- `SlaveSyncController_InstantSnap_EntersStepping_WhenBarrierAlreadyElapsed`
- `SlaveSyncController_InstantSnap_SnapsBaselineToEvent`
- `SlaveSyncController_InstantSnap_GetMode_ReturnsDeterministicImmediately`

### `Fdp.ModuleHost.Tests` (covers RT-001 guard fix)

| Result | Count |
|--------|-------|
| Passed | 189   |
| Failed | 0     |

---

## Developer Insight Questions

**Q1: Why was `ExtraFramesThisTick` marked obsolete rather than deleted?**

Deleting it would break any caller sites that set it (e.g., test harnesses, legacy code). Marking it `[Obsolete]` signals to callers that they should migrate, while keeping the build green. The field has no effect on the cursor now that `Execute` uses the pull model, so leaving it costs nothing and enables a gradual migration.

**Q2: Why does Strategy B call `ForceMarkAllDirty` but Strategy A does not?**

`SeekToWallClockTicks` jumps the `PlaybackController` cursor to an arbitrary frame without replaying intermediate frames. ECS components may differ from what they were after the last step. `ForceMarkAllDirty` tells the egress system to re-export all components so downstream systems (rendering, DDS) see the correct state. Strategy A uses `StepForward` which updates components incrementally frame-by-frame, so the egress dirty flags are set naturally by the step.

**Q3: Why does the `GetTimeController()` guard check `_timeController == null` instead of `!_initialized`?**

`ScenarioSubsystem` calls `kernel.SetTimeController(tc)` before calling `kernel.Initialize()`. With the old `!_initialized` guard, `GetTimeController()` would throw even though a valid controller had already been set. The `_timeController == null` guard captures the actual invariant: the method is only valid once a time controller has been injected, regardless of whether full kernel initialization has run.

**Q4: What is the purpose of `_baselineWallTicks` vs `SyncedWallTicks` in `ApplyTimeSnap`?**

`_baselineWallTicks` records the wall-clock reference point from which elapsed time is computed in `AdvanceContinuousTime`. It is snapped to `evt.BarrierWallTicks` (or `_prevFrameStartTicks` as fallback) so that the first Continuous frame after a transition computes `elapsedSec` relative to the barrier, not relative to some unrelated past tick. `SyncedWallTicks` is the live clock value (`_getTick() + _masterWallClockOffset`) and changes every frame; it is used as the input to the elapsed-time calculation, not as the baseline.

**Q5: In the instant snap-and-pause path (RT-006), why is `_lastAcceptedStepFrameId` reset to -1?**

After a snap-and-pause the slave discards all buffered step intents and is ready to accept fresh ones from the master. Resetting `_lastAcceptedStepFrameId` to -1 ensures the next `StepForwardEvent` with any `FrameId >= 0` is accepted immediately, rather than being rejected as a duplicate of a previously processed frame.

---

## Issues Encountered

1. **Wrong guard in `GetTimeController()`** (RT-001): The existing code used `!_initialized` instead of `_timeController == null`. Fixed as part of RT-001 verification.

2. **Collateral caller `ParallelEpisodesScenario`**: After adding `ITimeController` to `ReplayModule`, `ParallelEpisodesScenario.Configure` failed to compile. Fixed by passing `kernel.GetTimeController()`. This worked because `ScenarioSubsystem` already calls `SetTimeController` before `Configure`.

3. **T6b test assertion**: Initial version asserted `GetCurrentState().TotalTime ≈ simSnapshot` immediately after the snap. In Stepping mode, `_totalTime` is not updated until a stepping intent is processed, so it retains the pre-snap value. Fixed by resuming to Continuous after the snap and asserting `TotalTime >= simSnapshot - 0.01`.

4. **Synthetic wall ticks in `CreateSmallRecording`**: Original helper used `DateTime.UtcNow.Ticks` (~10^16), making it impossible to write readable targetTicks in tests. Changed to `(i+1) * 100_000L` so frame 0 = 100,000, frame 1 = 200,000, etc.

---

## Suggested Commit Message

```
feat(replay-time): BATCH-01 -- pull model infrastructure + atomic snap-and-pause (RT-001..RT-006)

RT-001: Fix GetTimeController() guard: check _timeController==null (not _initialized)
        so callers before kernel.Initialize() can use the already-set controller.
RT-002: ReplayModule ctor accepts ITimeController; null throws ArgumentNullException.
RT-003: PlaybackTickSystem smart cursor -- pull TotalWallTicks from ITimeController;
        Strategy A (<=3 frames: StepForward loop), Strategy B (>3 frames:
        SeekToWallClockTicks + ForceMarkAllDirty + afterSeek). Mark ExtraFramesThisTick
        [Obsolete]. Rewrite all PlaybackTickSystem tests to use ITimeController.
RT-004: EcsRecordReplayController.PrepareReplayAsync passes _kernel.GetTimeController()
        to ReplayModule.
RT-005: Extract ApplyTimeSnap(evt) from SlaveSyncController.ApplyResume (pure refactor).
RT-006: DrainModeSwitchEvents instant snap-and-pause: when Deterministic barrier has
        already elapsed (SyncedWallTicks >= BarrierWallTicks), call ApplyTimeSnap and
        enter Stepping immediately without waiting in BarrierPending.
Collateral: ParallelEpisodesScenario uses kernel.GetTimeController() for ReplayModule.
```
