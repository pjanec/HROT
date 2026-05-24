# BATCH-24 Report -- Phase 1: Context Menu Decoupling & Marker Components

**Status:** COMPLETE -- all tasks implemented, build passes (0 errors), all 6 new tests pass.

---

## Tasks Completed

### Task 1 -- Delete ExclusiveCaptureProxyTool

**Deleted:**

- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/ExclusiveCaptureProxyTool.cs`

No tests existed for the deleted class (confirmed by grep). The two call-sites in
`SimHostVisualization.cs` were left as compile stubs (TODO BATCH-24 comments) until
Task 6 replaced them with the correct ECS-driven pattern.

### Task 2 -- Add ActiveRotationToolRequest and GizmoComponentActivatedEvent

**New files created:**

- `Hrot/Subsystems/Hrot.SimHost/Gizmos/GizmoActivationMarkers.cs`
  Defines `ActiveRotationToolRequest` -- a zero-payload, `[ComponentId(186)]`-tagged
  marker struct. Decorated with `[ComponentId(HrotComponentIds.ActiveRotationToolRequest)]`.

**Modified files:**

- `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`
  Added `ActiveRotationToolRequest = 186` in a new gizmo activation block after ID 185.

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
  Added `GizmoComponentActivatedEvent` (EventId 8058) at end of file.

- `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs`
  Registered `ActiveRotationToolRequest` component and `GizmoComponentActivatedEvent`
  event at the end of `RegisterAll()`.

### Task 3 -- Enhance DataDrivenGizmoSystem

**Modified file:**

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

Three additions:

1. **Step 1b (per-frame mask teardown scan)** -- inserted between Steps 1 and 2 in
   `Execute()`. Iterates `_activeGizmos`, computes `BitMask256.HasAll` for each
   rule-based gizmo instance (skips injected gizmos with `RuleIndex < 0`), collects
   `(entity, ruleIndex)` pairs where the mask is no longer satisfied, then calls
   `TeardownGizmoByRule` for each collected pair. Uses a local list to avoid
   mutating the dictionary during iteration.

2. **Step 2b (late-activation via GizmoComponentActivatedEvent)** -- inserted after
   Step 2. Reads `GizmoComponentActivatedEvent`, checks the entity is alive and the
   component mask satisfies each rule, skips if the same RuleIndex is already active
   (via `.Any(gi => gi.RuleIndex == rule.RuleIndex)`), calls `CreateInstance`, adds
   to `_activeGizmos`/_entityList, and grants exclusive focus if the gizmo requests
   it and no other gizmo holds focus.

3. **`TeardownGizmoByRule(Entity entity, int ruleIndex)` helper** -- added before
   the existing `TeardownEntity` method. Iterates the gizmo list for the entity in
   reverse, finds the matching RuleIndex, clears focus if needed, calls
   `gizmo.Dispose()`, and removes the entry. Cleans up `_activeGizmos` and
   `_entityList` when the list reaches zero.

Also added `using System.Linq;` at the top for the `.Any()` call in Step 2b.

### Task 4 -- Create EntityRotatorGizmoDefinition

**New file:**

- `Hrot/Subsystems/Hrot.SimHost/Gizmos/EntityRotatorGizmoDefinition.cs`

Implements `IGizmoDefinition` with `RequiredComponents = { SimTransform, ActiveRotationToolRequest }`.
`VisibilityPolicy` is `AlwaysVisiblePolicy.Instance`. `CreateInstance` casts the
`ISimulationView` to `EntityRepository` and constructs an `EntityRotatorGizmo` with
an `onRemove` callback that conditionally removes `ActiveRotationToolRequest` from
the entity (guarded by `HasComponent` to prevent double-remove).

### Task 5 -- Create GizmoFocusInputBridge

**New file:**

- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoFocusInputBridge.cs`

