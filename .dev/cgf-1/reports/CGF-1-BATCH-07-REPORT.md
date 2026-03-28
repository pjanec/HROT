# CGF-1-BATCH-07 Report

**Batch:** CGF-1-BATCH-07  
**Developer:** Developer  
**Date:** 2026-03-29  
**Status:** COMPLETE

---

## Summary

**Part A** (tech-debt) was completed in full: `SteppedMasterController` wall-clock continuity (A.1), slave non-zero wall-tick seed test (A.2), `SurvivingNodes` deferred with documented justification (A.3), and DEBT-TRACKER updated (A.4).

**Part B** (CGF1-S0204) was completed: `SwitchTimeModeEvent` refactored to `BarrierWallTicks`, `DistributedTimeCoordinator` and `SlaveTimeModeListener` migrated to wall-tick-based barrier, `TimeConfig.LookaheadWallTicks` added, `TimeNetworkModule` created for DDS registration, and all five `FutureBarrierTests` success conditions implemented and passing.

Solution build: **0 errors**. Full `dotnet test IOS-IG-SimHost.sln --nologo --no-build`: **green** (all passing, 2 pre-existing skips).

---

## Part A — Tech Debt

### A.1 — `SteppedMasterController` wall-clock continuity

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedMasterController.cs`

**Change:** Added `private long _totalWallTicks` field.
- `SeedState(GlobalTime state)` now sets `_totalWallTicks = state.TotalWallTicks` (persists the PLL-synchronized clock baseline across mode switches).
- `Step(float)` accumulates `_totalWallTicks += (long)(fixedDeltaTime * Stopwatch.Frequency)` (deterministic tick advancement).
- `GetCurrentTime()` returns `TotalWallTicks = _totalWallTicks` (no re-derivation from `_unscaledTotalTime`).

**Test:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/SwitchableTimeControllerTests.cs`, test `SwitchTo_TransfersWallTicksToNewController` — seeds a `MasterTimeController` with `TotalWallTicks = 9_876_543`, calls `SwitchTo(stepped)`, asserts `stepped.GetCurrentState().TotalWallTicks == 9_876_543`.

### A.2 — Slave `SeedState` non-zero wall-tick test hardening

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveTimeControllerTests.cs`

**Added:** `SeedState_NonZeroWallTicks_ArePreservedAfterUpdate` — seeds `TotalWallTicks = 987_654_321L`, calls `Update()` with a 1 ms tick advance, asserts `TotalWallTicks ∈ [seed, seed + 10ms_ticks)`. Proves that the PLL is bypassed and the seeded baseline is preserved.

### A.3 — `SurvivingNodes` / per-node `NodeOpCommand` isolation  

**Decision: Explicit justified deferral to CGF-1-BATCH-08.**

**Justification:** Parts A.1–A.2 and the full CGF1-S0204 wall-tick barrier refactor (including `SwitchTimeModeEvent` struct overhaul, coordinator/listener migration, `DistributedPauseTests` updates across 7 test cases, NetworkDemo adapter fixes, `TimeNetworkModule` creation) consumed the batch's estimated 22–30 h capacity. The keyed-topic ADR for per-node `NodeOpCommand` isolation (Phase 2) is a non-trivial DDS schema design that deserves its own focused spike and test strategy.

**DEBT-TRACKER row updated:** Target Fix moved to `CGF-1-BATCH-08`.

### A.4 — DEBT-TRACKER

- Closed the two BATCH-07 rows for A.1 (SteppedMasterController correctness) and A.2 (slave wall-tick test).
- Updated A.3 `SurvivingNodes` row: Target Fix`→ CGF-1-BATCH-08` with deferral note.

---

## Part B — CGF1-S0204: Future Barrier Implementation

### Files Changed

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | `SwitchTimeModeEvent`: removed `BarrierFrame`, `FrameNumber`, `TotalTime`, `FixedDeltaSeconds`; added `BarrierWallTicks` (long), `FixedDelta` (float); added DDS note in XML |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeConfig.cs` | Removed `PauseBarrierFrames`; added `LookaheadWallTicks` (default ≈ 200 ms × Stopwatch.Frequency) |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs` | Full rewrite: `_pendingBarrierFrame` → `_pendingBarrierWallTicks`; `SwitchToDeterministic` uses `TotalWallTicks + LookaheadWallTicks`; `Update()` checks `TotalWallTicks ≥ barrier`; `HandleModeSwitch` delegates `BarrierWallTicks=0` to `SwitchToDeterministic` (backward-compat for external "initiate" events) |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs` | `_pendingBarrierFrame` → `_pendingBarrierWallTicks`; all checks use `TotalWallTicks` not `FrameNumber`; uses `evt.FixedDelta` |
| `FDP/Toolkits/FDP.Toolkit.Time/FDP.Toolkit.Time.csproj` | Added `ModuleHost.Network.Cyclone` project reference |
| `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` | **New file** — static registration helper; `RegisterTranslators(DdsParticipant)` returns a configured `BlitEventTranslator<SwitchTimeModeEvent>` for composition-root wiring |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/FutureBarrierTests.cs` | **New file** — 5 CGF1-S0204 success-condition tests |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/DistributedPauseTests.cs` | Updated all `PauseBarrierFrames` → `LookaheadWallTicks`; fixed `SwitchEvent_PropagatesBarrierFrameCorrectly` to assert `BarrierWallTicks > current`; restored and rewrote `RapidPauseUnpause_BeforeBarrier_HandlesSafely` (was partially damaged by multi-replace) |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Components/TimeModeComponent.cs` | `BarrierFrame`→`BarrierWallTicks`, `FixedDeltaSeconds`→`FixedDelta` |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/TimeSyncSystem.cs` | Updated field references |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/TimeInputSystem.cs` | `BarrierFrame = 0` → `BarrierWallTicks = 0` |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/PacketBridgeSystem.cs` | `FixedDeltaSeconds` → `FixedDelta` on `TimeModeComponent` |
| `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs` | `PauseBarrierFrames = 10` → `LookaheadWallTicks = 200ms` |
| `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/Scenarios/AdvancedTests.cs` | `BarrierFrame = 0` → `BarrierWallTicks = 0` |

