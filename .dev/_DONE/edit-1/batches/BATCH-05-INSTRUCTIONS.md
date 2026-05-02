# BATCH-05: Phase 4 Part 1 — Editor Adapters (`EditorSpawnAdapter`, `EditorMissionService`, `EditorOrbatAdapter`, `EditorMapPickAdapter`, `EditorZoneAdapter`, `EditorPreviewAdapter`, `EditorMapConfigAdapter`)

**Batch Number:** BATCH-05  
**Tasks:** EDIT1-A001, EDIT1-A002, EDIT1-A003, EDIT1-A004, EDIT1-A005, EDIT1-A007, EDIT1-A008  
**Phase:** Phase 4 — Hrot.Editor Adapters (Part 1)  
**Estimated Effort:** 9–11 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (Port interfaces), BATCH-03 (panels), BATCH-04 (domain events) ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch creates **seven Editor-specific adapter classes** in `Hrot.Editor/Adapters/`.  
Each adapter implements one or more Port interfaces from `Hrot.UI.Common.Facades` using  
the Editor's concrete infrastructure (ECS/FdpEventBus), with zero DDS coupling.

Work task-by-task. All adapters must compile with zero errors before this batch is done.  
Unit tests are required for each adapter (logic-level, not render-level).

Do NOT stop to ask questions. Work autonomously to completion.

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Design:** `.dev/edit-1/DESIGN.md` §Phase 4 (§4.A–4.H, skip §4.F and §4.I–§4.L — those are in BATCH-06)
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A001, §EDIT1-A002, §EDIT1-A003, §EDIT1-A004, §EDIT1-A005, §EDIT1-A007, §EDIT1-A008
4. **Previous reports:** `.dev/edit-1/reports/BATCH-04-REPORT.md`

### Source Code Locations

**New files to create (all in `Hrot.Editor/Adapters/`):**

| File | Implements |
|------|-----------|
| `Hrot.Editor/Adapters/EditorSpawnAdapter.cs` | `ISpawnController` |
| `Hrot.Editor/Adapters/EditorMissionService.cs` | `IMissionEditorService` |
| `Hrot.Editor/Adapters/EditorOrbatAdapter.cs` | `IOrbatDataProvider` + `IOrbatController` |
| `Hrot.Editor/Adapters/EditorMapPickAdapter.cs` | `IMapPickService` |
| `Hrot.Editor/Adapters/EditorZoneAdapter.cs` | `IZoneAuthoringController` |
| `Hrot.Editor/Adapters/EditorPreviewAdapter.cs` | `IPreviewController` |
| `Hrot.Editor/Adapters/EditorMapConfigAdapter.cs` | `IMapConfigController` |

**New tool helper files:**

| File | Used by |
|------|---------|
| `Hrot.Editor/Tools/ModalBoxSelectionTool.cs` | `EditorMapPickAdapter.PickAreaEntitiesAsync` |
| `Hrot.Editor/Tools/ObstaclePlacementTool.cs` | `EditorZoneAdapter.StartObstaclePlacementMode` |

**Existing files to reference:**

| File | Purpose |
|------|---------|
| `Hrot.ScenarioEditor/Tools/CreationTool.cs` | Pattern for `EditorSpawnAdapter.StartPlacementMode` |
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | Contains `IZoneManagerService` usage pattern |
| `Hrot.Editor/IEditorLogic.cs` | `IEditorLogic` interface used by `EditorOrbatAdapter` |
| `Hrot.Common/Orchestration/Handlers/PreviewClusterOpHandler.cs` | `PreviewClusterOpHandler` class (for `EditorPreviewAdapter`) |
| `FDP/Toolkits/FDP.Toolkit.Vis2D/MapCanvas.cs` | `PushTool()`, `PopTool()`, `ActiveTool` |

**Hrot.Editor.csproj** must be updated to reference `Hrot.UI.Common`.

