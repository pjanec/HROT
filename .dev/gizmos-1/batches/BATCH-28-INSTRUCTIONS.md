# BATCH-28: Phase 5 - StandardInteractionTool Eradication

**Batch Number:** BATCH-28
**Phase:** Phase 5 — StandardInteractionTool Eradication
**Dependencies:** BATCH-27 (Phase 4) must be committed

---

## Onboarding & Workflow

### Developer Instructions

This batch is the most architecturally significant of the series. You are dismantling the
`StandardInteractionTool` god-class (both the Hrot wrapper and the underlying FDP tool) and
replacing all of its responsibilities with pure ECS systems and gizmos.

**Before starting:** read every file listed in Required Reading. This batch cannot be done
by guessing — you must understand the existing selection/drag pipeline.

### Required Reading (IN ORDER)

1. **Phase spec:** `.dev/gizmos-1/old-stuff-erradication.md` — Phase 5 section ("Eradicating
   the Input Router") and the Phase 5 success conditions at the bottom.
2. **Hrot tool to delete:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs`
   — understand every event it exposes and every handler it calls.
3. **FDP god-class to delete:** `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs`
   — understand `HandleHover`, `HandleClick`, `HandleDrag`, all events.
4. **EntityDragTool (Hrot):** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EntityDragTool.cs`
5. **EntityDragTool (FDP):** `FDP/Engine/Fdp.Presentation/Vis2D/Tools/EntityDragTool.cs`
6. **BoxSelectionTool (FDP):** `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs`
7. **DataDrivenGizmoSystem (routing):**
   `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`
   — particularly `RouteInteractionEvents`. Events are read from a double-buffered bus;
   multiple systems can read the same event type independently in the same frame.
8. **DebugGizmoLayer:** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
   — `HandleInput` hit-tests debug sphere primitives and pushes `GizmoInteractionProxyTool`.
9. **GizmoInteractionProxyTool:**
   `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
   — publishes `GizmoInteractionStartedEvent` on `OnEnter`, drag/commit events during drag.
10. **IEntityStatefulGizmo:**
    `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`
    and `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Interaction/IGizmoInteractionHandler.cs`
11. **SelectionState:** `Hrot/Engine/Hrot.Core/Components/Map/SelectionState.cs`
12. **GizmoInteractionEvents:**
    `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
13. **IgApplication — interaction setup section (lines 1045-1200):**
    `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
14. **SimHostVisualization — interaction section (lines 230-350):**
    `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`
15. **Existing example gizmo** (for patterns):
    `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs`
16. **Previous review:** `.dev/gizmos-1/reviews/BATCH-27-REVIEW.md`

### Source Code Locations

- FDP infrastructure: `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`
- FDP presentation: `FDP/Engine/Fdp.Presentation/`
- Hrot gizmos: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/`
- Hrot systems (NEW): `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Systems/`
- Hrot tools (DELETE): `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/`
- Hrot tests: `Hrot/Engine/Hrot.Presentation.Tests/`
- IG application: `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- SimHost visualization: `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`

### Build Command

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
```

### Test Baseline (do not regress)

- `Hrot.Presentation.Tests`: 57 passed
- `Hrot.IG.Tests`: 315 passed, 68 pre-existing failures (do not add new failures)
- `Hrot.Editor.Tests`: 95 passed
- All FDP test projects: pass

### Report Submission

Submit your report to: `.dev/gizmos-1/reports/BATCH-28-REPORT.md`

If you have questions: `.dev/gizmos-1/questions/BATCH-28-QUESTIONS.md`

---

## Context

### Current Architecture (to be deleted)

```
[User clicks on map]
     |
     v
MapCanvas.ProcessInputPipeline()
     |
     v
StandardInteractionTool (IMapTool — canvas base tool)
  - HandleHover: find entity via ISimulationView.GetEntitiesAt (SPATIAL QUERY — must go)
  - HandlePress: record mouse-down position
  - HandleClick: select entity, fire OnEntitySelectRequest / OnWorldClick
  - HandleDrag: threshold check, push EntityDragTool
  |
  +--- fires: OnEntitySelectRequest
  |         -> Hrot wrapper: updates SelectionState ECS components
  |
  +--- fires: OnWorldClick
  |         -> IgApplication: network publish (MapClickEvent, SelectionChangedEvent)
  |         -> SimHostVisualization: right-click context menu, shift+right-click waypoints
  |
  +--- fires: OnDeleteRequested
  |         -> IgApplication: publishes DestroyEntityCommand
  |
  +--- fires: OnEntityDragEnd / OnEntityMoved
            -> IgApplication: calls SendGeoSpatialUpdate
            -> SimHostVisualization: writes SimTransform, calls SmartEgressUtil.MarkDirty
```

### Target Architecture (to be built)

```
[User clicks on entity debug sphere in DebugGizmoLayer]
     |
     v
DebugGizmoLayer.HandleInput (LEFT PRESS, entity sphere hit)
     |
     v
GizmoInteractionProxyTool pushed onto canvas
  OnEnter -> publishes GizmoInteractionStartedEvent { Token.Target = entity }
  HandlePress -> _dragActive = true
  HandleDrag -> publishes GizmoDragUpdateEvent
  HandleClick (Left, after drag) -> publishes GizmoInteractionCommitEvent
  HandleClick (Left, no drag) -> publishes GizmoInteractionCancelEvent (selection already done)
  HandleClick (Right) -> publishes GizmoInteractionCancelEvent
     |
     v
FdpEventBus (double-buffered; both systems read the same events independently)
     |
     +---> DataDrivenGizmoSystem.RouteInteractionEvents
     |         GizmoInteractionStartedEvent -> EntityDragGizmo.OnInteractionStarted
     |         GizmoDragUpdateEvent -> EntityDragGizmo.OnDragUpdate (writes SimTransform)
     |         GizmoInteractionCommitEvent -> EntityDragGizmo.OnCommit (final write + publish)
     |         GizmoInteractionCancelEvent -> EntityDragGizmo.OnCancel
     |
     +---> SelectionInteractionSystem.Tick
               GizmoInteractionStartedEvent -> update SelectionState ECS components
               GizmoKeyEvent(Delete) -> publish DestroyEntityCommand for selected entities

[For entities to be clickable, each entity must emit a pick sphere into DebugGizmoLayer]
  IgEntityPresentationGizmo.Draw -> draw.DrawEntitySphere(entity, worldPos, radius)
  SimHostEntityPresentationGizmo.Draw -> same
```

### Key Constraint: Events Are Non-Destructive

`FdpEventBus.Read<T>()` reads the previous frame's event buffer. Multiple systems calling
`Read<GizmoInteractionStartedEvent>()` in the same frame all see the same events independently.
`DataDrivenGizmoSystem` and `SelectionInteractionSystem` can both read the same events safely.

### Key Constraint: Entity Pick Spheres

For entity selection to work without canvas spatial queries, each selectable entity must emit a
world-space debug sphere primitive with a valid entity anchor set. `DebugGizmoLayer.HandleInput`
hit-tests these spheres. Currently `IDebugDrawBuilder` has no method for entity-anchored spheres.
You must add `DrawEntitySphere` to the infrastructure (see Task A below).

---

## Batch Objectives

1. Delete `StandardInteractionTool` (both Hrot wrapper and FDP god-class) and all related
   tools (`EntityDragTool`, `BoxSelectionTool`) plus their test files.
2. Add `DrawEntitySphere` to `IDebugDrawBuilder` / `DebugPrimitiveBuffer` so gizmos can emit
   world-space interactive spheres with entity anchors.
3. Create `SelectionInteractionSystem` that consumes `GizmoInteractionStartedEvent` to update
   `SelectionState` ECS components, and `GizmoKeyEvent(Delete)` to destroy selected entities.
4. Create `EntityDragGizmo` (`IEntityStatefulGizmo`) that handles drag interactions, writing
   directly to `SimTransform`.
5. Update `IgEntityPresentationGizmo` and `SimHostEntityPresentationGizmo` to emit pick spheres.
6. Rewire `IgApplication` and `SimHostVisualization` to use the new systems.
7. Update tests.

---

## Tasks

### Task A: Add `DrawEntitySphere` to Infrastructure

**File:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/IDebugDrawBuilder.cs` (UPDATE)
**File:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs` (UPDATE)

Add a default no-op method to `IDebugDrawBuilder`:

```csharp
/// <summary>
/// Emits a world-space sphere primitive anchored to <paramref name="anchor"/>.
/// The sphere is hit-testable by <c>DebugGizmoLayer</c> — clicking it triggers
/// <c>GizmoInteractionStartedEvent { Token.Target = anchor }</c>.
/// Default no-op so existing stub implementations compile without changes.
/// </summary>
void DrawEntitySphere(
    Entity anchor,
    Vector3 worldCenter,
    float   radius,
    Rgba32  color,
    byte    layer = 0) { }
```

Add concrete implementation in `DebugPrimitiveBuffer`:

```csharp
public void DrawEntitySphere(
    Entity  anchor,
    Vector3 worldCenter,
    float   radius,
    Rgba32  color,
    byte    layer = 0)
{
    var p = default(DebugPrimitive);
    p.Shape            = DebugPrimitiveShape.Sphere;
    p.Space            = CoordinateSpace.World;
    p.SizeMode         = SizeMode.WorldMeters;
    p.TargetView       = PipelineTarget.Map2D;
    p.Color            = color;
    p.SphereCenter     = worldCenter;
    p.SphereRadius     = radius;
    p.DebugLayer       = layer;
    p.AnchorIndex      = anchor.Index;
    p.AnchorGeneration = anchor.Generation;
    Append(p);
}
```

`DebugPrimitive` struct fields to set (verify field names by reading the struct):
- `AnchorIndex` = anchor.Index
- `AnchorGeneration` = anchor.Generation

These are already used in `DrawEntityLocal`. The `PickToken.IsValid` check in `DebugGizmoLayer`
is `!Target.IsNull`, which returns true when `AnchorGeneration != 0`. Any live ECS entity has
non-zero generation.

**Test:** Add one test to `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs`
verifying that `DrawEntitySphere` emits a sphere with the correct anchor entity:
```csharp
// SC-PHASE5-A: DrawEntitySphere emits sphere primitive with entity anchor.
public void DrawEntitySphere_SetsAnchorAndShape()
{
    var buffer = new DebugPrimitiveBuffer(4);
    var entity = new Entity(7, 3);
    buffer.DrawEntitySphere(entity, Vector3.Zero, 5f, Rgba32.Red);
    var frames = buffer.GetFrame();
    Assert.Equal(1, frames.Length);
    Assert.Equal(DebugPrimitiveShape.Sphere, frames[0].Shape);
    var token = frames[0].GetPickToken();
    Assert.True(token.IsValid);
    Assert.Equal(entity, token.Target);
}
```

---

### Task B: Create `SelectionInteractionSystem`

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Systems/SelectionInteractionSystem.cs` (NEW)

```csharp
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.IG.Components;
using Hrot.Map.Common.Events;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ECS system that translates gizmo interaction events into <see cref="SelectionState"/>
/// component mutations.
///
/// Consumes (non-destructive read -- safe to share with DataDrivenGizmoSystem):
///   <see cref="GizmoInteractionStartedEvent"/> -- entity click: select / deselect
///   <see cref="GizmoKeyEvent"/> -- Delete key: destroy all selected entities
///
/// Replaces the selection logic formerly in
/// <c>Hrot.ScenarioEditor.Tools.StandardInteractionTool</c> (Phase 5 eradication).
/// </summary>
public sealed class SelectionInteractionSystem
{
    private readonly EntityRepository _world;

    /// <summary>
    /// Optional callback fired after selection changes. Subscribe to publish network
    /// selection-change events (e.g. SelectionChangedEventDto) without coupling
    /// this system to network infrastructure.
    /// Receives (selectedEntity, worldPos). selectedEntity == Entity.Null means
    /// empty-space click (deselect all).
    /// </summary>
    public Action<Entity, System.Numerics.Vector3>? OnSelectionChanged;

    public SelectionInteractionSystem(EntityRepository world)
    {
        _world = world;
    }

    public void Tick(float dt)
    {
        // Selection from gizmo entity clicks.
        foreach (ref readonly var evt in _world.Bus.Read<GizmoInteractionStartedEvent>())
        {
            var entity = evt.Token.Target;

            if (entity.IsNull)
            {
                // Click on empty space: deselect all.
                ClearAllSelections();
                OnSelectionChanged?.Invoke(Entity.Null, evt.WorldPos);
            }
            else if (_world.IsAlive(entity))
            {
                // TODO(P2): read Raylib shift/ctrl state for multi-select.
                // Phase 5 implements single-select only.
                ClearAllSelections();
                SetSelected(entity, isPrimary: true);
                OnSelectionChanged?.Invoke(entity, evt.WorldPos);
            }
        }

        // Delete key: destroy all currently selected entities.
        foreach (ref readonly var key in _world.Bus.Read<GizmoKeyEvent>())
        {
            if (key.Key != MapKeyboardKey.Delete || key.IsPressed) continue;

            var toDestroy = new List<Entity>();
            var q = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
            foreach (var e in q)
            {
                if (!_world.IsAlive(e)) continue;
                var s = _world.GetComponent<SelectionState>(e);
                if (!s.IsSelected && !s.IsPrimarySelection) continue;
                toDestroy.Add(e);
            }

            foreach (var e in toDestroy)
            {
                if (!_world.IsAlive(e)) continue;
                if (_world.HasComponent<NetworkIdentity>(e))
                {
                    ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(e);
                    _world.Bus.PublishManaged(new DestroyEntityCommand
                    {
                        NetworkId = netId.Value,
                        Reason    = "user-deleted",
                    });
                }
                else
                {
                    _world.DestroyEntity(e);
                }
            }

            if (toDestroy.Count > 0)
                ClearAllSelections();
        }
    }

    /// <summary>
    /// Clears all ECS SelectionState components. Call before a world reset.
    /// </summary>
    public void ClearAllSelections()
    {
        var q = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
        foreach (var e in q)
        {
            if (_world.IsAlive(e))
                _world.SetComponent(e, new SelectionState { IsSelected = false, IsPrimarySelection = false });
        }
    }

    private void SetSelected(Entity entity, bool isPrimary)
    {
        if (!_world.HasComponent<SelectionState>(entity))
            _world.AddComponent(entity, new SelectionState());
        _world.SetComponent(entity, new SelectionState
        {
            IsSelected         = true,
            IsPrimarySelection = isPrimary,
        });
    }
}
```

**Tests:** Add to `Hrot/Engine/Hrot.Presentation.Tests/SelectionInteractionSystemTests.cs` (NEW):

```csharp
// SIS-001: GizmoInteractionStartedEvent with valid entity selects it.
// SIS-002: GizmoInteractionStartedEvent with null entity clears selection.
// SIS-003: Second click clears previous selection (single-select).
// SIS-004: GizmoKeyEvent(Delete, isPressed=false) on selected entity publishes DestroyEntityCommand.
// SIS-005: GizmoKeyEvent(Delete, isPressed=true) is ignored.
// SIS-006: ClearAllSelections() deselects all live entities.
// SIS-007: OnSelectionChanged callback fires on entity click.
// SIS-008: OnSelectionChanged fires with Entity.Null on empty-space click.
```

For each test: create a minimal `EntityRepository`, register `SelectionState` and relevant events,
publish the event via `bus.Publish(...)`, call `bus.SwapBuffers()`, then call `system.Tick(0f)`.
Check component state. Pattern follows existing tests in `Hrot.Presentation.Tests`.

---

### Task C: Create `EntityDragGizmo`

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityDragGizmo.cs` (NEW)

This gizmo is registered via `GizmoRegistry` for entities with `NetworkIdentity + SimTransform`.
It handles entity drag operations, writing position directly to `SimTransform`.

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// Entity-stateful gizmo that handles spatial drag interactions for selectable map entities.
///
/// Lifecycle:
///   OnInteractionStarted -- user pressed on the entity's pick sphere; record initial position.
///   OnDragUpdate         -- user is dragging; write live position to SimTransform so the
///                           entity moves in real time. Also resets VehicleState.Speed to 0
///                           if present (entity is stationary while being dragged).
///   OnCommit             -- drag released; write final position, fire OnDragCommitted callback.
///   OnCancel             -- restore the original position recorded at OnInteractionStarted.
///   UpdateAndDraw        -- emits the entity pick sphere (makes the entity selectable via
///                           DebugGizmoLayer) and, while dragging, a preview line from the
///                           original position to the cursor.
///
/// Replaces <c>Hrot.ScenarioEditor.Tools.EntityDragTool</c> and
/// <c>Fdp.Toolkit.Vis2D.Tools.EntityDragTool</c> (Phase 5 eradication).
/// </summary>
public sealed class EntityDragGizmo : IEntityStatefulGizmo
{
    // Sphere pick radius in world metres. Must be large enough to be
    // easily clickable but not so large it overlaps adjacent entities.
    private const float PickRadius = 8f;

    private static readonly Rgba32 PickSphereColor  = new Rgba32(0, 0, 0, 0);   // transparent
    private static readonly Rgba32 DragLineColor    = new Rgba32(255, 255, 0, 200);
    private static readonly Rgba32 DragSphereColor  = new Rgba32(255, 200, 0, 180);

    private readonly ISimulationView _view;
    private readonly Entity          _entity;

    private bool    _isDragging;
    private Vector3 _originalPos;
    private Vector3 _currentDragPos;

    /// <summary>
    /// Optional callback fired after a successful drag commit.
    /// Receives the entity and its final world position.
    /// Subscribe to trigger network update (e.g. SendGeoSpatialUpdate).
    /// </summary>
    public Action<Entity, Vector2>? OnDragCommitted;

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }

    public EntityDragGizmo(ISimulationView view, Entity entity)
    {
        _view   = view;
        _entity = entity;
    }

    public void Dispose() { }

    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    // -- UpdateAndDraw ---------------------------------------------------------

    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        if (!_view.IsAlive(_entity)) return;
        if (!_view.HasComponent<SimTransform>(_entity)) return;

        ref readonly var tf = ref _view.GetComponentRO<SimTransform>(_entity);
        var worldPos = new Vector3(tf.Position.X, tf.Position.Y, 0f);

        // Emit transparent pick sphere so DebugGizmoLayer can hit-test this entity.
        draw.DrawEntitySphere(_entity, worldPos, PickRadius, PickSphereColor);

        // While dragging: show a yellow preview line from original to current position.
        if (_isDragging)
        {
            draw.DrawLine(_originalPos, _currentDragPos, DragLineColor, thickness: 2f);
            draw.DrawSphere(_currentDragPos, 5f, DragSphereColor);
        }
    }

    // -- IGizmoInteractionHandler ---------------------------------------------

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
    {
        if (!_view.IsAlive(_entity) || !_view.HasComponent<SimTransform>(_entity)) return;
        ref readonly var tf = ref _view.GetComponentRO<SimTransform>(_entity);
        _originalPos    = new Vector3(tf.Position.X, tf.Position.Y, 0f);
        _currentDragPos = _originalPos;
        _isDragging     = false;   // drag starts only when OnDragUpdate fires
    }

    public void OnDragUpdate(Vector3 worldPos)
    {
        if (!_view.IsAlive(_entity)) return;
        _isDragging     = true;
        _currentDragPos = worldPos;
        ApplyPosition(worldPos);
    }

    public void OnCommit(Vector3 worldPos)
    {
        if (!_view.IsAlive(_entity)) return;
        _isDragging = false;
        ApplyPosition(worldPos);
        OnDragCommitted?.Invoke(_entity, new Vector2(worldPos.X, worldPos.Y));
    }

    public void OnCancel()
    {
        if (!_view.IsAlive(_entity)) return;
        _isDragging = false;
        ApplyPosition(_originalPos);
    }

    public void OnMenuAction(int actionId) { }
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }

    // -- Helpers ---------------------------------------------------------------

    private void ApplyPosition(Vector3 worldPos)
    {
        if (_view is not EntityRepository repo) return;
        if (!repo.IsAlive(_entity) || !repo.HasComponent<SimTransform>(_entity)) return;

        ref var tf = ref repo.GetComponentRW<SimTransform>(_entity);
        tf.Position = new Vector3(worldPos.X, worldPos.Y, tf.Position.Z);

        // Reset speed to 0 while the entity is being repositioned manually.
        if (repo.HasComponent<VehicleState>(_entity))
        {
            ref var vs = ref repo.GetComponentRW<VehicleState>(_entity);
            vs.Speed = 0;
        }
    }
}
```

**GizmoDefinition:** Create `EntityDragGizmoDefinition` in the same file (or a separate one) so
it can be registered:

```csharp
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>Wires EntityDragGizmo into the GizmoRegistry.</summary>
public sealed class EntityDragGizmoDefinition : IGizmoDefinition
{
    public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