### DDS Annotation Note

The design spec requests `[DdsTopic]` and `[DdsIdlFile("bdc-time")]` on `SwitchTimeModeEvent`. Adding these caused the CycloneDDS IDL code-generator to fail resolving `ModuleHost::Core::Time::TimeMode` as a CDR scoped name. These attributes are omitted from the struct declaration; DDS transport registration is handled at the composition root via `TimeNetworkModule.RegisterTranslators()`.

### FutureBarrierTests (5 / 5)

| Test | Verifies |
|------|---------|
| `SwitchToIsNotCalledBeforeBarrierWallTicks` | Controller NOT swapped at `BarrierWallTicks - 1` |
| `SlaveCallsSwitchToAfterBarrierWallTicks` | Slave swaps exactly once at `BarrierWallTicks` |
| `MasterCallsSwitchToAfterBarrierWallTicks` | Coordinator swaps exactly once at `BarrierWallTicks` |
| `SwitchTimeModeEvent_FieldIsBarrierWallTicks_NotFrameCounter` | Reflection: has `long BarrierWallTicks`, no `BarrierFrame` |
| `BarrierWallTicks_IsSetToFuture` | Published `BarrierWallTicks` > `TotalWallTicks` at publish time |

---

## Test Results

```
dotnet test IOS-IG-SimHost.sln --nologo --no-build
```

All test assemblies: **Passed!** (0 failures). Notable counts:
- `FDP.Toolkit.Time.Tests.dll`: 64 passed, 1 skip (pre-existing) — was 57 + 1 skip; added 7 new tests  
- `Fdp.Examples.NetworkDemo.Tests.dll`: 27 passed (unchanged count — `Deterministic_Time_Switch` fixed)
- All other assemblies unchanged and green.

---

## Success Criteria Check

- [x] Part A: stepped `TotalWallTicks` continuity + slave wall-tick test; `SurvivingNodes` row **explicitly deferred** to CGF-1-BATCH-08 with justification.
- [x] Part B: all 5 CGF1-S0204 `FutureBarrierTests` success conditions met.
- [x] Solution build clean (`0 errors`); tests green.
- [x] DEBT-TRACKER updated.
- [x] Report filed.