### Key API Facts

1. **`MapCanvas.PushTool(IMapTool tool)`** — pushes a new tool onto the stack. **`MapCanvas.PopTool()`** — pops the active tool. Confirmed in `FDP/Toolkits/FDP.Toolkit.Vis2D/MapCanvas.cs`.
2. **`CreationTool`** exists in `Hrot.ScenarioEditor/Tools/CreationTool.cs`. Read it for exact constructor args.
3. **`AreaAuthoringTool` and `RouteAuthoringTool` DO NOT EXIST as classes.** `ActivateAreaAuthoringTool()` and `ActivateRouteAuthoringTool()` are methods on `IgApplication` in `Hrot.IG`. For `EditorSpawnAdapter`, create minimal stub tool classes `AreaPlacementTool` and `RoutePlacementTool` in `Hrot.Editor/Tools/` that implement `IMapTool`.  
4. **`EmbarkEntityCommand`** is in `FDP.Toolkit.Behavior.Events` (added in BATCH-04).
5. **`MapUserConfig`** lives in `Hrot.IG/Systems/MapUserConfig.cs` (namespace `Hrot.IG.Systems`) and has only 3 bool fields: `ForceHostile`, `HideLabels`, `ContinuousDragUpdates`. It does NOT have layer-visibility fields matching `MapLayerState`. Since `Hrot.Editor.csproj` does not reference `Hrot.IG`, and the existing `MapUserConfig` has no Satellite/GroundUnits/AirUnits fields:
   - **Solution:** Add a new `class MapViewConfig` in `Hrot.Map.Common/Config/MapViewConfig.cs` with:
     - `bool ShowSatelliteLayer { get; set; }` 
     - `bool ShowGroundUnits { get; set; }`
     - `bool ShowAirUnits { get; set; }`
     - `bool ShowGrid { get; set; }`
   - `EditorMapConfigAdapter` will use an injected `MapViewConfig` instance (not ECS singleton) to read/write these layer states.
6. **`MapLayerState`** record: `bool Satellite, bool GroundUnits, bool AirUnits, bool Grid` (from `Hrot.UI.Common.Models`).
7. **`IScenarioStateProvider` and `ScenarioEditorState` DO NOT EXIST** — must be created as new files:
   - Create `Hrot.ScenarioEditor/IScenarioStateProvider.cs` with `interface IScenarioStateProvider { ScenarioEditorState CurrentState { get; } }`
   - Create `Hrot.ScenarioEditor/ScenarioEditorState.cs` with `enum ScenarioEditorState { Idle, LoadingEdit, OperatingEdit, LoadingPreview, OperatingPreview, SavingEdit }`
8. **`EntityInfo.CommanderId`** component — used by `EditorOrbatAdapter` to build ORBAT tree hierarchy. Search for `EntityInfo` in ECS components to confirm field name.

### Run tests with

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build
dotnet build Hrot.Editor
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error CS" | Select-Object -Last 5

# Test
dotnet test Hrot.Editor.Tests --no-build
dotnet test Hrot.ExCon.Tests --no-build
```

---

## Context

Phase 4 adapters bridge the UI's Port interfaces to the Editor's ECS/FdpEventBus infrastructure.  
This batch focuses on the seven "lighter" adapters.  
The complex adapters (`EditorEntityContextMenuHandler`, `EditorCargoSystem`, `EditorPerceptionSetupSystem`,  
`EditorZoneAuthoringSystem`, `PerceptionMapLayer`) are handled in BATCH-06.

---

## 🎯 Batch Objectives

1. **A001** — `EditorSpawnAdapter` — translates `ISpawnController` calls to `MapCanvas.PushTool` 
2. **A002** — `EditorMissionService` — dynamic behavior filtering + TAP (Task-based async pattern) mission commits
3. **A003** — `EditorOrbatAdapter` — ECS-backed ORBAT tree + embarkation/disembarkation
4. **A004** — `EditorMapPickAdapter` — async pick via `MapCanvas` tools
5. **A005** — `EditorZoneAdapter` — publishes zone managed-events + pushes obstacle tool
6. **A007** — `EditorPreviewAdapter` — wraps `PreviewClusterOpHandler` 
7. **A008** — `EditorMapConfigAdapter` — reads/writes `MapUserConfig` ECS singleton

---

## ✅ Tasks

### Task 1: EDIT1-A001 — `EditorSpawnAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A001  
**Design:** `.dev/edit-1/DESIGN.md` §4.A