    public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        => new EntityDragGizmo(view, entity);
}
```

You will need to look up how other definitions (e.g. `VertexEditGizmoDefinition`) handle
`IGizmoVisibilityPolicy` and what base class / interface patterns to follow. Use
`VertexEditGizmoDefinition.cs` as your model.

**Tests:** Add to `Hrot/Engine/Hrot.Presentation.Tests/EntityDragGizmoTests.cs` (NEW):

```csharp
// EDG-001: UpdateAndDraw emits a sphere primitive with valid entity pick token.
// EDG-002: OnDragUpdate writes to SimTransform.Position.
// EDG-003: OnCommit writes final position and fires OnDragCommitted.
// EDG-004: OnCancel restores original position.
// EDG-005: OnDragUpdate on dead entity is a no-op (no crash).
// EDG-006: OnCommit resets VehicleState.Speed when component present.
```

---

### Task D: Update Entity Presentation Gizmos to Emit Pick Spheres

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/IgEntityPresentationGizmo.cs` (UPDATE)

In `IgEntityPresentationGizmo.Draw`, AFTER the existing `DrawSpatialAnchor` call, add:

```csharp
// Emit transparent pick sphere so DebugGizmoLayer can hit-test this entity for selection.
// EntityDragGizmo also emits this, but IgEntityPresentationGizmo is stateless (runs every
// entity without a full gizmo lifecycle), so both are needed for the IG path.
// Note: IgEntityPresentationGizmo is stateless; it cannot hold IsFocused state.
// The pick sphere radius must match EntityDragGizmo.PickRadius.
draw.DrawEntitySphere(entity, new Vector3(tf.Position.X, tf.Position.Y, 0f), 8f, new Rgba32(0, 0, 0, 0));
```

