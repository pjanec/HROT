# BATCH-26 Review

**Status: APPROVED**

## Pass Condition Verification

| Condition | Verified |
|-----------|----------|
| `CreationTool.cs` physically deleted | YES — confirmed absent |
| `CreationToolConstants.cs` physically deleted | YES — confirmed absent |
| `AreaPlacementTool.cs` physically deleted | YES — confirmed absent |
| `RoutePlacementTool.cs` physically deleted | YES — confirmed absent |
| `ObstaclePlacementTool.cs` physically deleted | YES — confirmed absent |
| `CreationToolTests.cs` physically deleted | YES — confirmed absent |
| `EntityPlacementGizmo` implements stateful gizmo contract, `RequiresExclusiveFocus = true` | YES |
| `ObstaclePlacementGizmo` implements stateful gizmo contract, `RequiresExclusiveFocus = true` | YES |
| `PlacementCanvasBridge` implements `IMapTool`, forwards canvas events to gizmo | YES |
| `EditorSpawnAdapter`, `EditorZoneAdapter`, `MapCommandController` use bridge+gizmo | YES |
| `ToolPresenceTests` asserts `CreationTool` absent, new types present | YES |
| Solution builds 0 errors | YES |
| `Hrot.Presentation.Tests` all pass (EPG-001..006 included) | YES — 57 passed, 0 failed |
| `Hrot.IG.Tests` no new failures vs 68 baseline | YES — 315 passed, 68 failed (unchanged) |
| `Hrot.Editor.Tests` all pass | YES — 95 passed, 0 failed |

## Code Quality Assessment

### EntityPlacementGizmo
Good. Constants from `CreationToolConstants` are correctly inlined as `private const`.
`Remove()` fires `Exited` before calling `_onRemove()` — correct ordering (observer runs before canvas pop).
`OnMouseEvent(Left, isPressed=false)` is the correct commit trigger. `BuildAndPublishSpawnCommand` logic carried over from `CreationTool` verbatim.

### ObstaclePlacementGizmo
Good. Minimal and correct. Red sphere preview at cursor, single-click commit, right/Escape cancel.

### PlacementCanvasBridge
Good. Aliased usings (`GizmoMouseButton`, `GizmoKeyboardKey`) resolve the namespace collision cleanly. `HandleHover` and `HandleDrag` both call `OnDragUpdate` — correct. `HandleClick(Left)` passes `isPressed=false` (release = commit), `HandleClick(Right)` passes `isPressed=true` (press = cancel) — matches `EntityPlacementGizmo.OnMouseEvent` logic exactly. `OnExit` calls `SetFocus(false)` then `Dispose()` — correct ordering.

### Adapter changes
`EditorSpawnAdapter`, `EditorZoneAdapter`, `MapCommandController` all use the `bridge = null; var gizmo = new ...(onRemove: () => bridge?.RequestPop()); bridge = new PlacementCanvasBridge(gizmo)` pattern. The null-coalescing in `onRemove` is safe against the brief window before `bridge` is assigned.

### Unscripted fixes
The following files were correctly fixed outside the batch task list:
- `IgApplication.cs` — two test hooks updated to `PlacementCanvasBridge`
- `MapCommandControllerTests.cs` — casts updated to `PlacementCanvasBridge`

These were real compile errors found during build verification — fixing them was appropriate.

## Design Deviation: IEntityStatefulGizmo vs IStatefulGizmo

The spec called for `IStatefulGizmo` (GizmoMap.Contracts) managed by `GizmoInteractionManager`. The implementation uses `IEntityStatefulGizmo` (Fdp.Toolkits) managed by `PlacementCanvasBridge`.

**This deviation is ACCEPTED.** Rationale:

1. `IStatefulGizmo.UpdateAndDraw` takes `IGizmoDrawBuilder` (GizmoMap.Contracts). The FDP runtime does not provide this at the canvas layer — it provides `IDebugDrawBuilder`. There is no conversion path available without cross-assembly changes.
2. `IEntityStatefulGizmo` is the established runtime convention for all gizmos in `Hrot.Presentation` (`VertexEditGizmo`, `RouteWaypointGizmo`). Using it maintains consistency.
3. The Phase 3 goal is met: global authoring interactions are decoupled from `IMapTool` FSMs, declare `RequiresExclusiveFocus = true`, and emit commands via injected delegates. The `PlacementCanvasBridge` serves as the Phase 3 interaction manager (simplified, pending Phase 6 full removal of the canvas stack).
4. `GizmoInteractionManager` proper will be introduced in Phase 4 (picker tools), which is when the full routing pipeline needs to be unified.

## Conclusion

Phase 3 is complete. All five legacy tool files are deleted. Two new gizmo implementations replace them. `PlacementCanvasBridge` provides the canvas-to-gizmo translation layer. Build is clean, no test regressions.
