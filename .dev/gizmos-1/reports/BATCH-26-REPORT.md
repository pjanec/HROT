# BATCH-26 Report — Phase 3: Migrate Placement Tools to IStatefulGizmo

## Pass Conditions

| Condition | Status |
|-----------|--------|
| `CreationTool.cs` physically deleted | YES |
| `CreationToolConstants.cs` physically deleted | YES |
| `AreaPlacementTool.cs` physically deleted | YES |
| `RoutePlacementTool.cs` physically deleted | YES |
| `ObstaclePlacementTool.cs` physically deleted | YES |
| `CreationToolTests.cs` physically deleted | YES |
| `EntityPlacementGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | YES |
| `ObstaclePlacementGizmo` implements `IEntityStatefulGizmo`, `RequiresExclusiveFocus = true` | YES |
| `PlacementCanvasBridge` implements `IMapTool`, forwards events to gizmo | YES |
| `EditorSpawnAdapter`, `EditorZoneAdapter`, `MapCommandController` use bridge+gizmo | YES |
| `ToolPresenceTests` asserts `CreationTool` is absent | YES |
| Solution builds 0 errors | YES |
| `Hrot.Presentation.Tests`: all pass (incl. EPG-001..006) | YES — 57 passed, 0 failed |
| `Hrot.IG.Tests`: no new failures vs 68-failure baseline | YES — 315 passed, 68 failed (baseline) |
| `Hrot.Editor.Tests`: all pass | YES — 95 passed, 0 failed |

## Files Deleted (Task 1)

| File | Reason |
|------|--------|
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationTool.cs` | Replaced by `EntityPlacementGizmo` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationToolConstants.cs` | Constants inlined into `EntityPlacementGizmo` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/AreaPlacementTool.cs` | Not yet replaced; stub removed |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RoutePlacementTool.cs` | Not yet replaced; stub removed |
| `Hrot/Subsystems/Hrot.Editor/Tools/ObstaclePlacementTool.cs` | Replaced by `ObstaclePlacementGizmo` |
| `Hrot/Engine/Hrot.Presentation.Tests/ScenarioEditor/Tools/CreationToolTests.cs` | Old tests deleted; new tests in `EntityPlacementGizmoTests.cs` |

## Files Created

| File | Purpose |
|------|---------|
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs` | IEntityStatefulGizmo that converts left-click to SpawnEntityCommand |
| `Hrot/Subsystems/Hrot.Editor/Gizmos/ObstaclePlacementGizmo.cs` | IEntityStatefulGizmo for obstacle placement |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/PlacementCanvasBridge.cs` | IMapTool adapter that forwards canvas events to IEntityStatefulGizmo |
| `Hrot/Engine/Hrot.Presentation.Tests/ScenarioEditor/Gizmos/EntityPlacementGizmoTests.cs` | Unit tests EPG-001 through EPG-006 |

## Files Modified

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Editor/Adapters/EditorSpawnAdapter.cs` | Use `EntityPlacementGizmo + PlacementCanvasBridge` instead of `CreationTool` |
| `Hrot/Subsystems/Hrot.Editor/Adapters/EditorZoneAdapter.cs` | Use `ObstaclePlacementGizmo + PlacementCanvasBridge` instead of `ObstaclePlacementTool` |
| `Hrot/Subsystems/Hrot.IG/Systems/MapCommandController.cs` | Use `EntityPlacementGizmo + PlacementCanvasBridge` instead of `CreationTool` |
| `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs` | Assert `CreationTool`/`CreationToolConstants` absent; assert new types present |
| `Hrot/Subsystems/Hrot.IG.Tests/ToolInteractionIntegrationTests.cs` | Replace `CreationTool_LeftClick_*` tests with `EntityPlacementGizmo_LeftClick_*` |
| `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs` | Update placement tests to use `PlacementCanvasBridge` |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Update `TestHook_IsCreationToolActive` and `TestHook_DirectCreationToolClick` to use `PlacementCanvasBridge` |
| `Hrot/Subsystems/Hrot.IG.Tests/MapCommandControllerTests.cs` | Replace all `CreationTool` casts with `PlacementCanvasBridge` |

## Issues Encountered and Resolutions

### Issue 1: `PlacementCanvasBridge` using `IStatefulGizmo` vs `IEntityStatefulGizmo`

**Problem:** The batch instructions specified implementing `IStatefulGizmo` (from `GizmoMap.Contracts`), whose `UpdateAndDraw` takes `IGizmoDrawBuilder`. However, `ctx.DrawBuilder` in `RenderContext` is `IDebugDrawBuilder` (from `Fdp.Diagnostics.Contracts`). These are distinct interfaces — `DebugPrimitiveBuffer` does NOT implement `IGizmoDrawBuilder` via the FDP assembly. A cast from `IDebugDrawBuilder` to `IGizmoDrawBuilder` would fail at runtime.