**File:** `Hrot/Subsystems/Hrot.SimHost/Gizmos/SimHostEntityPresentationGizmo.cs` (UPDATE)

Apply the same change — emit a pick sphere after `DrawSpatialAnchor`:
```csharp
draw.DrawEntitySphere(entity, new Vector3(tf.Position.X, tf.Position.Y, 0f), 8f, new Rgba32(0, 0, 0, 0));
```

Read both files fully before editing. Preserve all existing comments exactly.
Check which parameters `DrawEntitySphere` takes; ensure `Entity entity` is available
in the `Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)` signature.

---

### Task E: Register `EntityDragGizmo` in GizmoRegistry

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs` (UPDATE — GizmoRegistry setup)

Find where `_gizmoRegistry!.Register(new VertexEditGizmoDefinition())` is called (around
line 1080). Add the new definition **before** `_gizmoRegistry!.Register(new VertexEditGizmoDefinition())`:

```csharp
_gizmoRegistry!.Register(new EntityDragGizmoDefinition());
```

You will also need to register it for SimHost. Find where
`Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll(...)` is called (in SimHost's
composition root — read the SimHost setup code). The `EntityDragGizmoDefinition` should
be registered there as well, OR via a `[GizmoProjector]` attribute on `EntityDragGizmo`
that uses `typeof(NetworkIdentity), typeof(SimTransform)`. Check how `VertexEditGizmoDefinition`
is registered — either via `GizmoRegistrar.Register(new VertexEditGizmoDefinition())` or via
source-generated code. Use the same pattern.

---

### Task F: Delete Legacy Files

Delete ALL of the following files (use the terminal or IDE):

**Hrot tools:**
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionTool.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/StandardInteractionToolConstants.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EntityDragTool.cs`