**Constructor:** `EditorSpawnAdapter(MapCanvas canvas, FdpEventBus bus)`

**Implementations:**
- `StartPlacementMode(long tkbType, string? initialPropertiesJson)` → create a `CreationTool` with the given `tkbType` and `initialPropertiesJson`, then `_canvas.PushTool(tool)`. Study `CreationTool`'s constructor in `Hrot.ScenarioEditor/Tools/CreationTool.cs` for exact args.
- `StartAreaAuthoringMode(string styleOverrideJson)` → `AreaAuthoringTool` does NOT exist as a class. Create a minimal `AreaPlacementTool : IMapTool` stub in `Hrot.Editor/Tools/AreaPlacementTool.cs`. Push it on the canvas.
- `StartRouteAuthoringMode()` → `RouteAuthoringTool` does NOT exist as a class. Create a minimal `RoutePlacementTool : IMapTool` stub in `Hrot.Editor/Tools/RoutePlacementTool.cs`. Push it on the canvas.

For `AreaPlacementTool` and `RoutePlacementTool`, implement the minimal `IMapTool` interface (inspect what methods are required via `FDP/Toolkits/FDP.Toolkit.Vis2D/Abstractions/IMapTool.cs`).

No DDS types.

**Tests (in `Hrot.Editor.Tests/`):**
1. Call `StartPlacementMode(2001, null)` with a spy canvas; assert `PushTool` was called with a `CreationTool`
2. Call `StartAreaAuthoringMode("")`; assert `PushTool` called with an area authoring tool
3. Call `StartRouteAuthoringMode()`; assert `PushTool` called with a route tool

---

### Task 2: EDIT1-A002 — `EditorMissionService`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A002  
**Design:** `.dev/edit-1/DESIGN.md` §4.B

**Constructor:** `EditorMissionService(FdpEventBus bus, EntityRepository repo, BehaviorRegistry registry)`

**Key implementations:**
- `GetAvailableBehaviors(long entityId)`:
  1. Cast `entityId` to `int`, get entity; guard alive + has `TkbIdentity`; else return `Array.Empty<string>()`
  2. Get `tkbType` from `TkbIdentity` component
  3. `var catalog = BehaviorCatalog.GetValidBehaviors(tkbType)` (from `Hrot.Map.Definitions`)
  4. Return `catalog.Where(n => registry.TryGetId(n, out _)).ToList()`
- `GetMissionSnapshot(long entityId)` → read `ActiveMissionPlan` managed component; map to `(MissionPlan?, long)`. Return `(null, 0)` if not present.
- `CommitMissionAsync` and `SendControlCommandAsync` → TAP pattern: create `TaskCompletionSource<MissionCommitResult>`, cache by `requestId`, publish `MissionControlIntent` event. Return `tcs.Task`.
- `PollAcks()` (called from Editor update loop) → consume `MissionControlAckEvent` from bus; resolve pending tasks.

**Tests:**
1. Create entity with `TkbIdentity.TkbType = TkbEntityTypes.Insurgent`; register `"Ambush"` behavior; call `GetAvailableBehaviors` → returns list containing `"Ambush"`
2. Entity not alive → `GetAvailableBehaviors` returns empty list
3. `CommitMissionAsync` → pump `PollAcks()` after injecting a matching ack event → `Task` completes with `Success = true`

