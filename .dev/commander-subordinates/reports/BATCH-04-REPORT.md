# BATCH-04 Report

**Batch:** BATCH-04
**Tasks:** CS013, CS014, CS017, CS018, CS019, CS026, CS027
**Status:** COMPLETE ✅

---

## Tasks Completed

### CS013 — UnitSubordinateTranslator
- Created `Hrot/Subsystems/Hrot.SimHost/Serializers/UnitSubordinateTranslator.cs`
- Registered via `HrotScenarioSerializerFactory` with `.RegisterTranslator(new UnitSubordinateTranslator())`
- Tests: `UnitSubordinateTranslatorTests.cs` — 4 tests (T01–T04), all pass

### CS014 — GenesisMaterializationSystem: MaterializeUnitSubordinate
- Added `MaterializeUnitSubordinate` private method to `GenesisMaterializationSystem`
- Called from `Execute()` alongside existing materialization methods
- Implements: normal resolution, deferred retry, capacity-exceeded abort, Active lifecycle escape hatch
- Tests: `Systems/GenesisMaterializationSystemTests.cs` — CS014-T01 through T04, all pass
  - T02 fix: entity lifecycle set to `Constructing` so escape-hatch doesn't fire on "retry" test case

### CS017 — OrbatNodeViewModel: CanAcceptSubordinates
- Added `bool CanAcceptSubordinates` as 6th positional parameter to the record in BOTH:
  - `Hrot/Engine/Hrot.Presentation/Models/OrbatNodeViewModel.cs`
  - `Hrot/Engine/Hrot.UI.Common/Models/OrbatNodeViewModel.cs`
- Updated all callers: `ExConOrbatAdapter.cs`, `EditorOrbatAdapter.cs`

### CS018 — IOrbatController: subordination methods
- Added `RequestAssignSubordinate(int subordinateRendererId, int commanderRendererId)` and
  `RequestRemoveSubordinate(int subordinateRendererId)` to BOTH interface copies:
  - `Hrot/Engine/Hrot.Presentation/Facades/IOrbatController.cs`
  - `Hrot/Engine/Hrot.UI.Common/Facades/IOrbatController.cs`
- Stub implementations with `FdpLog.Warn` in `ExConOrbatAdapter.cs` and `EditorOrbatAdapter.cs`

### CS019 — SharedOrbatPanel: subordination drag-drop
- Updated BOTH `SharedOrbatPanel.cs` copies:
  - Node drop target calls `HandleHierarchyDropPayload`
  - Background drop zone calls `ctrl.RequestRemoveSubordinate`
  - Added `HandleHierarchyDropPayload` internal method
- Tests: `SharedOrbatPanelTests.cs` in `Hrot.ExCon.Tests` — CS017 and CS019 tests, all pass

### CS026 — Cluster load handlers: InitialUnitSubordinateIntent drain guard
- Added drain guard to `HrotScenarioLoadHandler.cs`:
  `foreach (var _ in _world.Query().WithManaged<InitialUnitSubordinateIntent>().Build()) return;`
- Added same guard to `CgfScenarioLoadHandler.cs`
- Tests: `HrotScenarioLoadHandlerTests.cs` (CS026-T01, T02) and `CgfScenarioLoadHandlerTests.cs` (CS026-T03, T04) — all pass
  - Fix: Added `RegisterManagedComponent<InitialUnitSubordinateIntent>()` to both test constructors

### CS027 — StagingEntityExtractor: remap CommanderNetworkId on load
- Added `InitialUnitSubordinateIntent` remapping block to `StagingEntityExtractor.cs`
- Tests: `StagingEntityExtractorTests.cs` — CS027-T01 and T02, all pass

---

## Test Results

| Assembly | Passed | Failed | Skipped |
|---|---|---|---|
| `Hrot.SimHost.Tests.dll` | 500 | 2 (pre-existing `MissionPlanTranslatorTests`) | 3 |
| `Hrot.ExCon.Tests.dll` | 317 | 0 | 0 |

**New tests added this batch:** ~20 tests across CS013, CS014, CS026, CS027

---

## Build

`Build succeeded. 0 Error(s)` — `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`

---

## Notes

- `MissionPlanTranslatorTests` (2 failures) are pre-existing failures unrelated to this batch.
- Integration tests (`Hrot.ClusterRunner.Integration.Tests`) are excluded from batch scope per workflow norms.
