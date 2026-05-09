# BATCH-28 Report — Phase 5: StandardInteractionTool Eradication

**Status:** COMPLETE  
**Build:** Zero errors  
**Tests (Hrot.Presentation.Tests):** 71/71 passed  

---

## Summary

All `StandardInteractionTool` god-classes (Hrot wrapper and FDP base) have been deleted.
Entity selection and drag are now pure ECS systems and gizmos:
- `SelectionInteractionSystem` handles selection, deselection, and delete-key dispatch.
- `EntityDragGizmo` / `EntityDragGizmoDefinition` handle per-entity drag-to-move.

---

## Files Created

| File | Purpose |
|---|---|
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Systems/SelectionInteractionSystem.cs` | New ECS system replacing selection/delete logic from StandardInteractionTool |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityDragGizmo.cs` | New `IEntityStatefulGizmo` replacing EntityDragTool |
| `Hrot/Engine/Hrot.Presentation.Tests/SelectionInteractionSystemTests.cs` | 8 unit tests (SIS-001..SIS-008) |
| `Hrot/Engine/Hrot.Presentation.Tests/EntityDragGizmoTests.cs` | 6 unit tests (EDG-001..EDG-006) |

---

## Files Updated

| File | Change |
|---|---|
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/IDebugDrawBuilder.cs` | Added `DrawEntitySphere` default method (Task A) |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs` | Concrete `DrawEntitySphere` implementation (Task A) |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` | Test `DrawEntitySphere_SetsAnchorAndShape` (SC-PHASE5-A) |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/IgEntityPresentationGizmo.cs` | Added transparent pick sphere via `DrawEntitySphere` (Task D) |
| `Hrot/Subsystems/Hrot.SimHost/Gizmos/SimHostEntityPresentationGizmo.cs` | Same pick sphere (Task D) |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Removed alias + old field, added `SelectionInteractionSystem`, `EntityDragGizmoDefinition`, `SelectionInteractionSystemAdapter`, restored `_miniIosPanel.SetGateway` (Tasks E, G) |
| `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs` | Removed `_interactionTool`/`_vehicleQuery`, added `SelectionInteractionSystem`, Tick in Update (Task H) |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Registered `EntityDragGizmoDefinition` in GizmoRegistry |
| `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs` | Phase 5 type presence assertions (Task I) |
| `Hrot/Engine/Hrot.Presentation.Tests/WorldResetTests.cs` | Replaced legacy test with `SelectionInteractionSystem_ClearAllSelections_ResetsEcsState` (Task I) |
| `Hrot/Subsystems/Hrot.ExCon.Tests/ExConUiPackBoundaryTests.cs` | Removed `StandardInteractionTool` from forbidden names (Task I) |
| `Hrot/Subsystems/Hrot.IG.Tests/MapEventTranslatorTests.cs` | Replaced deleted-type test with Phase 5 absence assertion |
| `Hrot/Subsystems/Hrot.IG.Tests/ToolInteractionIntegrationTests.cs` | Replaced old tool test, removed stale using |
| `FDP/Examples/Fdp.Examples.CarKinem/CarKinemApp.cs` | Removed `StandardInteractionTool`, `PointSequenceTool`, and related event wiring |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Removed `StandardInteractionTool` field and setup block |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Removed `EditorInteractionTool` alias, field, and setup block |

---

## Files Deleted (Task F)

1. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs`
2. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionToolConstants.cs`
3. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EntityDragTool.cs`
4. `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs`
5. `FDP/Engine/Fdp.Presentation/Vis2D/Tools/EntityDragTool.cs`
6. `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs`
7. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/StandardInteractionToolTests.cs`
8. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/EntityDragToolTests.cs`
9. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/BoxSelectionToolTests.cs`

---

## P2 Technical Debt

The following behaviours were in StandardInteractionTool but are not yet implemented in the ECS replacement:

1. **Multi-select (Shift/Ctrl click):** `SelectionInteractionSystem` clears selection on every pick; augment-selection not implemented.

2. **Box selection:** `BoxSelectionTool` deleted; no ECS equivalent exists yet.

3. **Right-click waypoints in SimHost:** The old `_interactionTool.OnWorldClick` right-click branch that triggered waypoint/path dialogs was removed entirely from `SimHostVisualization`. No ECS substitute.

4. **Deselect on empty-space click:** `DebugGizmoLayer` only publishes `GizmoInteractionStartedEvent` when a primitive is hit. Clicking empty space does not deselect.

5. **SmartEgressUtil.MarkDirty on drag:** `EntityDragGizmo.ApplyPosition` writes directly to `SimTransform`. `SmartEgressUtil.MarkDirty` is not called; relies on egressTranslator auto-detection.

6. **Continuous drag preview:** `EntityDragGizmo` only commits position on mouse release (`OnDragCommitted`). Live preview during drag is deferred.

7. **CGF and Editor entity interaction:** `CgfSubsystem` and `EditorSubsystem` had their `StandardInteractionTool` blocks removed but do not yet wire `SelectionInteractionSystem`. Entity pick/select/drag is currently non-functional in CGF and Editor subsystems.