---

### Task 3: EDIT1-A003 — `EditorOrbatAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A003  
**Design:** `.dev/edit-1/DESIGN.md` §4.C

**Constructor:** `EditorOrbatAdapter(EntityRepository world, FdpEventBus bus, IEditorLogic logic)`

**Key implementations:**
- `GetVisibleNodes(filterText, expandedNodes)`:
  1. Build parent-child map from `EntityInfo.CommanderId` 
  2. Walk BFS/DFS from roots (`CommanderId == 0`)
  3. Apply `filterText` on `EntityInfo.Name` (case-insensitive, `string.Empty` matches all)
  4. Map to `OrbatNodeViewModel(EntityId, Name, Depth, HasChildren, IsPendingDelete)`

- `SelectEntity(int entityId)` → `_logic.ActivateTool(EditorTool.Select)` or the appropriate IEditorLogic method
- `RequestEmbark(int passengerId, int vehicleId)` → resolve both entities; publish `EmbarkEntityCommand { Passenger = p, Vehicle = v }`
- `RequestDisembark(int passengerId)` → resolve entity; publish `DisembarkEntityCommand { Passenger = p }`
- `CreateUnit(long tkbType)` → delegate to `_logic.ActivateTool(EditorTool.Edit)` (or spawn placement — see what IEditorLogic exposes)
- `ToggleExpanded(int entityId)` → local `HashSet<int>` management

**Tests:**
1. Two ECS entities (parent + child via `EntityInfo.CommanderId`); `GetVisibleNodes("")` returns 2 nodes with correct `Depth` (0 and 1)
2. Filter text filters nodes by name
3. `RequestEmbark(1, 2)` publishes `EmbarkEntityCommand` with correct `Passenger` and `Vehicle`

---

### Task 4: EDIT1-A004 — `EditorMapPickAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A004  
**Design:** `.dev/edit-1/DESIGN.md` §4.D

**Constructor:** `EditorMapPickAdapter(MapCanvas canvas)`  
(Note: `IGeographicTransform` and `ISimulationView` may not exist yet; simplify to just `MapCanvas` if so — just use canvas world coordinates directly)

**Key implementations:**
- `PickLocationAsync(ct)` → create `LocationPickerTool` (or equivalent click-listener tool); wire `OnLocationPicked` to `TCS.TrySetResult(geoPoint)`. Push tool on canvas. Return `tcs.Task`.
  If `LocationPickerTool` doesn't exist, create a minimal `SingleClickTool` in `Hrot.Editor/Tools/` that fires a `GeoPoint` callback on click.
- `PickEntityAsync(filterPresets, ct)` → similar pattern, fires entity int id callback.
- `PickAreaEntitiesAsync(filterPresets, ct)` → push `ModalBoxSelectionTool` (new class in `Hrot.Editor/Tools/`); resolves on box-select completion.

**Tests:**
1. Call `PickLocationAsync`; simulate tool's `OnLocationPicked` callback; assert Task completes with expected `GeoPoint`
2. Cancellation token cancelled before pick → Task is cancelled; tool is popped from canvas

---

### Task 5: EDIT1-A005 — `EditorZoneAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A005  
**Design:** `.dev/edit-1/DESIGN.md` §4.E

**Constructor:** `EditorZoneAdapter(MapCanvas canvas, FdpEventBus bus)`

**Implementations:**
- `SetRoadNetworkPath(zoneName, assetPath)` → `_bus.PublishManaged(new UpdateZoneConfigCommand { ZoneName = zoneName, RoadNetworkPath = assetPath })`
- `StartObstaclePlacementMode(zoneName, radius)` → create `ObstaclePlacementTool(radius, onClickPos => _bus.PublishManaged(new SpawnZoneObstacleCommand { ZoneName = zoneName, Position = ..., Radius = radius }))`. Then `_canvas.PushTool(tool)`.
- `ObstaclePlacementTool` is a new class in `Hrot.Editor/Tools/`: on left-click fires the callback with world position; then pops itself.