Generic canvas `IMapTool` replacing `ExclusiveCaptureProxyTool`. Does NOT reference
any specific gizmo implementation. Constructor takes `FdpEventBus` and `Entity`.
Stores a `PickToken { Target = focusEntity }` for use in all published events.

Event mapping:

| Canvas input             | ECS event published                      | Pop tool? |
|--------------------------|------------------------------------------|-----------|
| `HandleHover`            | `GizmoDragUpdateEvent`                   | no        |
| `HandlePress`            | (none, returns true to consume)          | no        |
| `HandleDrag`             | `GizmoDragUpdateEvent`                   | no        |
| `HandleClick(Left)`      | `GizmoMouseEvent { IsPressed=false }`    | yes       |
| `HandleClick(Right)`     | `GizmoMouseEvent { IsPressed=true }`     | yes       |
| `HandleKeyPressed(Esc)`  | `GizmoKeyEvent`                          | yes       |
| `HandleKeyPressed(other)`| `GizmoKeyEvent`                          | no        |

### Task 6 -- Fix SimHostVisualization context menus

**Modified file:**

- `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`

Both occurrences of the proxy hack pattern replaced:

1. Entity inspector "Rotate entity" menu item (~line 200):
   Adds `ActiveRotationToolRequest` (with `default` value for the zero-payload struct),
   publishes `GizmoComponentActivatedEvent`, pushes `GizmoFocusInputBridge`.

2. Map right-click `rotateTool:` lambda (~line 473):
   Same three-step pattern. Guards against missing `SimTransform` and missing `ActiveRotationToolRequest`
   via `HasComponent` checks before `AddComponent`.

Both sites use `AddComponent<T>(entity, default)` (not the zero-argument overload) since
`EntityRepository.AddComponent<T>` requires an explicit component value.

### Task 7 -- Register, wire, test

**Modified files:**

- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
  Added `_gizmoRegistry.Register(new EntityRotatorGizmoDefinition())` immediately after
  `GizmoRegistrar.RegisterAll(...)` and before `new DataDrivenGizmoSystem(...)`.

- `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs`
  (Done in Task 2 -- registration of component and event.)

### Task 8 -- Tests

**New file:**

- `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/EntityRotatorGizmoActivationTests.cs`

Six tests across three classes:

| Test ID    | Class                               | What it verifies                                                                        |
|------------|-------------------------------------|-----------------------------------------------------------------------------------------|
| SC_ER001   | EntityRotatorGizmoMarkerTests       | `ActiveRotationToolRequest` has no instance fields; managed size == 1 byte.             |
| SC_ER002   | EntityRotatorGizmoMarkerTests       | `EntityRotatorGizmoDefinition.RequiredComponents` has exactly SimTransform + marker.    |
| SC_ER003   | EntityRotatorGizmoSystemTests       | Publishing `GizmoComponentActivatedEvent` activates the gizmo; buffer receives draws.  |
| SC_ER004   | EntityRotatorGizmoSystemTests       | Removing `ActiveRotationToolRequest` tears the gizmo down; buffer is empty next frame.  |
| SC_ER005   | GizmoFocusInputBridgeTests          | `HandleHover` publishes `GizmoDragUpdateEvent` with correct position and token.         |
| SC_ER006   | GizmoFocusInputBridgeTests          | `HandleClick(Left)` publishes `GizmoMouseEvent` with `IsPressed=false`.                 |

---

## Build Result

```
Build succeeded.
    0 Error(s)
```

---

## Test Results

### New tests (BATCH-24)