**FDP tools (god-class and helpers):**
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/StandardInteractionTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/EntityDragTool.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Tools/BoxSelectionTool.cs`

**FDP tool tests (all tests for deleted classes):**
- `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/StandardInteractionToolTests.cs`
- `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/EntityDragToolTests.cs`
- `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Tools/BoxSelectionToolTests.cs`

After deletion, run `dotnet build` to find all compilation errors caused by these deletions.
Fix each error by following the instructions in Tasks G and H.

---

### Task G: Rewire `IgApplication.cs`

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs` (UPDATE)

Read lines 1045-1200 carefully to understand every line before changing it.

**Step G.1 — Remove the alias:**
Remove the using alias at the top of the file:
```csharp
// REMOVE THESE LINES:
// Disambiguate StandardInteractionTool: both Hrot.IG.Tools and FDP.Toolkit.Vis2D.Tools define it.
// Use the Hrot.IG variant which exposes OnWorldClick.
using StandardInteractionTool = Hrot.ScenarioEditor.Tools.StandardInteractionTool;
```

**Step G.2 — Remove the StandardInteractionTool block:**

Find the block that begins:
```csharp
        // StandardInteractionTool -- default canvas tool wiring selection to ECS.

        var interactionTool = new StandardInteractionTool(_world, query, selection);

        _canvas.SwitchTool(interactionTool);

        interactionTool.OnWorldClick += OnCanvasWorldClick;
```

