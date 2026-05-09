# BATCH-25 REPORT: Phase 2 — Purge Geometry Manipulation Tools

**Status:** COMPLETE — all tasks implemented, build passes (0 errors), all new tests pass, no regressions introduced.

---

## Summary

BATCH-25 completes Phase 2 of the gizmo migration initiative.  The four legacy
`*Tool` source files (`EditTool`, `EditToolConstants`, `RouteEditTool`,
`RouteEditToolConstants`) have been **physically deleted**.  In their place, two
new `IEntityStatefulGizmo` implementations (`VertexEditGizmo` and
`RouteWaypointGizmo`) are wired into the ECS via marker components and the
`DataDrivenGizmoSystem`, following the same pattern established in Phases 0-1.

---

## Pass Condition Checklist

| Condition | Status |
|-----------|--------|
| `EditTool.cs` deleted | PASS |
| `EditToolConstants.cs` deleted | PASS |
| `RouteEditTool.cs` deleted | PASS |
| `RouteEditToolConstants.cs` deleted | PASS |
| Solution builds with 0 errors | PASS |
| `Hrot.Presentation.Tests`: 51/51 pass (incl. VEG-001..005, RWG-001..004) | PASS |
| `Hrot.IG.Tests`: no new failures vs HEAD baseline | PASS |

---

## Test Results

### `Hrot.Presentation.Tests`

```
Passed!  - Failed: 0, Passed: 51, Skipped: 0, Total: 51
```

All 9 new tests pass:

| ID | Test | Result |
|----|------|--------|
| VEG-001 | `VertexEditGizmo_Draw_EmitsHandlesForEachVertex` | PASS |
| VEG-002 | `VertexEditGizmo_OnDragUpdate_MovesActiveVertex` | PASS |
| VEG-003 | `VertexEditGizmo_OnCommit_WritesBackToECS` | PASS |
| VEG-004 | `VertexEditGizmo_OnCancel_RevertsVertex` | PASS |
| VEG-005 | `VertexEditGizmo_MenuAction_InsertsAndDeletesVertex` | PASS |
| RWG-001 | `RouteWaypointGizmo_Draw_EmitsHandlesForEachWaypoint` | PASS |
| RWG-002 | `RouteWaypointGizmo_OnDragUpdate_MovesActiveWaypoint` | PASS |
| RWG-003 | `RouteWaypointGizmo_OnCancel_RevertsAndSetsIndexToMinusOne` | PASS |
| RWG-004 | `RouteWaypointGizmo_OnCommit_WritesBackToECS` | PASS |

### `Hrot.IG.Tests`

```
Failed: 68, Passed: 328, Skipped: 0, Total: 396
```

The 68 failures are **identical to HEAD baseline before BATCH-25 changes** — confirmed
by running the full test suite on the unmodified HEAD commit and observing the
same 68/328/396 result.  All 68 failures share the same root cause:

```
System.InvalidOperationException: StatelessGizmoRegistry.Register:
required component type 'BrainBlackboard' is not registered in ComponentTypeRegistry.
```

Stack trace: `IgApplication.InitializeEmbedded(headless: true)` →
`Hrot.IG.Gizmos.GizmoRegistrar.Register` →
`Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll`.

`BrainBlackboard` is registered by `CgfComponentRegistry` and
`CognitiveComponentRegistry`, neither of which runs in the headless init path.
This is a pre-existing architectural gap, unrelated to BATCH-25.

---

## Files Created

| File | Description |
|------|-------------|
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/ActiveEditMarkers.cs` | ECS marker structs `ActiveVertexEditRequest` (id=187) and `ActiveRouteEditRequest` (id=188) |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/IRouteWaypointEditorState.cs` | Interface exposing `SelectedVertexIndex` and `GetSelectedWaypointRef()` to `WaypointEditorPanel` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/VertexEditGizmo.cs` | Stateful gizmo — drags `EditablePolyline` vertices via SubElementId hit-testing; supports mid-point insert and vertex delete via context menu |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/VertexEditGizmoDefinition.cs` | Definition requiring `SimTransform` + `ActiveVertexEditRequest`; `RequiresExclusiveFocus = false` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteWaypointGizmo.cs` | Stateful gizmo — drags `RoutePlan` waypoints; exposes `RouteWaypointGizmo.Current` singleton for panel binding |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteWaypointGizmoDefinition.cs` | Definition requiring `SimTransform` + `ActiveRouteEditRequest`; `RequiresExclusiveFocus = false` |
| `Hrot/Engine/Hrot.Presentation.Tests/VertexEditGizmoTests.cs` | Tests VEG-001 through VEG-005 |
| `Hrot/Engine/Hrot.Presentation.Tests/RouteWaypointGizmoTests.cs` | Tests RWG-001 through RWG-004 |

---

## Files Modified