```
Passed Hrot.SimHost.Tests.Gizmos.GizmoFocusInputBridgeTests.SC_ER005_HandleHover_PublishesGizmoDragUpdateEvent [34 ms]
Passed Hrot.SimHost.Tests.Gizmos.EntityRotatorGizmoMarkerTests.SC_ER002_EntityRotatorGizmoDefinition_RequiredComponents_ContainsBothTypes [39 ms]
Passed Hrot.SimHost.Tests.Gizmos.EntityRotatorGizmoMarkerTests.SC_ER001_ActiveRotationToolRequest_IsMarkerWithNoFields [< 1 ms]
Passed Hrot.SimHost.Tests.Gizmos.GizmoFocusInputBridgeTests.SC_ER006_HandleClick_Left_PublishesGizmoMouseEventWithIsPressedFalse [3 ms]
Passed Hrot.SimHost.Tests.Gizmos.EntityRotatorGizmoSystemTests.SC_ER004_RemovingMarkerComponent_TearsDownGizmo [63 ms]
Passed Hrot.SimHost.Tests.Gizmos.EntityRotatorGizmoSystemTests.SC_ER003_GizmoComponentActivatedEvent_ActivatesGizmoAndDraws [< 1 ms]

Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 0.9583 Seconds
```

### FDP Toolkits gizmo tests (regression check)

```
Passed!  - Failed:     0, Passed:   144, Skipped:     0, Total:   144, Duration: 812 ms
```

All 144 pre-existing gizmo tests pass. The 26 failures in the full FDP Toolkits suite
and 20 failures in the full SimHost Tests suite are pre-existing and unrelated to
BATCH-24 (HillAttack, AreaQuery, MissionPlan, UnitSubordinate, MissionDirector).

---

## Developer Insights

**Q1: Most difficult part of modifying DataDrivenGizmoSystem?**

The trickiest part was avoiding dictionary mutation during iteration in the Step 1b
teardown scan. The solution was to collect `(entity, ruleIndex)` pairs into a local
list and then call `TeardownGizmoByRule` in a second pass. The ECS event registration
gotcha was that `ActiveRotationToolRequest` needs `[ComponentId]` to be registerable
with `EntityRepository.RegisterComponent<T>()` -- the error message is informative
("missing a [ComponentId] attribute") but easy to miss when the struct was initially
created without one.

**Q2: Did the per-frame mask teardown scan cause any test interference?**

No interference. The scan only affects entries in `_activeGizmos` (rule-based gizmos),
not injected gizmos, because injected gizmos live in the separate `_injectedGizmos`
dictionary and never appear in `_activeGizmos`. The pre-existing 144 gizmo tests all
continue to pass, confirming no regression.

**Q3: Design decisions in GizmoFocusInputBridge beyond the spec?**

The spec was silent on what to do with non-Escape key presses. The implementation
publishes a `GizmoKeyEvent` for every key but only pops the tool on Escape. This lets
future gizmos respond to arbitrary key bindings without changes to the bridge. The
alternative (ignoring non-Escape keys entirely) would have required modifying the bridge
for every new keybinding, creating exactly the kind of coupling the batch is designed
to eliminate.

**Q4: Were there existing tests for the deleted ExclusiveCaptureProxyTool?**

No. A grep for `ExclusiveCaptureProxyTool` across all test files returned zero results.
The proxy hack was added without any unit test coverage.

**Q5: Edge cases handled / not yet handled?**

Handled:
- Entity destroyed while rotating: `TeardownEntity` (Step 1) via `DestructionOrder`
  removes the gizmo cleanly before the mask scan runs. The `onRemove` callback guards
  with `repo.IsAlive(entity)` before attempting `RemoveComponent`.
- Double-activate: Step 2b skips creating a second gizmo instance when the same
  RuleIndex is already active for the entity.
- Double-remove of marker: `onRemove` guards with `repo.HasComponent<ActiveRotationToolRequest>`
  before calling `RemoveComponent`.

Not yet handled:
- User opens rotation on entity A, then opens rotation on entity B without closing A:
  `GizmoFocusInputBridge` only pops itself on click/escape, but a second push would
  create a stack. The second bridge would capture input while the first gizmo's focus
  is still set. This needs a "cancel existing rotation" step before activating a new one.
- Undo of the rotation: `EntityRotatorGizmo` does not push to the `GizmoUndoStack`.
  This is a known limitation of the existing gizmo, not introduced by BATCH-24.