**Tests:**
1. Call `SetRoadNetworkPath("z", "path.json")`; assert `UpdateZoneConfigCommand` published with correct fields (consume from bus)
2. Call `StartObstaclePlacementMode("z", 10f)`; assert `PushTool` called with `ObstaclePlacementTool`

---

### Task 6: EDIT1-A007 — `EditorPreviewAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A007  
**Design:** `.dev/edit-1/DESIGN.md` §4.G

**Constructor:** `EditorPreviewAdapter(PreviewClusterOpHandler handler, IScenarioStateProvider stateProvider)`  
**Located at:** `Hrot.Common/Orchestration/Handlers/PreviewClusterOpHandler.cs`.  
Search for `IScenarioStateProvider` in `Hrot.ScenarioEditor/` or `Hrot.Editor/`.  
**IMPORTANT:** `IScenarioStateProvider` and `ScenarioEditorState` DO NOT EXIST yet.  
Create them as described in Key API Facts §7 above.

**Implementations:**
- `IsInPreviewMode` → `stateProvider.CurrentState == ScenarioEditorState.OperatingPreview || stateProvider.CurrentState == ScenarioEditorState.LoadingPreview`
- `EnterPreviewMode()` → call `_handler.Commit(new NodeOpCommand(NodeOpType.PrepareState, "LoadingPreview"), null)` if `Commit` is accessible, OR add a convenience `TriggerLoadingPreview()` method to `PreviewClusterOpHandler` — inspect the class first and use the most appropriate entry point.
- `ExitPreviewMode()` → call the unloading preview equivalent.

**Implementations:**
- `IsInPreviewMode` → check `stateProvider.CurrentState` for preview states
- `EnterPreviewMode()` → `handler.LoadingPreviewCommit()` (or equivalent)
- `ExitPreviewMode()` → `handler.UnloadingPreviewCommit()` (or equivalent)

**Tests:**
1. Mock `IScenarioStateProvider.CurrentState = ScenarioEditorState.OperatingPreview`; assert `IsInPreviewMode == true`
2. Call `EnterPreviewMode()`; assert `handler.LoadingPreviewCommit()` called

---

### Task 7: EDIT1-A008 — `EditorMapConfigAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A008  
**Design:** `.dev/edit-1/DESIGN.md` §4.H

**Constructor:** `EditorMapConfigAdapter(MapViewConfig config)`  
(`MapViewConfig` is a NEW class you create in `Hrot.Map.Common/Config/MapViewConfig.cs` — see Key API Facts §5)

**Implementations:**
- `GetCurrentConfig()` → map `config.ShowSatelliteLayer`, `config.ShowGroundUnits`, `config.ShowAirUnits`, `config.ShowGrid` → `new MapLayerState(Satellite: ..., GroundUnits: ..., AirUnits: ..., Grid: ...)`
- `ApplyConfig(MapLayerState cfg)` → `config.ShowSatelliteLayer = cfg.Satellite; config.ShowGroundUnits = cfg.GroundUnits; config.ShowAirUnits = cfg.AirUnits; config.ShowGrid = cfg.Grid`

**Located at:** `Hrot.IG/Systems/MapUserConfig.cs`. Check `Hrot.Editor.csproj` for existing project references to `Hrot.IG` or `Hrot.Map.Common` to see if it's accessible.  
If `MapUserConfig` is in `Hrot.IG` and `Hrot.Editor` doesn't reference it, extract only the layer-visibility fields into a new `MapViewConfig` class in `Hrot.Map.Common` and use that instead — update this note in your report.

**Tests:**
1. `ApplyConfig(new MapLayerState(Satellite: false, GroundUnits: true, AirUnits: true, Grid: false))`; assert `MapViewConfig.ShowSatelliteLayer == false` and `MapViewConfig.ShowGrid == false`
2. Fresh `MapViewConfig` (all defaults); `GetCurrentConfig()` returns `MapLayerState` with all fields matching defaults

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: complete tasks in sequence with passing tests:**

