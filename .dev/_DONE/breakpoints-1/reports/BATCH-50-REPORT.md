# BATCH-50 Report

**Batch:** BATCH-50
**Status:** COMPLETE
**Build:** `dotnet build IOS-IG-SimHost.sln -v quiet` — Build succeeded, 5 pre-existing warnings (CS0618 `IBlueprintTimeController` obsolete), 0 errors.

---

## Tasks Completed

| Task | Description | Status |
|------|-------------|--------|
| CT-1 (D-BP-03) | Null asset guard in gutter renderers | DONE |
| CT-2 (D-BP-05) | Coordinator event subscription wiring test (Test 19 + 20) | DONE |
| P11T3 | Enforce `DataBreakpointSystem` ordering after `RecorderTickSystem` | DONE |
| P11T4 | `OnHit` re-entrancy guard + `ReentrancyTests.cs` | DONE |
| P11T5 | `PausedTick` uses `GlobalTime.TotalWallTicks` + 3 new `TemporalStatusBannerTests` | DONE |
| P11T6 | `OnExternalHit` fallback removal + 2 new `ExternalHitTagTests` | DONE |
| P11T11 | Reusable hits buffer in `EvaluateStatefulBreakpoints` + 1 new `StatefulTest` | DONE |
| P11T12 | `OccurrenceThreshold` validation + `DESIGN.md` updates + 2 new `DataBreakpointManagerTests` | DONE |

---

## Files Changed

### Production code

| File | Change |
|------|--------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Re-entrancy guard in `OnHit`; `_pausedTick` widened to `long`; `OnHit` reads `GlobalTime.TotalWallTicks`; `OnExternalHit` fallback removed; `_statefulHitsBuffer` field added; `EvaluateStatefulBreakpoints` reuses buffer; `AddBreakpoint` throws on `occurrenceThreshold < 1` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` | `PausedTick` type `uint` → `long`; `occurrenceThreshold` default 0 → 1; XML-doc updated |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs` | `using Fdp.Toolkit.Replay;` added; `[UpdateAfter(typeof(RecorderTickSystem))]` attribute added; class docstring updated |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs` | `CountManagerBreakpoints()`: added `_asset is null` guard; `Render()`: added `_asset is null` guard |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs` | `CountBreakpoints()`: added `_asset is null` guard; `Render()`: added `_asset is null` guard |
| `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` | Added `internal void RaiseReloadBeginForTest()` test seam |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | `SetBreakpoint`: now registers `ExternalHitTagPredicateDto` in `_dataBreakpointManager`; `ClearBreakpoint`/`ClearAllBreakpoints`: now unregisters from manager; `SetDataBreakpointManager`: reconciles existing session BPs with new manager; added `_mgrBpIds` dictionary |
| `.dev/breakpoints-1/DESIGN.md` | §6.2: `OccurrenceThreshold` comment updated; §9 `OnPauseStateChanged` signature corrected; §13.5: threshold semantics updated |

### Test code