Replace the ENTIRE block from that comment through the closing brace of the `if (_networkEnabled)` block that includes `OnEntityMoved` subscription, with:

```csharp
        // Phase 5: selection and drag handled by SelectionInteractionSystem + EntityDragGizmo.
        // Canvas no longer has a base tool for entity picking.

        _selectionSystem = new SelectionInteractionSystem(_world);

        // When a network-enabled entity is clicked, also publish MapClickEvent and
        // SelectionChangedEvent so that ExCon can track map selections.
        if (_networkEnabled)
        {
            _selectionSystem.OnSelectionChanged += (entity, worldPos) =>
            {
                OnCanvasClicked(new System.Numerics.Vector2(worldPos.X, worldPos.Y),
                    MapMouseButton.Left, false, false, entity, updateSelection: true);
            };
        }

        _kernel.RegisterGlobalSystem(new SelectionInteractionSystemAdapter(_selectionSystem));
```

Where `SelectionInteractionSystemAdapter` is a small private nested class (see Step G.3).

You also need to REMOVE these lines (the EntityQuery and DefaultSelectionState are no longer needed by the interaction tool):
```csharp
        // D. Entity query for the StandardInteractionTool (selection/picking).
        // Area-overlay and route entities are excluded from the interaction query
        // so that clicking on them does not accidentally select non-tactical entities.
        var query = _world.Query()
            .With<NetworkIdentity>()
            .With<SimTransform>()
            .Without<MapOverlayStyle>()
            .WithoutManaged<Hrot.Map.Common.Components.RoutePlan>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();

        var selection = new DefaultSelectionState();
```

