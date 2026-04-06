# BATCH-07 Report

**Batch:** BATCH-07  
**Developer:** AI Developer (GitHub Copilot)  
**Date:** 2026-04-06  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| EDIT1-W002 | ✅ Complete | `ScenarioFileService` zone bundling already implemented in prior commit; `ScenarioFileServiceZoneTests` (2 tests) already present and passing. Fixed pre-existing `EditorFileOpsIntegrationTests` failure caused by camelCase JSON key mismatch. |
| EDIT1-X001 | ✅ Complete | Created `Hrot.ExCon/Adapters/ExConOrbatAdapter.cs` (150 lines); 7 tests added to `ExConAdapterTests.cs`. |
| EDIT1-X002 | ✅ Complete | Added `Hrot.UI.Common.Facades.ISpawnController` to `ExConLogic` class declaration using FQN to avoid namespace ambiguity. |
| EDIT1-X003 | ✅ Complete | Added `GetAvailableBehaviors` to `Services.IMissionEditorService`, implemented in `MissionEditorService` via `DoctrineCatalog`, delegated in `ExConMissionShim`. |

---

## 🧪 Testing Results

**Unit Tests Passed:** 462 / 462 across all relevant suites

| Test Suite | Before | After | New Tests |
|------------|--------|-------|-----------|
| `Hrot.ExCon.Tests` | 377 | 388 | 11 (X001×7, X002×1, X003×3) |
| `Hrot.ScenarioEditor.Tests` | 16 | 16 | 0 (W002 tests pre-existed) |
| `Hrot.Editor.Tests` | 57\* | 58 | 0 new; 1 pre-existing fixed |

\*Was 58 total but 1 was broken pre-batch-07 due to camelCase serialization mismatch.

**Key Test Scenarios Verified:**

- [x] `ExConAdapterTests.GetVisibleNodes_EmptyRepo_ReturnsEmptyList` — baseline
- [x] `ExConAdapterTests.GetVisibleNodes_TwoEntities_ReturnsCorrectDepths` — depth ordering
- [x] `ExConAdapterTests.GetVisibleNodes_FilterText_ExcludesNonMatchingNodes` — filter logic
- [x] `ExConAdapterTests.SelectEntity_DelegatesToLogicSelectEntity` — delegation
- [x] `ExConAdapterTests.CreateUnit_DelegatesToLogicStartPlacementMode` — delegation
- [x] `ExConAdapterTests.ToggleExpanded_TogglesLocalSetTwice_ReturnsToOriginalState` — state
- [x] `ExConAdapterTests.RequestEmbark_AndDisembark_DoNotThrow` — no-op safety
- [x] `ExConLogicSpawnControllerTests.ExConLogic_ImplementsISpawnController` — interface
- [x] `MissionEditorServiceGetBehaviorsTests.GetAvailableBehaviors_InsurgentEntity_ReturnsInsurgentDoctrines`
- [x] `MissionEditorServiceGetBehaviorsTests.GetAvailableBehaviors_EntityNotFound_ReturnsEmpty`
- [x] `MissionEditorServiceGetBehaviorsTests.GetAvailableBehaviors_InfantryEntity_ReturnsInfantryDoctrines`
- [x] `ScenarioFileServiceZoneTests.SaveScenario_WithActiveZone_WritesZoneSection` (pre-existing W002)
- [x] `ScenarioFileServiceZoneTests.SaveScenario_WithoutActiveZones_OmitsZoneSection` (pre-existing W002)

---

## 📝 Developer Insights

**Q1: What was the actual `IDerRepo` entity iteration API?**

`IDerRepo.GetAllEntities()` exists and returns `IEnumerable<IDerEntity>`. This matches the batch instructions. `IDerRepo.GetEntity(int entityId)` returns `IDerEntity?`. Both are used in `ExConOrbatAdapter` as specified.

**Q2: Did `IZoneManagerService.GetActiveZones()` return type match `HrotScenarioEnvelopeDto.Zones` type?**

Yes. `IZoneManagerService.GetActiveZones()` returns `Dictionary<string, ZoneDefinitionDto>?` which matches the `HrotScenarioEnvelopeDto.Zones` property type exactly. The `SaveScenario` method handles the null-or-empty case: `Zones = (activeZones != null && activeZones.Count > 0) ? activeZones : null`. The `WhenWritingNull` serializer option then omits the key when zones are absent.