| File | Change |
|------|--------|
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs` | Added `using System.Reflection;` and `using Fdp.Toolkit.Replay;`; added **Test 19** (`HotReload_CoordinatorOnReloadBegin_PropagatesViaSub_ToManager`); added **Test 20** (`RecorderRunsBeforeBreakpointSystem_AttributePresent`) |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ReentrancyTests.cs` | New file with 2 tests: `OnHit_SecondHitInSameTick_DoesNotOverwritePostTickSnapshot`, `EvaluateStatefulBreakpoints_MultipleHits_PausesOnce` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TemporalStatusBannerTests.cs` | Added `using System.Linq;`; added `[Collection("ComponentRegistry")]`; added 3 new tests: `PausedTick_ReflectsGlobalTimeTotalWallTicks`, `BannerShowsWallClockTickNotVersionCounter`, `PausedTick_FallbackToRepoVersion_WhenGlobalTimeNotRegistered` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ExternalHitTagTests.cs` | Updated test 6 (`ExternalHitTag_NoMatchingBreakpoint_StillPausesViaFallback` → `OnExternalHit_NoTagMatch_DoesNotPause`) to assert the fallback is gone; added new test `OnExternalHit_TagMatch_StillPausesAndRewinds` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointSystemStatefulTests.cs` | Added 1 new test: `StatefulEvaluation_HitsBuffer_IsReusedAcrossCalls` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` | Added 2 new tests: `AddBreakpoint_ThresholdZero_Throws`, `AddBreakpoint_ThresholdOne_IsDefault_PausesOnFirstHit` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreeBreakpointWiringTests.cs` | Stub `PausedTick`: `uint` → `long`; added assertion in `BTree_GutterRenderer_ManagerWired_IsReady` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmBreakpointWiringTests.cs` | Stub `PausedTick`: `uint` → `long`; added assertion in `Hsm_GutterRenderer_ManagerWired_IsReady` |

---

## Deviations from Instructions

### 1. `BlueprintDebugSession` updated (unplanned)

**Deviation:** Removing the `OnExternalHit` fallback (P11T6) broke the existing `Blueprint_NodeBP_RoutesToManager_TripleBufferRewindApplied` test. The fallback was the only mechanism that allowed `BlueprintDebugSession.HandleBreakpointHit → OnExternalHit` to trigger a manager pause when no `ExternalHitTagPredicateDto` was registered.

**Fix:** Updated `BlueprintDebugSession.SetBreakpoint` to register an `ExternalHitTagPredicateDto` in the `DataBreakpointManager` (when wired), so `OnExternalHit(nodeId, entity)` can now find the matching tag. `ClearBreakpoint`, `ClearAllBreakpoints`, and `SetDataBreakpointManager` updated accordingly with `_mgrBpIds` tracking dictionary.

### 2. `AiHotReloadCoordinator.RaiseReloadBeginForTest()` added (unplanned)

**Deviation:** Test 19 originally used `subsystem.AiCoordinator!.OnReloadBegin?.Invoke()` which is a C# error (CS0079: events cannot be invoked from outside the declaring type).

**Fix:** Added `internal void RaiseReloadBeginForTest()` test seam to `AiHotReloadCoordinator`. This is an internal method accessible only to the integration test project (via existing `InternalsVisibleTo`).

### 3. ExternalHitTagTests test 6 updated instead of added

**Deviation:** The existing test 6 (`ExternalHitTag_NoMatchingBreakpoint_StillPausesViaFallback`) tested the OLD fallback behavior that P11T6 removed. The batch instructions called for adding `OnExternalHit_NoTagMatch_DoesNotPause`.

**Fix:** Replaced test 6 in-place with `OnExternalHit_NoTagMatch_DoesNotPause` (same intent, corrected assertion for new behavior). Added `OnExternalHit_TagMatch_StillPausesAndRewinds` as a new test.

---

## Test Results

### `Hrot.Diagnostics.Breakpoints.Tests`
```
Passed!  - Failed: 0, Passed: 113, Skipped: 0, Total: 113
```

### `Hrot.ClusterRunner.Integration.Tests` (BreakpointSubsystemWiring filter)
```
Passed!  - Failed: 0, Passed: 20, Skipped: 0, Total: 20
```

### `Hrot.BTree.Editor.Tests`
```
Passed!  - Failed: 0, Passed: 167, Skipped: 0, Total: 167
```

### `Hrot.Hsm.Editor.Tests`
```
Passed!  - Failed: 0, Passed: 192, Skipped: 0, Total: 192
```

---

## Tracker Updates

- **TASK-TRACKER.md:** P11T3, P11T4, P11T5, P11T6, P11T11, P11T12 marked `[x]`
- **DEBT-TRACKER.md:** D-BP-03, D-BP-05 → RESOLVED