| File | Changes |
|------|---------|
| `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` | Added `ActiveVertexEditRequest = 187` and `ActiveRouteEditRequest = 188` after `ActiveRotationToolRequest = 186` |
| `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs` | Replaced 4 `NotNull` assertions for deleted tool types with 4 `Null` assertions in `ScenarioEditor_Assembly_ContainsAllToolTypes` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Registered new marker components and gizmo definitions; replaced edit/route tool activation with ECS marker toggle |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Added `using Hrot.ScenarioEditor.Gizmos;`; registered `ActiveVertexEditRequest`, `ActiveRouteEditRequest`, `GizmoComponentActivatedEvent`; registered `VertexEditGizmoDefinition`, `RouteWaypointGizmoDefinition`, `DataDrivenGizmoSystem`; changed `WaypointEditorPanel` constructor to accept `Func<IRouteWaypointEditorState?>`; qualified `GizmoRegistrar.Register` call to resolve ambiguity; updated `TestHook_ActiveRouteEditTool` to return `RouteWaypointGizmo.Current`; removed stale RouteEditTool/EditTool popup blocks; rewrote `ActivateAreaEditingTool()` to use ECS marker toggle |
| `Hrot/Subsystems/Hrot.IG/UI/WaypointEditorPanel.cs` | Changed constructor parameter from concrete tool type to `Func<IRouteWaypointEditorState?>` |
| `Hrot/Subsystems/Hrot.IG.Tests/AdvancedFeaturesIntegrationTests.cs` | Replaced `EditTool`-based Step 4 with `VertexEditGizmo` interaction; removed `using Hrot.ScenarioEditor.Tools;`; added `using Hrot.ScenarioEditor.Gizmos;` |
| `Hrot/Subsystems/Hrot.IG.Tests/IgApplicationTests.cs` | Replaced `RouteEditTool` reference with `RouteWaypointGizmo` in the CT-1 test |
| `Hrot/Subsystems/Hrot.IG.Tests/WaypointEditorPanelTests.cs` | Updated to use `StubRouteState : IRouteWaypointEditorState` stub instead of concrete tool |
| `FDP/Diagnostics/Fdp.Diagnostics.Network/TypeForwards.cs` | Pre-existing fix: commented out dead `global using StringInternBatch` alias |
| `FDP/Toolkits/Fdp.Toolkits/GlobalUsings.GizmoNetwork.cs` | Pre-existing fix: same |
| `FDP/Toolkits/Fdp.Toolkits.Tests/GlobalUsings.GizmoNetwork.cs` | Pre-existing fix: same |
| `IOS-IG-SimHost.sln` | Project references updated for new gizmo source files |

---

## Files Deleted

| File | Reason |
|------|--------|
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EditTool.cs` | Replaced by `VertexEditGizmo` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EditToolConstants.cs` | No longer needed |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RouteEditTool.cs` | Replaced by `RouteWaypointGizmo` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RouteEditToolConstants.cs` | No longer needed |
| `Hrot/Subsystems/Hrot.IG.Tests/EditToolTests.cs` | Covered by `VertexEditGizmoTests.cs` |
| `Hrot/Subsystems/Hrot.IG.Tests/RouteEditToolTests.cs` | Covered by `RouteWaypointGizmoTests.cs` |

---

## Pre-existing Issues Fixed

| Issue | Files Affected | Fix |
|-------|---------------|-----|
| `GizmoMap.Network.StringInternBatch` type does not exist; global alias was dead code | `Fdp.Diagnostics.Network/TypeForwards.cs`, `Fdp.Toolkits/GlobalUsings.GizmoNetwork.cs`, `Fdp.Toolkits.Tests/GlobalUsings.GizmoNetwork.cs` | Commented out the `global using StringInternBatch` line in all three files |
| `GizmoRegistrar` ambiguity after adding `using Hrot.ScenarioEditor.Gizmos;` — both `Hrot.IG.Gizmos` and `Hrot.ScenarioEditor.Gizmos` contain a generated `GizmoRegistrar` | `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Qualified the call as `Hrot.IG.Gizmos.GizmoRegistrar.Register(...)` |

---

## Key Design Decisions

- **Non-exclusive focus**: Both gizmos set `RequiresExclusiveFocus = false` so they coexist with other drawing layers without monopolising the input focus.
- **SubElementId anchoring**: Vertex/waypoint handles use `prim.SubElementId = (ushort)(i + 1)` (0 is reserved as "no sub-element") so hit-testing uniquely identifies which handle was picked.
- **OnCancel reverts in-memory only**: Neither gizmo writes to ECS on cancel; only `OnCommit` calls `_repo.SetManagedComponent` + `_repo.Bus.PublishManaged(new UpdateEntityCommand {...})`.
- **Singleton `RouteWaypointGizmo.Current`**: Set in constructor, cleared in `Dispose`, so `WaypointEditorPanel` can poll it via `Func<IRouteWaypointEditorState?>` without holding a direct reference.
- **Interface decoupling**: `IRouteWaypointEditorState` lets `WaypointEditorPanel` (in `Hrot.IG`) read gizmo state without depending on the `Hrot.Presentation` assembly directly.