**Q3: Did `IExConLogic` have embark/disembark methods?**

No. `IExConLogic` has no embark or disembark methods. `RequestEmbark` and `RequestDisembark` in `ExConOrbatAdapter` are implemented as no-ops that log a `FdpLog<ExConOrbatAdapter>.Warn` warning with a message "ExCon embarkation/disembarkation not yet implemented over DDS". This matches the batch spec.

**Q4: What project references needed to be added?**

None. `Hrot.Map.Definitions` was already referenced in `Hrot.ExCon.csproj`. `Hrot.UI.Common` was already referenced in `Hrot.ExCon.csproj`. No new `.csproj` references were needed.

---

## 🔧 Design Decisions

**X002 — FQN for ISpawnController**: Adding `using Hrot.UI.Common.Facades;` to `ExConLogic.cs` caused ambiguity between `Hrot.ExCon.Services.IMapPickService` / `IMissionEditorService` and their UI.Common counterparts. Used the fully qualified name `Hrot.UI.Common.Facades.ISpawnController` in the class declaration instead to avoid the conflict.

**X001 — BFS vs DFS**: Used BFS (Queue) to match the `EditorOrbatAdapter` pattern, which is more predictable for testing and avoids stack overflow on deep hierarchies. The `OrbatPanel` uses DFS with recursion and a depth cap — both approaches are valid.

**X001 — expandedNodes parameter**: `GetVisibleNodes` uses the `expandedNodes` parameter passed by the caller (the `SharedOrbatPanel`) for expansion state, consistent with how `EditorOrbatAdapter` works. The adapter also maintains an internal `_expandedNodes` field via `ToggleExpanded`, which other callers may use.

**X001 — IsPendingDelete**: Set via `_logic.IsEntityPendingDelete(entityId)` to match the `OrbatPanel` pattern that grays out deleting entities.

**X003 — Added `GetAvailableBehaviors` to `Services.IMissionEditorService`**: The task spec said to update `MissionEditorService.cs` but `Services.IMissionEditorService` didn't have the method. Added the method declaration to the internal interface so `MissionEditorService` can implement it and `ExConMissionShim` can delegate properly. This is the correct layered approach.

**W002 — Pre-existing EditorFileOpsIntegration test fix**: The test `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` was broken before this batch because `ScenarioFileService.SaveScenario` was already updated to use `HrotSerializerOptions.HrotJsonOptions` (camelCase), but the test still used PascalCase `"Header"` and `"Entities"` property names. Fixed to use `"header"` and `"entities"` (and `"subsystemType"`).

---

## ⚠️ Outstanding Issues / Next Steps

- `ExConOrbatAdapter.RequestEmbark` and `RequestDisembark` are no-ops pending Phase 7 DDS embarkation support.
- `ExConSpawnShim` in `ExConPanelAdapters.cs` remains as a temporary shim; X002 means `ExConLogic` now directly implements `ISpawnController`, so the shim can be replaced in BATCH-08.
- No ECS imports in `Hrot.ExCon/Adapters/` — verified by code inspection.

---

## 📁 Files Created

- `Hrot.ExCon/Adapters/ExConOrbatAdapter.cs` (new)
- `Hrot.ExCon.Tests/Adapters/ExConAdapterTests.cs` (new)
- `.dev/edit-1/reports/BATCH-07-REPORT.md` (this file)

## 📁 Files Modified

- `Hrot.ExCon/ExConLogic.cs` — added `Hrot.UI.Common.Facades.ISpawnController` to class declaration
- `Hrot.ExCon/Services/IMissionEditorService.cs` — added `GetAvailableBehaviors(long entityId)` declaration
- `Hrot.ExCon/Services/MissionEditorService.cs` — added `using Hrot.Map.Definitions.Tkb;`, implemented `GetAvailableBehaviors`
- `Hrot.ExCon/ExConPanelAdapters.cs` — updated `ExConMissionShim.GetAvailableBehaviors` to delegate to `_inner`
- `Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs` — fixed pre-existing camelCase JSON key mismatch
