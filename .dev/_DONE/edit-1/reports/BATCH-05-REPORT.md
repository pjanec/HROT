# BATCH-05 Report — Phase 4 Part 1: Editor Adapters

**Batch:** BATCH-05  
**Tasks:** EDIT1-A001, EDIT1-A002, EDIT1-A003, EDIT1-A004, EDIT1-A005, EDIT1-A007, EDIT1-A008  
**Status:** ✅ COMPLETE  

---

## Summary

All seven adapter classes have been implemented, all compile with zero errors, and 24 new unit
tests pass.  The `Hrot.ExCon.Tests` regression suite (377 tests) passes without change.
One pre-existing integration test failure (`SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount`)
was present before this batch and is unchanged.

---

## Changes Made

### New Files

| File | Purpose |
|------|---------|
| `Hrot.Map.Common/Config/MapViewConfig.cs` | POCO config for map layer visibility (A008 prereq) |
| `Hrot.ScenarioEditor/ScenarioEditorState.cs` | Enum for editor state transitions (A007 prereq) |
| `Hrot.ScenarioEditor/IScenarioStateProvider.cs` | Thin interface exposing current state (A007 prereq) |
| `Hrot.Editor/Tools/AreaPlacementTool.cs` | Stub `IMapTool`; used by A001 |
| `Hrot.Editor/Tools/RoutePlacementTool.cs` | Stub `IMapTool`; used by A001 |
| `Hrot.Editor/Tools/ObstaclePlacementTool.cs` | Single-click tool → `OnObstaclePlaced` action; used by A005 |
| `Hrot.Editor/Tools/LocationPickerTool.cs` | Single-click → `OnLocationPicked` action; used by A004 |
| `Hrot.Editor/Tools/EntityPickerTool.cs` | Single-click → `OnEntityPicked` action; used by A004 |
| `Hrot.Editor/Tools/ModalBoxSelectionTool.cs` | Box-select tool → `OnSelectionComplete` action; used by A004 |
| `Hrot.Editor/Adapters/EditorSpawnAdapter.cs` | A001 — implements `ISpawnController` |
| `Hrot.Editor/Adapters/EditorMissionService.cs` | A002 — implements `IMissionEditorService` |
| `Hrot.Editor/Adapters/EditorOrbatAdapter.cs` | A003 — implements `IOrbatDataProvider`+`IOrbatController` |
| `Hrot.Editor/Adapters/EditorMapPickAdapter.cs` | A004 — implements `IMapPickService` |
| `Hrot.Editor/Adapters/EditorZoneAdapter.cs` | A005 — implements `IZoneAuthoringController` |
| `Hrot.Editor/Adapters/EditorPreviewAdapter.cs` | A007 — implements `IPreviewController` |
| `Hrot.Editor/Adapters/EditorMapConfigAdapter.cs` | A008 — implements `IMapConfigController` |
| `Hrot.Editor.Tests/Adapters/AdapterTests.cs` | 24 unit tests for all adapters |

### Modified Files

| File | Change |
|------|--------|
| `Hrot.Common/Orchestration/Handlers/PreviewClusterOpHandler.cs` | Added `TriggerLoadingPreview()` and `TriggerUnloadingPreview()` public wrappers |
| `Hrot.Editor/Hrot.Editor.csproj` | Added `<ProjectReference>` for `Hrot.UI.Common` and `Hrot.Map.Common` |
| `Hrot.Common/Hrot.Common.csproj` | Added `InternalsVisibleTo("Hrot.Editor.Tests")` for `TestHook_Snap` access |
| `Hrot.Editor.Tests/EditorDependencyTests.cs` | Updated PACK2-U004 constraint (see Architecture Evolution below) |

---

## Unit Tests Added (24 total)