**Resolution:** Used `IEntityStatefulGizmo` (from `Fdp.Toolkit.Diagnostics.Gizmos` in `Fdp.Toolkits`) instead, which takes `IDebugDrawBuilder`. This is consistent with all other gizmos in `Hrot.Presentation` (`VertexEditGizmo`, `RouteWaypointGizmo`, `EntityRotatorGizmo` in `Hrot.SimHost`). The functional contract is identical; only the draw-builder parameter type differs.

### Issue 2: `MapMouseButton` type ambiguity in `PlacementCanvasBridge`

**Problem:** The file initially imported both `using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;` (for `IStatefulGizmo`) and had alias imports for `GizmoMouseButton`/`GizmoKeyboardKey`. The compiler saw `MapMouseButton` as ambiguous between the canvas and gizmo namespaces.

**Resolution:** Removed the namespace-level import; used explicit aliases (`using GizmoMouseButton = ...`, `using GizmoKeyboardKey = ...`) to resolve the ambiguity.

### Issue 3: `IgApplication.cs` test hooks still referenced `CreationTool`

**Problem:** `IgApplication.cs` has two internal test hooks (`TestHook_IsCreationToolActive`, `TestHook_DirectCreationToolClick`) that were not covered by the batch tasks but referenced the deleted `CreationTool`. Found during build verification.

**Resolution:** Updated both test hooks to reference `PlacementCanvasBridge` instead.

### Issue 4: `MapCommandControllerTests.cs` not updated

**Problem:** `MapCommandControllerTests.cs` still used `CreationTool` in cast expressions. The batch tasks did not explicitly list this file for update, but the deleted type made it a compile error.

**Resolution:** Updated all `(CreationTool)canvas.ActiveTool!` casts to `(PlacementCanvasBridge)canvas.ActiveTool!`.

### Issue 5: `AdapterTests.cs` missing `using Hrot.Editor.Tools`

**Problem:** When updating `AdapterTests.cs`, the `using Hrot.Editor.Tools;` import was removed. However, `LocationPickerTool` and `ModalBoxSelectionTool` (unrelated to the batch changes) also live in that namespace.

**Resolution:** Added `using Hrot.Editor.Tools;` back alongside `using Hrot.ScenarioEditor.Gizmos;`.

### Issue 6: `ToolInteractionIntegrationTests.cs` missing `using Hrot.ScenarioEditor.Tools`

**Problem:** The `StandardInteractionTool_SelectEntity_*` test (pre-existing) at the end of the file requires `using Hrot.ScenarioEditor.Tools;`, which was removed when we replaced the `using Hrot.ScenarioEditor.Tools;` with `using Hrot.ScenarioEditor.Gizmos;`.

**Resolution:** Added both usings.

## Design Decisions

1. **`IEntityStatefulGizmo` over `IStatefulGizmo`:** Using `IEntityStatefulGizmo` (FDP-extended) is the correct choice for gizmos running inside Hrot.Presentation, as it takes `IDebugDrawBuilder` which the canvas RenderContext provides. `IStatefulGizmo` (GizmoMap.Contracts) is meant for pure standalone gizmos outside the FDP runtime. This is consistent with `VertexEditGizmo`, `RouteWaypointGizmo`, and `EntityRotatorGizmo` in `Hrot.SimHost`.

2. **`onRemove` callback pattern:** The `PlacementCanvasBridge.RequestPop()` is wired into gizmo construction so that when the gizmo calls `_onRemove()`, the bridge pops itself off the canvas, which triggers `OnExit()` and `Dispose()`. This avoids reference cycles and circular calls.

3. **`autoPopOnPlace = true` default:** Single-placement mode is the default; multi-placement can be enabled by the caller passing `autoPopOnPlace: false`. This is backwards-compatible with the previous `CreationTool` behavior.

4. **Constants inlined:** `CreationToolConstants` values (`DefaultTkbType = 101L`, `GhostAlpha = 128`, `GhostRadiusPx = 15`, `GhostLabelOffsetY = 20`) were inlined as `private const` in `EntityPlacementGizmo`. This is appropriate since they are implementation details of the gizmo.

5. **`Exited` event retained:** The `Exited` event on `EntityPlacementGizmo` provides the same lifecycle hook that `MapCommandController` previously used via `tool.Exited += OnCreationToolExited`. The `onRemove` callback now handles this inline.