And remove the `selection` usages. Check if `selection` is used elsewhere in the method
(it is only used to pass to `StandardInteractionTool`). The `SelectionRenderSystem` and
`_selectionStateQuery` remain unchanged.

**Step G.3 — Add adapter for kernel registration:**

`SelectionInteractionSystem` is a POJO (plain C# class), not a kernel system interface.
Add a small private nested adapter inside `IgApplication` (or extract to a separate file in
`Hrot.Presentation/ScenarioEditor/Systems/`) that wraps it:

```csharp
private sealed class SelectionInteractionSystemAdapter : IEcsModuleSystem
{
    private readonly SelectionInteractionSystem _system;
    public SelectionInteractionSystemAdapter(SelectionInteractionSystem system)
        => _system = system;
    public void Execute(ISimulationView view, float deltaTime)
        => _system.Tick(deltaTime);
}
```

Look at `IgApplication.cs` around line 4100 for an existing example of this pattern (there is
one already). Use the same pattern.

**Step G.4 — Add `_selectionSystem` field:**

Find the field declarations section in `IgApplication` and add:
```csharp
    private SelectionInteractionSystem? _selectionSystem;
```

**Step G.5 — Update `OnDeleteRequested` logic:**

The old `StandardInteractionTool.OnDeleteRequested` lambda is now gone. Its logic has moved
into `SelectionInteractionSystem.Tick`. The entity deletion on Delete key press is now handled
there. No additional wiring needed in IgApplication.

**Step G.6 — Update `OnEntityDragEnded` wiring:**

Find the `if (_networkEnabled)` block that subscribes:
```csharp
interactionTool.OnEntityDragEnd += OnEntityDragEnded;
interactionTool.OnEntityMoved += (entity, worldPos) => { ... };
```

These subscriptions must be replaced with wiring via `EntityDragGizmo.OnDragCommitted`.
However, `EntityDragGizmo` instances are created per-entity by `DataDrivenGizmoSystem`, so
you cannot subscribe before they are created.

For Phase 5, use the following approach: register the `EntityDragGizmoDefinition` with a
factory lambda that injects the callback:

```csharp
if (_networkEnabled)
{
    _gizmoRegistry!.Register(
        new EntityDragGizmoDefinition(onDragCommitted: (entity, worldPos) =>
        {
            _lastDragWorldPos = worldPos;
            OnEntityDragEnded(entity);
        }));
}
else
{
    _gizmoRegistry!.Register(new EntityDragGizmoDefinition());
}
```

This requires `EntityDragGizmoDefinition` to accept an optional `Action<Entity, Vector2>?
onDragCommitted` parameter and inject it into each `EntityDragGizmo` instance. Update
the definition accordingly.

**Step G.7 — Update `OnCanvasWorldClick` subscriptions:**

The old `interactionTool.OnWorldClick += OnCanvasWorldClick` is gone. Right-click context menus
are now handled by `DebugGizmoLayer.HandleRightClick` (which already fires context menus via
`Box2D` primitives). For shift+right-click waypoints and right-click context menus that do NOT
use a Box2D context-menu binding, we accept this as P2 debt. The IG test hooks (`TestHook_SimulateMapClick`, `TestHook_SimulateEntityClick`) remain unchanged since they call `OnCanvasClicked` directly.

**Step G.8 — Continuous drag updates:**

The old `OnEntityMoved` subscribed to send `SendGeoSpatialUpdate` every throttled interval.
In Phase 5, the `EntityDragGizmo.OnDragCommitted` fires only once on drop. Continuous drag
updates are deferred to P2. The old `_userConfig.ContinuousDragUpdates` path and
`_continuousDragTimer` are no longer used by the interaction path (they may still be
used elsewhere; do NOT remove them if they are referenced elsewhere in `IgApplication`).

---

### Task H: Rewire `SimHostVisualization.cs`

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs` (UPDATE)

Read lines 230-350 carefully before editing.

**Step H.1 — Remove field:**
```csharp
    private StandardInteractionTool?  _interactionTool;
```
Remove this field declaration.

**Step H.2 — Remove using:**
Remove `using Fdp.Toolkit.Vis2D.Tools;` if it is only used for `StandardInteractionTool`.
Check if anything else uses types from that namespace.

**Step H.3 — Replace the interaction tool block:**

Find the block that begins:
```csharp
            _interactionTool = new StandardInteractionTool(repo, _vehicleQuery);
```

And ends at:
```csharp
            _map.SwitchTool(_interactionTool);
```
(including all the event subscriptions `OnEntitySelectRequest`, `OnEntityMoved`,
`OnRegionSelected`, `OnWorldClick`, `OnDeleteRequested` between these lines)

Replace the ENTIRE block with:

```csharp
            // Phase 5: entity selection via SelectionInteractionSystem;
            // entity drag via EntityDragGizmo registered in DataDrivenGizmoSystem.
            var selectionSystem = new SelectionInteractionSystem(repo);

            // Sync selection to SimHostSelectionManager and FDP inspector.
            selectionSystem.OnSelectionChanged += (entity, worldPos) =>
            {
                if (entity == Entity.Null)
                {
                    _selection!.Clear();
                    _fdpInspectorState.SelectedEntity = null;
                }
                else if (repo.IsAlive(entity))
                {
                    _selection!.Set(entity);
                    _fdpInspectorState.SelectedEntity = entity;
                }
            };

            _kernel.RegisterGlobalSystem(new LambdaEcsModuleSystem(
                (view, dt) => selectionSystem.Tick(dt)));
```

`LambdaEcsModuleSystem` is a small helper. Look for existing examples of wrapping a POJO
as an `IEcsModuleSystem` in `SimHostVisualization.cs` or its callers. If none exists, create
a small private nested class similar to the adapter in `IgApplication`.

The old subscriptions that are now gone:
- `OnEntitySelectRequest` → `SelectionInteractionSystem` handles this
- `OnEntityMoved` → `EntityDragGizmo.ApplyPosition` handles this
- `OnRegionSelected` → P2 debt (box selection not in Phase 5)
- `OnWorldClick` → context menu via `DebugGizmoLayer.HandleRightClick`; waypoints P2 debt
- `OnDeleteRequested` → `SelectionInteractionSystem.Tick` handles GizmoKeyEvent(Delete)

**Step H.4 — Remove `_vehicleQuery` field if only used for StandardInteractionTool:**

Check if `_vehicleQuery` is used anywhere else in `SimHostVisualization`. If the only usage
was to pass to `StandardInteractionTool`, remove the field and its initialization.

**Step H.5 — Inject `OnDragCommitted` callback:**

The old `OnEntityMoved` handler wrote to `SimTransform` AND called `SmartEgressUtil.MarkDirty`.
In Phase 5, `EntityDragGizmo.ApplyPosition` writes to `SimTransform` directly. For the
`SmartEgressUtil.MarkDirty` call, either:

(a) Call `SmartEgressUtil.MarkDirty` inside `EntityDragGizmo.ApplyPosition` if
    `_worldPosDescriptorId` can be injected, OR
(b) Subscribe to `EntityDragGizmo.OnDragCommitted` from `SimHostVisualization` via the
    definition factory (same pattern as Task G.6).

For option (b), update `EntityDragGizmoDefinition` to accept an optional factory callback
that is called on each new gizmo instance. Then in SimHostVisualization:

```csharp
if (_gizmoSystem != null)
{
    // EntityDragGizmo handles drag; wire OnDragCommitted for SmartEgressUtil.
    // Note: _gizmoSystem is the DataDrivenGizmoSystem registered by the caller.
    // We cannot inject into already-registered definitions here; this requires
    // SimHostVisualization's caller to pass the factory. Accept P2 debt:
    // SmartEgressUtil.MarkDirty is not called on drag in Phase 5; egressTranslator
    // detects SimTransform delta organically.
}
```

For Phase 5, accept that `SmartEgressUtil.MarkDirty` is NOT called during drag. The
`GeoSpatialEgressTranslator` should detect the `SimTransform` change and publish a network
update. If it does not, mark as P1 debt in the report.

---

### Task I: Update Tests

**File:** `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs` (UPDATE)

In `ScenarioEditor_Assembly_ContainsAllToolTypes()`:

Change:
```csharp
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionTool"));
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionToolConstants"));
```

To:
```csharp
// Phase 5: StandardInteractionTool and constants deleted.
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionTool"));
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionToolConstants"));
```

Also add assertions for the new types:
```csharp
// Phase 5 additions.
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Systems.SelectionInteractionSystem"));
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Gizmos.EntityDragGizmo"));
```

**File:** `Hrot/Engine/Hrot.Presentation.Tests/WorldResetTests.cs` (UPDATE)

Rewrite `FlushForWorldReset_ClearsSelection()` to test `SelectionInteractionSystem`:

```csharp
[Fact]
public void SelectionInteractionSystem_ClearAllSelections_ResetsEcsState()
{
    // Arrange
    var world = new EntityRepository();
    world.RegisterComponent<SelectionState>();
    var entity = world.CreateEntity();
    world.AddComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

    var system = new SelectionInteractionSystem(world);

    // Act
    system.ClearAllSelections();

    // Assert
    var state = world.GetComponent<SelectionState>(entity);
    Assert.False(state.IsSelected);
    Assert.False(state.IsPrimarySelection);
}
```

Remove the test `FlushForWorldReset_ClearsSelection()` entirely and replace with the above.
Keep `WorldResetEvent_IsPlainClass()` unchanged.

**File:** `Hrot/Subsystems/Hrot.ExCon.Tests/ExConUiPackBoundaryTests.cs` (UPDATE)

In the `ForbiddenTypeNames` array, remove `"StandardInteractionTool"`:

Change:
```csharp
private static readonly string[] ForbiddenTypeNames =
{
    "CreationTool",
    "EditTool",
    "RouteEditTool",
    "MeasureTool",
    "StandardInteractionTool",
};
```

To:
```csharp
private static readonly string[] ForbiddenTypeNames =
{
    "CreationTool",
    "EditTool",
    "RouteEditTool",
    "MeasureTool",
};
```

---

## Technical Debt (P2 — Do NOT block Phase 5 completion)

The following items are intentionally deferred. Document them in your report:

1. **P2: Shift/Ctrl multi-select.** `SelectionInteractionSystem` is single-select only.
   Add modifier key reading (Raylib or canvas input) in a follow-up batch.

2. **P2: Box selection.** `BoxSelectionTool` is deleted but `BoxSelectionGizmo` is not
   created. Box-select gesture (drag on empty space) is non-functional in Phase 5.

3. **P2: Shift+right-click waypoints.** `OnWorldClick(shift=true, Right)` → waypoints path
   is gone. Needs canvas-level right-click event or a new mechanism.

4. **P2: Deselect on empty-space click.** Clicking on empty canvas space does not deselect.
   `GizmoInteractionStartedEvent` is only published when a gizmo primitive is hit. Fix by
   modifying `DebugGizmoLayer.HandleInput` to publish a null-token event when no primitive
   is hit (requires updating the SC-GZ025-3 test expectation).

5. **P2: `SmartEgressUtil.MarkDirty` on drag.** Drag position is written to SimTransform but
   `SmartEgressUtil.MarkDirty` is not explicitly called; relies on egressTranslator auto-detection.
   Verify this works; if not, wire `OnDragCommitted` through the definition factory.

6. **P3: Continuous drag updates.** Old `_userConfig.ContinuousDragUpdates` path fired network
   updates every throttle interval during drag. Now only fires on commit. Add an optional
   `OnDragUpdated` callback to `EntityDragGizmo` for this.

---

## Testing Requirements

After completing all tasks, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
```

Zero build errors required.

Then run tests:
```powershell
dotnet test IOS-IG-SimHost.sln -c Debug --no-build --nologo -v q
```

Required test outcomes:
- `Hrot.Presentation.Tests`: >= 57 passed (will increase with new tests)
- `Hrot.IG.Tests`: 315 passed, <= 68 failures (baseline; no new failures)
- `Hrot.Editor.Tests`: >= 95 passed
- All FDP tests: same counts as before (the deleted test files reduce count; ensure no other FDP tests broke)

---

## Report Requirements

Submit `.dev/gizmos-1/reports/BATCH-28-REPORT.md` covering:

- **Files changed, added, deleted** (complete list)
- **Build errors encountered and how resolved**
- **Tests added** (names and IDs)
- **P2 debt items confirmed** — was `SmartEgressUtil.MarkDirty` confirmed to work without explicit call?
- **Design decisions** — how did you handle `EntityDragGizmoDefinition` factory injection?
- **Issues encountered** — any deviation from these instructions and why?
- **Suggested commit message**