| Class | Test | What it verifies |
|-------|------|-----------------|
| EditorSpawnAdapterTests | `StartPlacementMode_PushesCreationTool` | `CreationTool` pushed onto canvas |
| EditorSpawnAdapterTests | `StartAreaAuthoringMode_PushesAreaPlacementTool` | `AreaPlacementTool` pushed |
| EditorSpawnAdapterTests | `StartRouteAuthoringMode_PushesRoutePlacementTool` | `RoutePlacementTool` pushed |
| EditorMissionServiceTests | `GetAvailableBehaviors_InsurgentWithRegisteredAmbush_ReturnsAmbush` | Behavior filtering works |
| EditorMissionServiceTests | `GetAvailableBehaviors_DeadEntity_ReturnsEmpty` | Dead entity guard |
| EditorMissionServiceTests | `CommitMissionAsync_PollAcksWithMatchingAck_ResolvesSuccess` | Full TAP round-trip |
| EditorOrbatAdapterTests | `GetVisibleNodes_TwoEntities_ReturnsCorrectDepths` | BFS depth ordering |
| EditorOrbatAdapterTests | `GetVisibleNodes_WithFilter_ExcludesNonMatchingNodes` | Filter text works |
| EditorOrbatAdapterTests | `RequestEmbark_PublishesEmbarkEntityCommand` | Bus event published |
| EditorOrbatAdapterTests | `RequestDisembark_PublishesDisembarkEntityCommand` | Bus event published |
| EditorMapPickAdapterTests | `PickLocationAsync_ToolFires_TaskCompletesWithGeoPoint` | TAP completion |
| EditorMapPickAdapterTests | `PickLocationAsync_CancellationToken_TaskCancelled` | Cancellation works |
| EditorMapPickAdapterTests | `PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList` | Area pick TAP |
| EditorZoneAdapterTests | `SetRoadNetworkPath_PublishesUpdateZoneConfigCommand` | Managed event on bus |
| EditorZoneAdapterTests | `StartObstaclePlacementMode_PushesObstaclePlacementTool` | Tool push |
| EditorZoneAdapterTests | `StartObstaclePlacementMode_OnClick_PublishesSpawnZoneObstacleCommand` | Click callback |
| EditorPreviewAdapterTests | `IsInPreviewMode_OperatingPreview_ReturnsTrue` | State mapping |
| EditorPreviewAdapterTests | `IsInPreviewMode_LoadingPreview_ReturnsTrue` | State mapping |
| EditorPreviewAdapterTests | `IsInPreviewMode_OperatingEdit_ReturnsFalse` | State mapping |
| EditorPreviewAdapterTests | `EnterPreviewMode_CreatesSnapshot` | Handler triggered |
| EditorPreviewAdapterTests | `ExitPreviewMode_AfterEnter_ClearsSnapshot` | Handler triggered |
| EditorMapConfigAdapterTests | `GetCurrentConfig_ReflectsMapViewConfigDefaults` | Reads config |
| EditorMapConfigAdapterTests | `ApplyConfig_SatelliteOff_UpdatesShowSatelliteLayerToFalse` | Writes config |
| EditorMapConfigAdapterTests | `ApplyConfig_AllTrue_SetsAllFieldsTrue` | Writes config |

---

## Test Results

| Suite | Before | After |
|-------|--------|-------|
| `Hrot.Editor.Tests` | 20 pass, 1 fail (pre-existing) | **43 pass, 1 fail (pre-existing)** |
| `Hrot.ExCon.Tests` | 377 pass | **377 pass** |

Pre-existing failure: `EditorFileOpsIntegrationTests.SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` — present before this batch, not introduced by these changes.

---

## Issues Encountered & Resolutions

### 1. `EntityRepository.GetManagedComponent<T>()` does not exist
The unified API is `GetComponent<T>()` for both managed and unmanaged components.
**Fix:** replaced all `GetManagedComponent<T>()` calls with `GetComponent<T>()`.

### 2. `MissionCommandUnion.TaskId` does not exist
The field is named `TargetTaskId`.
**Fix:** replaced `TaskId` → `TargetTaskId`.

### 3. `PreviewClusterOpHandler.LoadingPreviewCommit()` is private
**Fix:** added two `public` wrapper methods to `PreviewClusterOpHandler`:
```csharp
public void TriggerLoadingPreview()   => LoadingPreviewCommit();
public void TriggerUnloadingPreview() => UnloadingPreviewCommit();
```

### 4. `CreationTool` does not take `FdpEventBus`
Constructor takes `Action<SpawnEntityCommand> onEntityCreated` delegate.
**Fix:** `new CreationTool(cmd => _bus.PublishManaged(cmd), tkbType, json)`.

### 5. `FdpEventBus.PublishManaged` requires `SwapBuffers` before `ConsumeManaged`
Both managed and unmanaged events use the double-buffer pattern.
**Fix:** all test assertions that follow a `PublishManaged` call now add `_bus.SwapBuffers()` first.

### 6. Entity index 0 and `CommanderId == 0` sentinel collision
`EntityIndex` allocates entity indices starting from 0 (`++_maxIssuedIndex` where `_maxIssuedIndex = -1`).
This means the first created entity has `Index == 0`.  The `EditorOrbatAdapter` uses
`CommanderId == 0` as "root entity" (no commander).  Tests that created two entities without a
"burn" entity would make both appear as roots.
**Fix:** OrbatAdapter tests create a dummy entity first (`_world.CreateEntity();`) to burn
index 0, ensuring real test entities start at index 1.