1. **A001:** Implement → Write tests → **ALL tests pass** ✅
2. **A002:** Implement → Write tests → **ALL tests pass** ✅
3. **A003:** Implement → Write tests → **ALL tests pass** ✅
4. **A004:** Implement → Write tests → **ALL tests pass** ✅
5. **A005:** Implement → Write tests → **ALL tests pass** ✅
6. **A007:** Implement → Write tests → **ALL tests pass** ✅
7. **A008:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until tests pass. Fix compile errors immediately. Work autonomously.

---

## 🧪 Testing Requirements

- **Minimum 15 meaningful tests** across all 7 adapters
- Tests must use mocks/stubs for `MapCanvas` (use a simple `FakeMapCanvas` spy class)
- Tests must verify behavior (correct command published, correct method called), not just compilation
- `EditorMissionService.GetAvailableBehaviors` must be tested with actual ECS entity in world

---

## ⚠️ Quality Standards

- Zero DDS/CycloneDDS imports in any `Hrot.Editor/Adapters/` file
- Zero `Hrot.ExCon` imports
- `Hrot.Editor.csproj` must gain `<ProjectReference>` to `Hrot.UI.Common`
- XML `<summary>` on all public types and methods
- If any ECS method throws on unregistered component: register required components in test world setup

---

## 📊 Developer Insights (Required in Report)

**Q1:** What issues did you encounter implementing each adapter?

**Q2:** What existing types didn't exist as expected (e.g. `LocationPickerTool`, `MapUserConfig`, `IScenarioStateProvider`)?  
What did you create or substitute?

**Q3:** What design decisions did you make beyond the spec? (e.g. constructor signature changes, tool patterns)

**Q4:** Which adapter was most complex and why?

**Q5:** What is the highest-risk item for BATCH-06 (Editor systems: CargoSystem, PerceptionSetupSystem)?

---

## 🎯 Success Criteria

- [ ] All 7 adapter files created in `Hrot.Editor/Adapters/`
- [ ] `Hrot.Editor.csproj` references `Hrot.UI.Common`
- [ ] `Hrot.Editor` and `Hrot.Editor.Tests` build with zero errors
- [ ] Minimum 15 unit tests written and passing
- [ ] No DDS imports in any adapter file
- [ ] Report submitted to `.dev/edit-1/reports/BATCH-05-REPORT.md`

---

## 📚 Reference Materials

- **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A001 through §EDIT1-A008
- **Design:** `.dev/edit-1/DESIGN.md` §4.A through §4.H
- **Port interfaces:** `Hrot.UI.Common/Facades/`
- **New event types:** `FDP/Toolkits/FDP.Toolkit.Behavior/Events/EmbarkEntityCommand.cs`, `DisembarkEntityCommand.cs`
- **Zone commands:** `Hrot.Map.Common/Events/SpawnZoneObstacleCommand.cs`, `UpdateZoneConfigCommand.cs`
- **Tool patterns:** `Hrot.ScenarioEditor/Tools/` (CreationTool, StandardInteractionTool, RouteEditTool, EditTool — AreaAuthoring/RouteAuthoring only exist as IG methods, NOT tool classes)
- **IMapTool interface:** `FDP/Toolkits/FDP.Toolkit.Vis2D/Abstractions/IMapTool.cs`
- **New IScenarioStateProvider + ScenarioEditorState:** Create in `Hrot.ScenarioEditor/` (see Key API Facts §7)
- **BehaviorCatalog:** `Hrot.Map.Definitions/Tkb/BehaviorCatalog.cs`
- **BehaviorRegistry:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`
- **Test project:** `Hrot.Editor.Tests/` (use this for all adapter tests)
