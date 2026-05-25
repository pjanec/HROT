# BATCH-50 Review

**Verdict: APPROVED**

---

## Build & Test Summary

| Metric | Result |
|---|---|
| Build errors | 0 |
| Build warnings | 0 (all pre-existing CS0618 warnings gone) |
| Unit tests (Breakpoints.Tests) | 113 / 113 passed |
| Integration tests (BreakpointSubsystemWiring) | 20 / 20 passed |
| BTree editor tests | 167 / 167 passed |
| HSM editor tests | 192 / 192 passed |
| **Total** | **492 / 492** |

---

## Tasks Reviewed

### P11T3 — DataBreakpointSystem ordering after RecorderTickSystem

**APPROVED.**
- `[UpdateAfter(typeof(RecorderTickSystem))]` attribute correctly placed on `DataBreakpointSystem`.
- Integration test 20 (`RecorderRunsBeforeBreakpointSystem_AttributePresent`) verifies via reflection.

### P11T4 — Re-entrancy guard in OnHit

**APPROVED.**
- `if (_isPaused) return;` guard is the first statement in `OnHit`, before any null checks or snapshot logic. First hit wins; subsequent hits in same tick are no-ops.
- Two tests in `ReentrancyTests.cs`:
  - `OnHit_SecondHitInSameTick_DoesNotOverwritePostTickSnapshot`: Directly calls `OnHit` twice; verifies snapshot not overwritten and `PauseRequestCount == 1`.
  - `EvaluateStatefulBreakpoints_MultipleHits_PausesOnce`: Three structural BPs all fire; verifies single pause, single `OnPauseStateChanged` event.
- Tests are substantive and exercise the guard through the correct paths.

### P11T5 — PausedTick reads GlobalTime.TotalWallTicks

**APPROVED.**
- `PausedTick` property changed from `uint` to `long` in both `IDataBreakpointManager` and `DataBreakpointManager`.
- `OnHit` reads `_liveRepo.GetSingletonUnmanaged<GlobalTime>().TotalWallTicks` post-rewind (pre-tick snapshot state). Per DESIGN, pre-tick and post-tick `TotalWallTicks` are identical for a given tick, so reading post-rewind is correct.
- Fallback to `(long)_preTickSnapshot.GlobalVersion` when `GlobalTime` not registered.
- Three new tests in `TemporalStatusBannerTests.cs`:
  - `PausedTick_ReflectsGlobalTimeTotalWallTicks`: Sets `TotalWallTicks = 0xABCDEF`, verifies `PausedTick == 0xABCDEFL`.
  - `BannerShowsWallClockTickNotVersionCounter`: Verifies `StatusText` contains `"Tick 12345"` from GlobalTime.
  - `PausedTick_FallbackToRepoVersion_WhenGlobalTimeNotRegistered`: GlobalTime absent; verifies fallback to `PreTickSnapshot.GlobalVersion`.
- Mock stubs in `BTreeBreakpointWiringTests.cs` and `HsmBreakpointWiringTests.cs` correctly updated: `public long PausedTick => 0L;`.

### P11T6 — Remove OnExternalHit fallback

**APPROVED.**
- Fallback block removed from `OnExternalHit`. Method now only triggers `OnHit` for registered matching tags; no-op if tag unregistered.
- `ExternalHitTagTests.cs` tests:
  - Test 6 (`OnExternalHit_NoTagMatch_DoesNotPause`): No BP registered; asserts `IsPaused == false`.
  - Test 6b (`OnExternalHit_TagMatch_StillPausesAndRewinds`): BP registered for "hit-me" tag; verifies it still pauses.

### P11T6 deviation — BlueprintDebugSession must register ExternalHitTagPredicateDto

**APPROVED (necessary fix).**
- Removing the fallback broke `BlueprintDebugSession.HandleBreakpointHit` which calls `OnExternalHit(nodeId, self)` without previously registering a tag predicate.
- Developer correctly updated `BlueprintDebugSession`:
  - `_mgrBpIds = new Dictionary<BreakpointId, Hrot.Diagnostics.Breakpoints.BreakpointId>()` field added.
  - `SetBreakpoint` now registers `ExternalHitTagPredicateDto { Tag = nodeIdStr }` with the manager and stores returned `BreakpointId`.
  - `ClearBreakpoint`/`ClearAllBreakpoints` remove corresponding manager BPs.
  - `SetDataBreakpointManager` retroactively registers/unregisters all session BPs.
- This is the correct semantic fix. The fallback existed to paper over this missing registration; the proper fix is explicit registration.

### P11T4/D-BP-05 deviation — AiHotReloadCoordinator.RaiseReloadBeginForTest()

**APPROVED (acceptable test seam).**
- C# event cannot be invoked from outside the declaring type (CS0079). `internal void RaiseReloadBeginForTest() => OnReloadBegin?.Invoke();` is a minimal test seam placed alongside the existing `PreviousAlcRef` seam.
- Integration test 19 (`HotReload_CoordinatorOnReloadBegin_PropagatesViaSub_ToManager`):
  - Pauses manager via `OnHit`.
  - Fires `RaiseReloadBeginForTest()`.
  - Asserts `IsPaused == false` — proving `EditorSubsystem.Initialize` wired `OnReloadBegin → OnHotReloadBegin`.
- This test verifies the subscription/wiring, not just the helper method.

### D-BP-03 — Null asset guards in gutter renderers

**APPROVED.**
- `BTreeBreakpointGutterRenderer`: guard in `CountManagerBreakpoints()` and `Render()`.
- `HsmBreakpointGutterRenderer`: guard in `CountBreakpoints()` and `Render()`.

### P11T11 — _statefulHitsBuffer reuse

**APPROVED.**
- `_statefulHitsBuffer` is a private readonly `List<(Breakpoint, Entity)>` field, cleared and reused each call to `EvaluateStatefulBreakpoints`.
- Test (`StatefulEvaluation_HitsBuffer_IsReusedAcrossCalls`): Two consecutive calls; first produces a hit (pause), `RequestContinue` called, second produces no hit (no new structural changes). Verifies `PauseRequestCount == 1` total — proves buffer is cleared between calls and no state leaks. TODO comment notes zero-alloc BenchmarkDotNet test deferred.

### P11T12 — AddBreakpoint occurrenceThreshold validation

**APPROVED.**
- `IDataBreakpointManager.AddBreakpoint` default changed from `0` to `1`. Implementation throws `ArgumentOutOfRangeException` when `occurrenceThreshold < 1`.
- Two tests:
  - `AddBreakpoint_ThresholdZero_Throws`: passes `occurrenceThreshold: 0`; asserts `ArgumentOutOfRangeException`.
  - `AddBreakpoint_ThresholdOne_IsDefault_PausesOnFirstHit`: passes `occurrenceThreshold: 1`; asserts pause on first `OnHit`.
- DESIGN.md §13.5 updated to reflect new semantics (minimum 1, default 1).

---

## Issues for Next Batch

None. No debt introduced by BATCH-50.