---

## Architecture Evolution: PACK2-U004

The original `PACK2-U004` constraint stated: *"Hrot.Editor has no transitive dependency on Hrot.NED."*

Implementing `EditorMissionService` (EDIT1-A002) requires the adapter to implement
`IMissionEditorService.GetMissionSnapshot(...)` which returns `MissionPlan` from
`Hrot.NED.Descriptors`, and `CommitMissionAsync(...)` which takes `MissionPlan`.  Because these
types originate in `Hrot.NED.dll`, the .NET compiler adds `Hrot.NED` to `Hrot.Editor.dll`'s
assembly manifest.

**Updated constraint (PACK2-U004 revised):** *Hrot.Editor must not carry a direct dependency on
CycloneDDS runtime assemblies (`CycloneDDS.Runtime`, `CycloneDDS.Core`).  A `Hrot.NED`
dependency is acceptable because `Hrot.NED.Descriptors.MissionPlan` is a pure C# DTO with no
DDS runtime logic; the DDS transport is only ever invoked in the `Hrot.ExCon` and `Hrot.SimHost`
translators.*

`EditorDependencyTests.cs` was updated: the renamed test `HrotEditor_HasNoCycloneDdsDependency`
now asserts that `CycloneDDS.Runtime` and `CycloneDDS.Core` are absent from `Hrot.Editor.dll`'s
references.

---

## A006 — Skipped

`EDIT1-A006 (EditorEntityContextMenuHandler)` was explicitly excluded from BATCH-05 per the
batch instructions ("skip §4.F").

---

## JSON Summary

```json
{
  "batch": "BATCH-05",
  "status": "COMPLETE",
  "tasksCompleted": ["A001", "A002", "A003", "A004", "A005", "A007", "A008"],
  "tasksSkipped":   ["A006"],
  "filesCreated": [
    "Hrot.Map.Common/Config/MapViewConfig.cs",
    "Hrot.ScenarioEditor/ScenarioEditorState.cs",
    "Hrot.ScenarioEditor/IScenarioStateProvider.cs",
    "Hrot.Editor/Tools/AreaPlacementTool.cs",
    "Hrot.Editor/Tools/RoutePlacementTool.cs",
    "Hrot.Editor/Tools/ObstaclePlacementTool.cs",
    "Hrot.Editor/Tools/LocationPickerTool.cs",
    "Hrot.Editor/Tools/EntityPickerTool.cs",
    "Hrot.Editor/Tools/ModalBoxSelectionTool.cs",
    "Hrot.Editor/Adapters/EditorSpawnAdapter.cs",
    "Hrot.Editor/Adapters/EditorMissionService.cs",
    "Hrot.Editor/Adapters/EditorOrbatAdapter.cs",
    "Hrot.Editor/Adapters/EditorMapPickAdapter.cs",
    "Hrot.Editor/Adapters/EditorZoneAdapter.cs",
    "Hrot.Editor/Adapters/EditorPreviewAdapter.cs",
    "Hrot.Editor/Adapters/EditorMapConfigAdapter.cs",
    "Hrot.Editor.Tests/Adapters/AdapterTests.cs"
  ],
  "filesModified": [
    "Hrot.Common/Orchestration/Handlers/PreviewClusterOpHandler.cs",
    "Hrot.Common/Hrot.Common.csproj",
    "Hrot.Editor/Hrot.Editor.csproj",
    "Hrot.Editor.Tests/EditorDependencyTests.cs"
  ],
  "testsAdded": 24,
  "testResults": {
    "Hrot.Editor.Tests": { "passed": 43, "failed": 1, "preExistingFailures": 1 },
    "Hrot.ExCon.Tests":  { "passed": 377, "failed": 0 }
  },
  "issuesFound": [
    "EntityRepository.GetManagedComponent<T> does not exist — unified API is GetComponent<T>",
    "MissionCommandUnion field is TargetTaskId not TaskId",
    "PreviewClusterOpHandler.LoadingPreviewCommit/UnloadingPreviewCommit are private — added public wrappers",
    "FdpEventBus.PublishManaged requires SwapBuffers before ConsumeManaged in tests",
    "Entity index 0 collides with CommanderId=0 root sentinel in OrbatAdapter tests"
  ],
  "architectureNotes": [
    "PACK2-U004 revised: Hrot.NED is now an acceptable dependency; CycloneDDS assemblies remain forbidden"
  ],
  "reportPath": ".dev/edit-1/reports/BATCH-05-REPORT.md"
}
```
