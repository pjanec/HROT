# BATCH-25: Phase 2 — Purge Geometry Manipulation Tools

**Batch Number:** BATCH-25
**Phase:** Phase 2 — Purging Geometry Manipulation Tools
**Estimated Effort:** 4-6 hours
**Dependencies:** BATCH-24 (Phase 1 complete and committed)

---

## Onboarding & Workflow

### Developer Instructions

Phase 1 migrated `EntityRotationTool` to `EntityRotatorGizmo`. Phase 2 targets the two
remaining canvas-tool-based geometry editors:

- **`EditTool`** — drags vertices of `EditablePolyline` overlays (area boundaries, etc.)
- **`RouteEditTool`** — drags waypoints of `RoutePlan` route entities

Both are replaced by `IEntityStatefulGizmo` implementations that use ECS marker
components and SubElementId-based vertex hit-testing. The canvas tool stack is NOT
redesigned in this phase (Phase 5 task). Instead, the new gizmos use `RequiresExclusiveFocus = false`
and rely on `DebugGizmoLayer` hit-testing of Box2D handles to route interactions.

**Pass condition:** `EditTool.cs`, `RouteEditTool.cs`, `EditToolConstants.cs`,
`RouteEditToolConstants.cs` are **physically deleted** from the repository and the
solution builds with all tests passing.

### Required Reading (IN ORDER)

1. `.dev/gizmos-1/old-stuff-erradication.md` — Phase 2 spec (read Phase 2 section only)
2. `Hrot/Subsystems/Hrot.SimHost/Gizmos/GizmoActivationMarkers.cs` — Pattern for marker components
3. `Hrot/Subsystems/Hrot.SimHost/Gizmos/EntityRotatorGizmo.cs` — Pattern for IEntityStatefulGizmo
4. `Hrot/Subsystems/Hrot.SimHost/Gizmos/EntityRotatorGizmoDefinition.cs` — Pattern for IGizmoDefinition
5. `FDP/ExtDeps/GizmoMap/GizmoMap.Example/Gizmos/VertexEditGizmo.cs` — Box2D handle emission pattern

### Key Design References

- **Marker components** live in `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/`
  (namespace `Hrot.ScenarioEditor.Gizmos`). This is the `Hrot.Presentation` project.
- **Box2D handles** are emitted via `draw.EmitRaw(in prim)` — see GizmoMap.Example VertexEditGizmo
  for the exact `default(DebugPrimitive)` initialization pattern.
- **ECS routing:** Use `prim.AnchorIndex = entity.Index` and `prim.AnchorGeneration = (ushort)entity.Generation`
  (NOT `prim.BoxAnchorId` which is for the GizmoMap.Contracts path).
- **Commit behavior:** For `RequiresExclusiveFocus = false` gizmos, each vertex drag is one
  interaction session (Started → DragUpdate → Commit/Cancel). The marker stays between sessions
  so the operator can drag multiple vertices.
- **Context menus:** The gizmo calls `draw.DrawContextMenuBinding(networkId, menuJson)` each frame
  so that right-clicking a vertex handle shows insert/delete options. The gizmo handles the chosen
  action in `OnMenuAction(int actionId)`.

### Source Code Location

- **New gizmos:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/`
- **HrotComponentIds:** `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`
- **EditorSubsystem:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- **IgApplication:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- **WaypointEditorPanel:** `Hrot/Subsystems/Hrot.IG/UI/WaypointEditorPanel.cs`
- **Tests to update:** `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs`
  and `Hrot/Subsystems/Hrot.IG.Tests/WaypointEditorPanelTests.cs`
- **Tests to DELETE:** `Hrot/Subsystems/Hrot.IG.Tests/EditToolTests.cs`
  and `Hrot/Subsystems/Hrot.IG.Tests/RouteEditToolTests.cs`

### Report Submission

When done, submit your report to:
`.dev/gizmos-1/reports/BATCH-25-REPORT.md`

If you have questions, create:
`.dev/gizmos-1/questions/BATCH-25-QUESTIONS.md`

---

## Task 1: Add Component IDs

**File:** `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`

Add two new IDs after `ActiveRotationToolRequest = 186`:

```csharp
public const byte ActiveVertexEditRequest = 187;
public const byte ActiveRouteEditRequest  = 188;
```

---

## Task 2: Create Marker Components

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/ActiveEditMarkers.cs`

```csharp
using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Zero-byte ECS marker. Adding this to an entity signals DataDrivenGizmoSystem
    // to instantiate VertexEditGizmo for the entity's EditablePolyline.
    // Removed by VertexEditGizmoDefinition.onRemove when the interaction ends.
    [ComponentId(HrotComponentIds.ActiveVertexEditRequest)]
    public struct ActiveVertexEditRequest { }

    // Zero-byte ECS marker. Adding this to an entity signals DataDrivenGizmoSystem
    // to instantiate RouteWaypointGizmo for the entity's RoutePlan.
    // Removed by RouteWaypointGizmoDefinition.onRemove when the interaction ends.
    [ComponentId(HrotComponentIds.ActiveRouteEditRequest)]
    public struct ActiveRouteEditRequest { }
}
```

---

## Task 3: Create IRouteWaypointEditorState Interface

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/IRouteWaypointEditorState.cs`

```csharp
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Interface that lets WaypointEditorPanel read per-waypoint state from
    // RouteWaypointGizmo without depending on the concrete gizmo class.
    // WaypointEditorPanel receives a Func<IRouteWaypointEditorState?> in its
    // constructor and calls it each frame to get the currently active gizmo state.
    public interface IRouteWaypointEditorState
    {
        // Index of the vertex currently selected for editing, or -1 if none.
        int SelectedVertexIndex { get; }

        // Returns a ref to the selected waypoint so the panel can mutate
        // TargetSpeed and ExtensionJson in-place (same pattern as RouteEditTool).
        ref RouteWaypoint GetSelectedWaypointRef();
    }
}
```

---

## Task 4: Create VertexEditGizmo

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/VertexEditGizmo.cs`

Non-exclusive-focus gizmo for dragging `EditablePolyline` vertices.

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Non-exclusive-focus gizmo that lets the operator drag individual vertices of
    // an EditablePolyline entity. One vertex drag = one interaction session.
    // The marker stays between sessions so multiple vertices can be edited in sequence.
    //
    // Design:
    // - RequiresExclusiveFocus = false: DebugGizmoLayer hit-tests Box2D handles and
    //   pushes GizmoInteractionProxyTool which routes Started/DragUpdate/Commit/Cancel.
    // - SubElementId = vertexIndex + 1 (0 is reserved as "no handle").
    // - AnchorIndex / AnchorGeneration encode the ECS Entity (not BoxAnchorId).
    // - OnCommit: writes back relative points to EditablePolyline, publishes UpdateEntityCommand.
    // - OnCancel: reverts the dragged vertex.
    // - OnMenuAction(1): insert a new vertex after the active one.
    // - OnMenuAction(2): delete the active vertex.
    // - The gizmo does NOT call _onRemove() on its own (marker stays for multiple drags).
    //   _onRemove() is provided by the definition and removes ActiveVertexEditRequest
    //   when called from outside (e.g. tool switch, entity lifecycle).
    public sealed class VertexEditGizmo : IEntityStatefulGizmo
    {
        // Context menu JSON: array format required by ContextMenuAdapter.
        private static readonly string MenuJson =
            "[{\"id\":1,\"label\":\"Insert point after\"},{\"id\":2,\"label\":\"Delete point\"}]";

        private static readonly Rgba32 IdleColor   = new Rgba32(0, 210, 120, 220);
        private static readonly Rgba32 ActiveColor = Rgba32.Red;

        private readonly EntityRepository _repo;
        private readonly Entity           _entity;
        private readonly long             _networkId;
        private readonly Action           _onRemove;
        private readonly Vector2          _originOffset;

        // Working copy in ABSOLUTE world space (= relative Points + origin).
        private readonly List<Vector2> _points;

        // Index of the vertex being dragged (-1 = none).
        private int    _activeVertex = -1;
        private Vector2 _savedPos;

        private bool _active = true;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public VertexEditGizmo(ISimulationView view, Entity entity, long networkId, Action onRemove)
        {
            _repo      = view as EntityRepository
                ?? throw new ArgumentException("VertexEditGizmo requires EntityRepository access.", nameof(view));
            _entity    = entity;
            _networkId = networkId;
            _onRemove  = onRemove ?? throw new ArgumentNullException(nameof(onRemove));

            _originOffset = Vector2.Zero;
            if (_repo.HasComponent<SimTransform>(_entity))
            {
                ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(_entity);
                _originOffset = new Vector2(tf.Position.X, tf.Position.Y);
            }

            // Load current points into working copy in absolute world coords.
            _points = new List<Vector2>();
            if (_repo.HasManagedComponent<EditablePolyline>(_entity))
            {
                var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
                if (poly.Points != null)
                {
                    foreach (var p in poly.Points)
                        _points.Add(_originOffset + p);
                }
            }
        }

        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active || _points.Count == 0) return;

            // ContextMenuBinding so right-clicking a vertex handle shows the insert/delete menu.
            draw.DrawContextMenuBinding(_networkId, MenuJson);

            // Box2D handle for each vertex.
            for (int i = 0; i < _points.Count; i++)
            {
                bool isActive = (i == _activeVertex);
                var prim = default(DebugPrimitive);
                prim.Shape            = DebugPrimitiveShape.Box2D;
                prim.Space            = CoordinateSpace.World;
                prim.TargetView       = PipelineTarget.Map2D;
                prim.BoxCenterX       = _points[i].X;
                prim.BoxCenterY       = _points[i].Y;
                prim.BoxExtentX       = 8f;
                prim.BoxExtentY       = 8f;
                prim.Color            = isActive ? ActiveColor : IdleColor;
                prim.SubElementId     = (ushort)(i + 1);
                prim.AnchorIndex      = _entity.Index;
                prim.AnchorGeneration = (ushort)_entity.Generation;
                prim.InspNetworkId    = _networkId;
                draw.EmitRaw(in prim);
            }
        }

        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
        {
            int idx = (int)token.SubElementId - 1;
            if (idx < 0 || idx >= _points.Count) return;
            _activeVertex = idx;
            _savedPos     = _points[idx];
        }

        public void OnDragUpdate(Vector3 worldPos)
        {
            if (_activeVertex < 0 || _activeVertex >= _points.Count) return;
            _points[_activeVertex] = new Vector2(worldPos.X, worldPos.Y);
        }

        public void OnCommit(Vector3 worldPos)
        {
            // Finalize the drag. Do NOT call _onRemove; marker stays for more vertex edits.
            WriteBackAndPublish();
            _activeVertex = -1;
        }

        public void OnCancel()
        {
            if (_activeVertex >= 0 && _activeVertex < _points.Count)
                _points[_activeVertex] = _savedPos;
            _activeVertex = -1;
        }

        public void OnMenuAction(int actionId)
        {
            if (_activeVertex < 0 || _activeVertex >= _points.Count) return;

            if (actionId == 1) // Insert after
            {
                int next = (_points.Count == 1) ? 0 : (_activeVertex + 1) % _points.Count;
                var mid  = (_points[_activeVertex] + _points[next]) * 0.5f;
                _points.Insert(_activeVertex + 1, mid);
                WriteBackAndPublish();
            }
            else if (actionId == 2) // Delete
            {
                _points.RemoveAt(_activeVertex);
                _activeVertex = -1;
                WriteBackAndPublish();
            }
        }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
        public void Dispose() { }

        // ---- private helpers ---------------------------------------------------

        private void WriteBackAndPublish()
        {
            if (!_repo.IsAlive(_entity)) return;

            var relPoints = new List<Vector2>(_points.Count);
            foreach (var p in _points)
                relPoints.Add(p - _originOffset);

            var updatedPolyline = new EditablePolyline { Points = relPoints };
            _repo.SetManagedComponent(_entity, updatedPolyline);

            _repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId          = _networkId,
                ComponentsToUpdate = new List<object> { updatedPolyline },
                RequestId          = Guid.NewGuid(),
            });
        }
    }
}
```

---

## Task 5: Create VertexEditGizmoDefinition

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/VertexEditGizmoDefinition.cs`

```csharp
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // IGizmoDefinition for interactive EditablePolyline vertex editing.
    // Activated by DataDrivenGizmoSystem when SimTransform + ActiveVertexEditRequest
    // are both present on an entity.
    // Only instantiates a gizmo if the entity also has an EditablePolyline managed component.
    public sealed class VertexEditGizmoDefinition : IGizmoDefinition
    {
        public Type[] RequiredComponents { get; } =
        {
            typeof(SimTransform),
            typeof(ActiveVertexEditRequest),
        };

        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository
                ?? throw new ArgumentException(
                    $"{nameof(VertexEditGizmoDefinition)}.CreateInstance requires " +
                    $"direct EntityRepository access, not {view.GetType().Name}.");

            // Only create a gizmo for entities that actually have an EditablePolyline.
            if (!repo.HasManagedComponent<EditablePolyline>(entity))
                return new NullGizmo();

            long networkId = 0;
            if (repo.HasComponent<NetworkIdentity>(entity))
                networkId = repo.GetComponentRO<NetworkIdentity>(entity).Value;

            return new VertexEditGizmo(
                view,
                entity,
                networkId,
                onRemove: () =>
                {
                    if (repo.IsAlive(entity) && repo.HasComponent<ActiveVertexEditRequest>(entity))
                        repo.RemoveComponent<ActiveVertexEditRequest>(entity);
                });
        }
    }
}
```

> **Note:** `NullGizmo` is a private no-op gizmo that does nothing. Define it at the bottom of the file or in the same file:
> ```csharp
> // No-op gizmo returned when the entity lacks EditablePolyline (safety guard).
> private sealed class NullGizmo : IEntityStatefulGizmo
> {
>     public bool RequiresExclusiveFocus => false;
>     public bool IsFocused { get; private set; }
>     public void SetFocus(bool f) => IsFocused = f;
>     public void UpdateAndDraw(float dt, IDebugDrawBuilder draw) { }
>     public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
>     public void OnDragUpdate(Vector3 worldPos) { }
>     public void OnCommit(Vector3 worldPos) { }
>     public void OnCancel() { }
>     public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
>     public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
>     public void OnMenuAction(int actionId) { }
>     public void Dispose() { }
> }
> ```

---

## Task 6: Create RouteWaypointGizmo

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteWaypointGizmo.cs`

Non-exclusive-focus gizmo for dragging `RoutePlan` waypoints.
Also implements `IRouteWaypointEditorState` so `WaypointEditorPanel` can read its state.

Key coordinate mapping: `RouteWaypoint.Position` uses `X = East`, `Z = North`.
On the 2D map canvas, `X = East`, `Y = North`. So: `mapX = waypoint.Position.X`, `mapY = waypoint.Position.Z`.

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Non-exclusive-focus gizmo that lets the operator drag individual waypoints of
    // a RoutePlan entity. Implements IRouteWaypointEditorState so WaypointEditorPanel
    // can read per-waypoint TargetSpeed and ExtensionJson without depending on this class.
    //
    // Coordinate convention:
    //   RouteWaypoint.Position = (X=East, Y=?, Z=North) -- same ENU as SimTransform.
    //   2D map canvas = (X=East, Y=North). So worldPos.X -> waypoint.Position.X,
    //   worldPos.Y -> waypoint.Position.Z.
    //
    // Static Current: exposes the active gizmo instance so WaypointEditorPanel can
    // bind to it without requiring a DI lookup. Set on construction, cleared on Dispose.
    public sealed class RouteWaypointGizmo : IEntityStatefulGizmo, IRouteWaypointEditorState
    {
        // Context menu JSON: insert / delete waypoint.
        private static readonly string MenuJson =
            "[{\"id\":1,\"label\":\"Insert waypoint after\"},{\"id\":2,\"label\":\"Delete waypoint\"}]";

        private static readonly Rgba32 IdleColor   = new Rgba32(0, 160, 255, 220);
        private static readonly Rgba32 ActiveColor = Rgba32.Red;

        // Tracks the single active instance so WaypointEditorPanel can bind.
        public static RouteWaypointGizmo? Current { get; private set; }

        private readonly EntityRepository    _repo;
        private readonly Entity              _entity;
        private readonly long                _networkId;
        private readonly Action              _onRemove;

        // Working copy of waypoints.
        private readonly List<RouteWaypoint> _waypoints;
        private readonly bool                _isLoop;

        // Selected / dragging vertex.
        private int          _activeVertex = -1;
        private RouteWaypoint _savedWaypoint;

        private bool _active = true;

        // ---- IRouteWaypointEditorState ----------------------------------------
        public int SelectedVertexIndex => _activeVertex;

        public ref RouteWaypoint GetSelectedWaypointRef()
        {
            if (_activeVertex < 0 || _activeVertex >= _waypoints.Count)
                throw new InvalidOperationException("No vertex selected.");
            return ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_waypoints)[_activeVertex];
        }

        // ---- IEntityStatefulGizmo --------------------------------------------
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public RouteWaypointGizmo(ISimulationView view, Entity entity, long networkId, Action onRemove)
        {
            _repo      = view as EntityRepository
                ?? throw new ArgumentException("RouteWaypointGizmo requires EntityRepository access.", nameof(view));
            _entity    = entity;
            _networkId = networkId;
            _onRemove  = onRemove ?? throw new ArgumentNullException(nameof(onRemove));

            // Load current waypoints.
            _waypoints = new List<RouteWaypoint>();
            _isLoop    = false;
            if (_repo.HasManagedComponent<RoutePlan>(_entity))
            {
                var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity);
                _isLoop = plan.IsLoop;
                if (plan.Waypoints != null)
                    _waypoints.AddRange(plan.Waypoints);
            }

            Current = this;
        }

        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active || _waypoints.Count == 0) return;

            // ContextMenuBinding so right-clicking a handle shows the waypoint menu.
            draw.DrawContextMenuBinding(_networkId, MenuJson);

            // Route line segments.
            int segCount = _isLoop ? _waypoints.Count : _waypoints.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a = _waypoints[i];
                var b = _waypoints[(i + 1) % _waypoints.Count];
                draw.DrawLine(
                    new Vector3(a.Position.X, a.Position.Z, 0f),
                    new Vector3(b.Position.X, b.Position.Z, 0f),
                    new Rgba32(0x44, 0x88, 0xFF, 0xFF), 1.5f, SizeMode.ScreenPixels);
            }

            // Box2D handle per waypoint.
            for (int i = 0; i < _waypoints.Count; i++)
            {
                bool isActive = (i == _activeVertex);
                var pos = _waypoints[i].Position;
                var prim = default(DebugPrimitive);
                prim.Shape            = DebugPrimitiveShape.Box2D;
                prim.Space            = CoordinateSpace.World;
                prim.TargetView       = PipelineTarget.Map2D;
                prim.BoxCenterX       = pos.X;
                prim.BoxCenterY       = pos.Z;     // Z=North maps to canvas Y
                prim.BoxExtentX       = 8f;
                prim.BoxExtentY       = 8f;
                prim.Color            = isActive ? ActiveColor : IdleColor;
                prim.SubElementId     = (ushort)(i + 1);
                prim.AnchorIndex      = _entity.Index;
                prim.AnchorGeneration = (ushort)_entity.Generation;
                prim.InspNetworkId    = _networkId;
                draw.EmitRaw(in prim);
            }
        }

        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
        {
            int idx = (int)token.SubElementId - 1;
            if (idx < 0 || idx >= _waypoints.Count) return;
            _activeVertex  = idx;
            _savedWaypoint = _waypoints[idx];
        }

        public void OnDragUpdate(Vector3 worldPos)
        {
            if (_activeVertex < 0 || _activeVertex >= _waypoints.Count) return;
            var wp = _waypoints[_activeVertex];
            // worldPos.Y on 2D map = North = Position.Z
            wp.Position = new Vector3(worldPos.X, wp.Position.Y, worldPos.Y);
            _waypoints[_activeVertex] = wp;
        }

        public void OnCommit(Vector3 worldPos)
        {
            // Finalize drag. Marker stays so more waypoints can be edited.
            WriteBackAndPublish();
            _activeVertex = -1;
        }

        public void OnCancel()
        {
            if (_activeVertex >= 0 && _activeVertex < _waypoints.Count)
                _waypoints[_activeVertex] = _savedWaypoint;
            _activeVertex = -1;
        }

        public void OnMenuAction(int actionId)
        {
            if (_activeVertex < 0 || _activeVertex >= _waypoints.Count) return;

            if (actionId == 1) // Insert after
            {
                int next = (_waypoints.Count == 1) ? 0 : (_activeVertex + 1) % _waypoints.Count;
                var midPos = (_waypoints[_activeVertex].Position + _waypoints[next].Position) * 0.5f;
                var newWp  = new RouteWaypoint
                {
                    Position    = midPos,
                    TargetSpeed = _waypoints[_activeVertex].TargetSpeed,
                };
                _waypoints.Insert(_activeVertex + 1, newWp);
                WriteBackAndPublish();
            }
            else if (actionId == 2) // Delete
            {
                _waypoints.RemoveAt(_activeVertex);
                _activeVertex = -1;
                WriteBackAndPublish();
            }
        }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }

        public void Dispose()
        {
            if (Current == this)
                Current = null;
        }

        // ---- private helpers ---------------------------------------------------

        private void WriteBackAndPublish()
        {
            if (!_repo.IsAlive(_entity)) return;
            if (!_repo.HasManagedComponent<RoutePlan>(_entity)) return;

            var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity);
            plan.Mutate(wps =>
            {
                wps.Clear();
                wps.AddRange(_waypoints);
            });

            _repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId          = _networkId,
                ComponentsToUpdate = new List<object> { plan },
                RequestId          = Guid.NewGuid(),
            });
        }
    }
}
```

---

## Task 7: Create RouteWaypointGizmoDefinition

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteWaypointGizmoDefinition.cs`

```csharp
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // IGizmoDefinition for interactive RoutePlan waypoint editing.
    // Activated by DataDrivenGizmoSystem when SimTransform + ActiveRouteEditRequest
    // are both present on an entity.
    // Only instantiates a gizmo if the entity also has a RoutePlan managed component.
    public sealed class RouteWaypointGizmoDefinition : IGizmoDefinition
    {
        public Type[] RequiredComponents { get; } =
        {
            typeof(SimTransform),
            typeof(ActiveRouteEditRequest),
        };

        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository
                ?? throw new ArgumentException(
                    $"{nameof(RouteWaypointGizmoDefinition)}.CreateInstance requires " +
                    $"direct EntityRepository access, not {view.GetType().Name}.");

            if (!repo.HasManagedComponent<RoutePlan>(entity))
                return new NullGizmo();

            long networkId = 0;
            if (repo.HasComponent<NetworkIdentity>(entity))
                networkId = repo.GetComponentRO<NetworkIdentity>(entity).Value;

            return new RouteWaypointGizmo(
                view,
                entity,
                networkId,
                onRemove: () =>
                {
                    if (repo.IsAlive(entity) && repo.HasComponent<ActiveRouteEditRequest>(entity))
                        repo.RemoveComponent<ActiveRouteEditRequest>(entity);
                });
        }

        // No-op gizmo returned when the entity lacks RoutePlan (safety guard).
        private sealed class NullGizmo : IEntityStatefulGizmo
        {
            public bool RequiresExclusiveFocus => false;
            public bool IsFocused { get; private set; }
            public void SetFocus(bool f) => IsFocused = f;
            public void UpdateAndDraw(float dt, IDebugDrawBuilder draw) { }
            public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
            public void OnDragUpdate(Vector3 worldPos) { }
            public void OnCommit(Vector3 worldPos) { }
            public void OnCancel() { }
            public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
            public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
            public void OnMenuAction(int actionId) { }
            public void Dispose() { }
        }
    }
}
```

---

## Task 8: Update HrotComponentIds (already done in Task 1)

Covered by Task 1. No separate action needed.

---

## Task 9: Update EditorSubsystem

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

### 9a. Register new marker components

Locate the component registration block in `Initialize()` — just after the existing
`_world.RegisterComponent<TracerTarget>();` line. Add:

```csharp
// Vertex and route edit gizmo activation markers (Phase 2 geometry editing gizmos).
_world.RegisterComponent<Hrot.ScenarioEditor.Gizmos.ActiveVertexEditRequest>();
_world.RegisterComponent<Hrot.ScenarioEditor.Gizmos.ActiveRouteEditRequest>();
```

### 9b. Register new gizmo definitions

Locate the line:
```csharp
editorGizmoRegistry.Register(new Hrot.SimHost.Gizmos.EntityRotatorGizmoDefinition());
```
Add immediately after it:
```csharp
editorGizmoRegistry.Register(new Hrot.ScenarioEditor.Gizmos.VertexEditGizmoDefinition());
editorGizmoRegistry.Register(new Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmoDefinition());
```

### 9c. Replace the Edit case

Replace the current `case Hrot.Editor.EditorTool.Edit:` block (which calls
`_canvas!.PushTool(new Hrot.ScenarioEditor.Tools.EditTool(...))`) with:

```csharp
case Hrot.Editor.EditorTool.Edit:
{
    // Add ActiveVertexEditRequest marker; DataDrivenGizmoSystem creates
    // VertexEditGizmo. No bridge needed (RequiresExclusiveFocus = false).
    var entity = _selectionState.PrimarySelected;
    if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.IG.Components.EditablePolyline>(e))
    {
        if (!_world!.HasComponent<Hrot.ScenarioEditor.Gizmos.ActiveVertexEditRequest>(e))
            _world!.AddComponent<Hrot.ScenarioEditor.Gizmos.ActiveVertexEditRequest>(e, default);
        _world!.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent { Entity = e });
    }
    break;
}
```

### 9d. Replace the Route case

Replace the current `case Hrot.Editor.EditorTool.Route:` block (which calls
`_canvas!.PushTool(new Hrot.ScenarioEditor.Tools.RouteEditTool(...))`) with:

```csharp
case Hrot.Editor.EditorTool.Route:
{
    // Add ActiveRouteEditRequest marker; DataDrivenGizmoSystem creates
    // RouteWaypointGizmo. No bridge needed (RequiresExclusiveFocus = false).
    var entity = _selectionState.PrimarySelected;
    if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(e))
    {
        if (!_world!.HasComponent<Hrot.ScenarioEditor.Gizmos.ActiveRouteEditRequest>(e))
            _world!.AddComponent<Hrot.ScenarioEditor.Gizmos.ActiveRouteEditRequest>(e, default);
        _world!.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent { Entity = e });
    }
    break;
}
```

---

## Task 10: Update IgApplication

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

### 10a. Add using directive

Add at the top of the file (alongside the other `using` statements):
```csharp
using Hrot.ScenarioEditor.Gizmos;
```

### 10b. Register new components in InitializeEcs()

In `InitializeEcs()`, after the last `_world.RegisterComponent<MapDisplayComponent>();` line
(or after `_world.RegisterComponent<EntityInfo>();` — find the end of the individual
registrations block), add:

```csharp
// Gizmo activation event and marker components for local vertex/route editing (Phase 2).
_world.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent>();
_world.RegisterComponent<ActiveVertexEditRequest>();
_world.RegisterComponent<ActiveRouteEditRequest>();
```

### 10c. Register gizmo definitions in the GizmoRegistrar block

After the line:
```csharp
GizmoRegistrar.Register(_gizmoRegistry, _statelessGizmoRegistry, _gizmoSettingsRegistry);
```
Add:
```csharp
_gizmoRegistry!.Register(new VertexEditGizmoDefinition());
_gizmoRegistry!.Register(new RouteWaypointGizmoDefinition());
```

### 10d. Add DataDrivenGizmoSystem for IG

Locate this comment block (around line 1205):
```csharp
// DataDrivenGizmoSystem is NOT registered in IG. IG is a dumb terminal.
// Primitives arrive via DebugPrimitivesIngressTranslator (see _ingressTranslator).
// GZ038: removed DataDrivenGizmoSystem registration.
```

Replace the entire comment block with:
```csharp
// GZ038 reversed: DataDrivenGizmoSystem is registered for local vertex/route editing.
// isSelectedPredicate: null because IG is dumb terminal -- draw all active gizmos.
var igDataDrivenGizmoSystem = new DataDrivenGizmoSystem(
    _gizmoRegistry!,
    _gizmoBuffer!,
    isSelectedPredicate: null);
_kernel.RegisterGlobalSystem(igDataDrivenGizmoSystem);
```

### 10e. Change WaypointEditorPanel constructor

Locate:
```csharp
_waypointEditorPanel = new WaypointEditorPanel(_canvas);
```
Replace with:
```csharp
_waypointEditorPanel = new WaypointEditorPanel(() => RouteWaypointGizmo.Current);
```

### 10f. Rewrite ActivateAreaEditingTool

Replace the entire `private void ActivateAreaEditingTool(long networkEntityId)` method body
with the new implementation below. Keep the XML summary comment that precedes the method
(update it to reflect the new behavior).

```csharp
private void ActivateAreaEditingTool(long networkEntityId)
{
    if (!_entityMap.TryGetEntity(networkEntityId, out var entity))
    {
        FdpLog<IgApplication>.Warn(
            "[Node-{0}] ActivateAreaEditingTool: entity not found for NetID {1}.", _effectiveInstanceId, networkEntityId);
        return;
    }

    // ── Route entity path — use RouteWaypointGizmo via ActiveRouteEditRequest marker ──
    if (World.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(entity))
    {
        // Toggle: if the gizmo is already active, remove the marker to close it.
        if (World.HasComponent<ActiveRouteEditRequest>(entity))
        {
            World.RemoveComponent<ActiveRouteEditRequest>(entity);
            FdpLog<IgApplication>.Info(
                "[Node-{0}] Route editing deactivated for NetID {1}.", _effectiveInstanceId, networkEntityId);
        }
        else
        {
            if (!World.HasComponent<SimTransform>(entity))
            {
                FdpLog<IgApplication>.Warn(
                    "[Node-{0}] ActivateAreaEditingTool: entity {1} has no SimTransform yet.", _effectiveInstanceId, networkEntityId);
                return;
            }
            World.AddComponent<ActiveRouteEditRequest>(entity, default);
            _world.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent { Entity = entity });
            FdpLog<IgApplication>.Info(
                "[Node-{0}] Route editing activated for NetID {1}.", _effectiveInstanceId, networkEntityId);
        }
        return;
    }

    // ── Area overlay path — use VertexEditGizmo via ActiveVertexEditRequest marker ──
    if (!World.HasManagedComponent<EditablePolyline>(entity))
    {
        FdpLog<IgApplication>.Warn(
            "[Node-{0}] ActivateAreaEditingTool: entity {1} has no EditablePolyline.", _effectiveInstanceId, networkEntityId);
        return;
    }

    // Toggle: if the gizmo is already active, remove the marker to close it.
    if (World.HasComponent<ActiveVertexEditRequest>(entity))
    {
        World.RemoveComponent<ActiveVertexEditRequest>(entity);
        FdpLog<IgApplication>.Info(
            "[Node-{0}] Area editing deactivated for NetID {1}.", _effectiveInstanceId, networkEntityId);
    }
    else
    {
        if (!World.HasComponent<SimTransform>(entity))
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ActivateAreaEditingTool: entity {1} has no SimTransform yet.", _effectiveInstanceId, networkEntityId);
            return;
        }
        World.AddComponent<ActiveVertexEditRequest>(entity, default);
        _world.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent { Entity = entity });
        FdpLog<IgApplication>.Info(
            "[Node-{0}] Area editing activated for NetID {1}.", _effectiveInstanceId, networkEntityId);
    }
}
```

### 10g. Remove stale vertex context menu blocks

In `DrawUI()` (or the method that calls `Draw()`), locate and DELETE these two blocks
entirely (they reference `RouteEditTool` and `EditTool` which are being deleted):

```csharp
// ── Vertex context menu for RouteEditTool ─────────────────────────────────────────────
if (_canvas.ActiveTool is RouteEditTool routeTool && routeTool.PendingVertexContextMenu)
{
    ImGui.OpenPopup("##routeVtxCtx");
}
if (ImGui.BeginPopup("##routeVtxCtx"))
{
    if (ImGui.MenuItem("Insert point after"))
        (_canvas.ActiveTool as RouteEditTool)?.InsertWaypointAfterSelected();
    if (ImGui.MenuItem("Delete point"))
        (_canvas.ActiveTool as RouteEditTool)?.DeleteSelectedWaypoint();
    ImGui.Separator();
    if (ImGui.MenuItem("Cancel"))
        (_canvas.ActiveTool as RouteEditTool)?.CloseVertexContextMenu();
    ImGui.EndPopup();
}

// ── Vertex context menu for EditTool (overlay shapes) ─────────────────
if (_canvas.ActiveTool is EditTool editTool && editTool.PendingVertexContextMenu)
{
    ImGui.OpenPopup("##overlayVtxCtx");
}
if (ImGui.BeginPopup("##overlayVtxCtx"))
{
    if (ImGui.MenuItem("Insert point after"))
        (_canvas.ActiveTool as EditTool)?.InsertPointAfterSelected();
    if (ImGui.MenuItem("Delete point"))
        (_canvas.ActiveTool as EditTool)?.DeleteSelectedPoint();
    ImGui.Separator();
    if (ImGui.MenuItem("Cancel"))
        (_canvas.ActiveTool as EditTool)?.CloseVertexContextMenu();
    ImGui.EndPopup();
}
```

Also remove the `using Hrot.ScenarioEditor.Tools;` at the top of the file IF (and only if)
nothing else in `IgApplication.cs` still references that namespace after these changes.
Check carefully — `StandardInteractionTool` is aliased to `Hrot.ScenarioEditor.Tools.StandardInteractionTool`
in the using directives, so that using alias must stay.

---

## Task 11: Update WaypointEditorPanel

**File:** `Hrot/Subsystems/Hrot.IG/UI/WaypointEditorPanel.cs`

Replace the entire file content. Key changes:
1. Remove `using Hrot.ScenarioEditor.Tools;`
2. Add `using Hrot.ScenarioEditor.Gizmos;`
3. Change field `_canvas` → `_getActiveState` (`Func<IRouteWaypointEditorState?>`)
4. Change constructor parameter
5. Update `UpdatePanelState` signature from `RouteEditTool?` to `IRouteWaypointEditorState?`
6. Update `DrawContent()` to call `_getActiveState()`

New file:

```csharp
using System;
using Hrot.ScenarioEditor.Gizmos;
using ImGuiNET;

namespace Hrot.IG.UI;

/// <summary>
/// ImGui panel that exposes per-waypoint editing controls when a
/// <see cref="RouteWaypointGizmo"/> is active and a vertex is selected (ROUTES1-T013).
///
/// <para>
/// The panel reads <see cref="IRouteWaypointEditorState.SelectedVertexIndex"/> from the
/// active gizmo state each frame. When a vertex is selected, it renders:
/// <list type="bullet">
///   <item>Read-only position label.</item>
///   <item><c>Target Speed (m/s)</c> float input — updates
///         <see cref="Hrot.Map.Common.Components.RouteWaypoint.TargetSpeed"/> in-place.</item>
///   <item><c>AI Advice (JSON)</c> multiline text input — updates
///         <see cref="Hrot.Map.Common.Components.RouteWaypoint.ExtensionJson"/> in-place.</item>
/// </list>
/// </para>
///
/// <para>
/// The panel does NOT commit changes — <see cref="RouteWaypointGizmo"/> owns the working
/// state and writes back on each <c>OnCommit</c>.
/// </para>
/// </summary>
public class WaypointEditorPanel
{
    private readonly Func<IRouteWaypointEditorState?> _getActiveState;

    // Working buffer for the multiline JSON input (avoids per-frame allocation).
    private string _jsonBuffer = string.Empty;

    // CT-2: cache the last rendered waypoint index so we only copy ExtensionJson
    // into _jsonBuffer when the selection actually changes, avoiding per-frame
    // string allocation.
    private int _lastWpIndex = -1;

    // CT-2: tracks whether the gizmo was active in the previous draw call so that
    // a deactivation can be detected and keyboard focus cleared.
    private bool _wasRouteToolActive;

    // ── Test hooks ────────────────────────────────────────────────────────────

    /// <summary>Exposes the cached selection index for headless tests (CT-2).</summary>
    internal int    TestHook_LastWpIndex        => _lastWpIndex;

    /// <summary>Exposes the current JSON buffer contents for headless tests (CT-2).</summary>
    internal string TestHook_JsonBuffer         => _jsonBuffer;

    /// <summary>Exposes the focus-tracking state for headless tests (CT-2).</summary>
    internal bool   TestHook_WasRouteToolActive => _wasRouteToolActive;

    /// <param name="getActiveState">
    /// Factory that returns the currently active <see cref="IRouteWaypointEditorState"/>,
    /// or <see langword="null"/> when no route editing gizmo is active.
    /// </param>
    public WaypointEditorPanel(Func<IRouteWaypointEditorState?> getActiveState)
        => _getActiveState = getActiveState ?? throw new ArgumentNullException(nameof(getActiveState));

    /// <summary>
    /// Core panel state update: refreshes <see cref="_lastWpIndex"/>,
    /// <see cref="_jsonBuffer"/>, and <see cref="_wasRouteToolActive"/> based on the
    /// given gizmo state. Separated from <see cref="Draw"/> so headless unit tests
    /// can exercise the caching logic without an active ImGui context (CT-2).
    /// </summary>
    /// <param name="activeState">
    /// The active <see cref="IRouteWaypointEditorState"/> with a valid selection, or
    /// <see langword="null"/> when no vertex is selected.
    /// </param>
    internal void UpdatePanelState(IRouteWaypointEditorState? activeState)
    {
        if (activeState == null)
        {
            _wasRouteToolActive = false;
            _lastWpIndex = -1;
            return;
        }

        _wasRouteToolActive = true;

        // Only refresh the JSON buffer when the selection index changes; avoids
        // per-frame string allocation for unchanged waypoints (CT-2).
        if (activeState.SelectedVertexIndex != _lastWpIndex)
        {
            _lastWpIndex = activeState.SelectedVertexIndex;
            ref var wp   = ref activeState.GetSelectedWaypointRef();
            _jsonBuffer  = wp.ExtensionJson ?? string.Empty;
        }
    }

    /// <summary>
    /// Renders the waypoint editor ImGui window.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        IgPanelColors.Push();
        bool visible = ImGui.Begin("Waypoint Editor");
        IgPanelColors.Pop();
        if (!visible) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        var activeState = _getActiveState();
        bool hasSelection = activeState?.SelectedVertexIndex >= 0;

        // CT-2: when the gizmo deactivates, strip keyboard focus from any
        // still-active ImGui input widget to prevent stale values from leaking.
        if (_wasRouteToolActive && !hasSelection)
            ImGui.SetKeyboardFocusHere(-1);

        UpdatePanelState(hasSelection ? activeState : null);

        if (!hasSelection)
        {
            ImGui.TextDisabled("Select a waypoint to edit its properties.");
            return;
        }

        ref var wp = ref activeState!.GetSelectedWaypointRef();

        // ── Position (read-only) ──────────────────────────────────────────────
        ImGui.LabelText("Position", $"({wp.Position.X:F1}, {wp.Position.Y:F1}, {wp.Position.Z:F1})");

        ImGui.Separator();

        // ── Target Speed ──────────────────────────────────────────────────────
        float speed = wp.TargetSpeed;
        if (ImGui.InputFloat("Target Speed (m/s)", ref speed))
            wp.TargetSpeed = System.Math.Max(0f, speed);

        // ── AI Advice JSON ────────────────────────────────────────────────────
        if (ImGui.InputTextMultiline(
                "AI Advice (JSON)",
                ref _jsonBuffer,
                maxLength: 2048,
                size: new System.Numerics.Vector2(0f, 80f)))
        {
            wp.ExtensionJson = string.IsNullOrWhiteSpace(_jsonBuffer) ? null : _jsonBuffer;
        }
    }
}
```

---

## Task 12: Update ToolPresenceTests

**File:** `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs`

In `ScenarioEditor_Assembly_ContainsAllToolTypes()`, **remove** the four assertions for
deleted types:
```csharp
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.EditTool"));
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditTool"));
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.EditToolConstants"));
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditToolConstants"));
```

**Add** inverse assertions in the `IG_Assembly_DoesNotContainToolTypes()` test
(or in the same test) confirming the types are gone:
```csharp
// Deleted in Phase 2 — must no longer exist in ScenarioEditor assembly.
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.EditTool"));
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditTool"));
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.EditToolConstants"));
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditToolConstants"));
```

---

## Task 13: Update WaypointEditorPanelTests

**File:** `Hrot/Subsystems/Hrot.IG.Tests/WaypointEditorPanelTests.cs`

Replace the entire file. The key changes:

1. Remove `using Hrot.ScenarioEditor.Tools;`, `using Fdp.Toolkit.Vis2D;`,
   `using Fdp.Toolkit.Vis2D.Abstractions;`
2. Add `using Hrot.ScenarioEditor.Gizmos;`
3. Replace `CreatePanel()` factory to use the new constructor
4. Replace `CreateAndEnterTool()` helper with `StubRouteState` (a private inner class that
   implements `IRouteWaypointEditorState` for testing)
5. Update all `panel.UpdatePanelState(tool)` calls to `panel.UpdatePanelState(state)`

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.IG.UI;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="WaypointEditorPanel"/> -- state-management logic (CT-2,
/// ROUTES1-BATCH-04).
///
/// <para>
/// Tests exercise <see cref="WaypointEditorPanel.UpdatePanelState"/> directly, which
/// contains the caching logic separated from the ImGui rendering calls. This allows
/// headless execution without an active ImGui/Raylib context.
/// </para>
///
/// <para>
/// Assertions target the observable test-hook properties
/// (<c>TestHook_LastWpIndex</c>, <c>TestHook_JsonBuffer</c>,
/// <c>TestHook_WasRouteToolActive</c>) rather than ImGui widget state.
/// </para>
/// </summary>
public class WaypointEditorPanelTests
{
    // ── Test stub implementing IRouteWaypointEditorState ─────────────────────

    private sealed class StubRouteState : IRouteWaypointEditorState
    {
        private readonly RouteWaypoint[] _waypoints;

        public int SelectedVertexIndex { get; }

        public StubRouteState(RoutePlan plan, int selectedIndex)
        {
            _waypoints = plan.Waypoints?.ToArray() ?? Array.Empty<RouteWaypoint>();
            SelectedVertexIndex = selectedIndex;
        }

        public ref RouteWaypoint GetSelectedWaypointRef()
            => ref MemoryMarshal.GetArrayDataReference(_waypoints)[SelectedVertexIndex];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WaypointEditorPanel CreatePanel()
        => new WaypointEditorPanel(() => null);

    private static RoutePlan MakePlan(params string?[] jsonValues)
    {
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            for (int i = 0; i < jsonValues.Length; i++)
                wps.Add(new RouteWaypoint
                {
                    Position      = new Vector3(i * 10f, 0f, i * 10f),
                    TargetSpeed   = 5f,
                    ExtensionJson = jsonValues[i],
                });
        });
        return plan;
    }

    private static StubRouteState CreateStubState(RoutePlan plan, int selectIndex = 0)
        => new StubRouteState(plan, selectIndex);

    // ══════════════════════════════════════════════════════════════════════════
    // Initial state
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Directly after construction both <see cref="WaypointEditorPanel.TestHook_LastWpIndex"/>
    /// and <see cref="WaypointEditorPanel.TestHook_WasRouteToolActive"/> must be at their
    /// sentinel defaults (-1 and false respectively) before any <c>UpdatePanelState</c>
    /// call.
    /// </summary>
    [Fact]
    public void InitialState_LastWpIndexMinusOne_WasRouteToolActiveFalse()
    {
        var panel = CreatePanel();

        Assert.Equal(-1, panel.TestHook_LastWpIndex);
        Assert.False(panel.TestHook_WasRouteToolActive);
        Assert.Equal(string.Empty, panel.TestHook_JsonBuffer);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // _lastWpIndex caching -- buffer allocation behaviour
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When <c>UpdatePanelState</c> is called twice for the same selection, the
    /// <c>_jsonBuffer</c> string reference must remain identical (no new string
    /// created), validating structural continuity across unaffected layout draws
    /// (CT-2 memory layout check).
    /// </summary>
    [Fact]
    public void JsonBuffer_NotUpdatedWhenWaypointIndexUnchanged_SameReference()
    {
        var panel = CreatePanel();
        var state = CreateStubState(MakePlan(@"{""dangerLevel"":1}", null));

        // First draw: index changes from -1 -> 0, buffer is populated.
        panel.UpdatePanelState(state);
        string firstRef = panel.TestHook_JsonBuffer;

        // Second draw: same index -- buffer must NOT be re-assigned.
        panel.UpdatePanelState(state);
        string secondRef = panel.TestHook_JsonBuffer;

        Assert.Equal(0, panel.TestHook_LastWpIndex);
        Assert.True(ReferenceEquals(firstRef, secondRef),
            "JsonBuffer must not be re-assigned when the selected waypoint index is unchanged.");
    }

    /// <summary>
    /// When the selection moves to a different waypoint, <c>_jsonBuffer</c> must be
    /// refreshed with the new waypoint's <see cref="RouteWaypoint.ExtensionJson"/>
    /// and <c>_lastWpIndex</c> must reflect the new index.
    /// </summary>
    [Fact]
    public void JsonBuffer_UpdatedWhenWaypointIndexChanges_ReflectsNewJson()
    {
        var panel = CreatePanel();
        var plan  = MakePlan(@"{""dangerLevel"":1}", @"{""dangerLevel"":99}");

        var stateAtWp0 = CreateStubState(plan, selectIndex: 0);
        panel.UpdatePanelState(stateAtWp0);

        Assert.Equal(0, panel.TestHook_LastWpIndex);
        Assert.Equal(@"{""dangerLevel"":1}", panel.TestHook_JsonBuffer);

        // Select wp1 (different stub state simulating a re-select).
        var stateAtWp1 = CreateStubState(plan, selectIndex: 1);
        panel.UpdatePanelState(stateAtWp1);

        Assert.Equal(1, panel.TestHook_LastWpIndex);
        Assert.Equal(@"{""dangerLevel"":99}", panel.TestHook_JsonBuffer);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // _wasRouteToolActive transitions (CT-2 focus guard)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When <c>UpdatePanelState(null)</c> is called (gizmo deactivated / no selection),
    /// <see cref="WaypointEditorPanel.TestHook_WasRouteToolActive"/> must be
    /// <c>false</c> and <c>_lastWpIndex</c> must reset to -1.
    /// </summary>
    [Fact]
    public void WasRouteToolActive_TransitionsToFalse_WhenToolDeactivated()
    {
        var panel = CreatePanel();
        var state = CreateStubState(MakePlan("{}"));

        // Activate.
        panel.UpdatePanelState(state);
        Assert.True(panel.TestHook_WasRouteToolActive);
        Assert.Equal(0, panel.TestHook_LastWpIndex);

        // Deactivate (simulates gizmo disposal / marker removal).
        panel.UpdatePanelState(null);

        Assert.False(panel.TestHook_WasRouteToolActive);
        Assert.Equal(-1, panel.TestHook_LastWpIndex);
    }
}
```

> **Note on `GetArrayDataReference`:** `MemoryMarshal.GetArrayDataReference` returns a
> `ref` to the first element of the array. Indexing is done via `ref Unsafe.Add(...)`.
> A simpler alternative that avoids unsafe helpers:
> ```csharp
> public ref RouteWaypoint GetSelectedWaypointRef()
> {
>     var span = _waypoints.AsSpan();
>     return ref span[SelectedVertexIndex];
> }
> ```
> where `_waypoints` is `RouteWaypoint[]` and `AsSpan()` comes from `MemoryExtensions`.
> **Use whichever compiles cleanly.** The test only reads `ExtensionJson`, it does not mutate.

---

## Task 14: Delete Old Tool Files

Delete the following files completely (do not leave empty stubs):

1. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EditTool.cs`
2. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/EditToolConstants.cs`
3. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RouteEditTool.cs`
4. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/RouteEditToolConstants.cs`
5. `Hrot/Subsystems/Hrot.IG.Tests/EditToolTests.cs`
6. `Hrot/Subsystems/Hrot.IG.Tests/RouteEditToolTests.cs`

---

## Task 15: Write Gizmo Unit Tests

Write tests in `Hrot/Engine/Hrot.Presentation.Tests/`.

### 15a. VertexEditGizmoTests.cs

Create `Hrot/Engine/Hrot.Presentation.Tests/VertexEditGizmoTests.cs`. The test setup
requires an `EntityRepository` with `SimTransform`, `EditablePolyline`, and
`NetworkIdentity` registered. Use a no-op `IDebugDrawBuilder` stub.

**Required test cases:**

| ID | Name | What to assert |
|----|------|----------------|
| VEG-001 | `OnInteractionStarted_SetsActiveVertex` | After `OnInteractionStarted(token with SubElementId=2, ...)`, `OnDragUpdate` moves point at index 1 |
| VEG-002 | `OnCommit_WritesBackToEcs` | After drag + `OnCommit`, `repo.GetManagedComponentRO<EditablePolyline>(entity).Points` reflects the moved vertex |
| VEG-003 | `OnCancel_RevertsVertex` | After drag + `OnCancel`, `Points` is unchanged from initial |
| VEG-004 | `OnMenuAction_InsertAfter_AddsVertex` | `OnMenuAction(1)` after selecting vertex 0 inserts a midpoint; `Points.Count` increases by 1 |
| VEG-005 | `OnMenuAction_Delete_RemovesVertex` | `OnMenuAction(2)` after selecting vertex 0 removes that vertex; `Points.Count` decreases by 1 |

Use `Hrot.Core` `EntityRepository` and `SimHostComponentRegistry`-style registration
(you can call `HrotSharedComponentRegistry.RegisterAll(repo)` then register
`EditablePolyline`, `ActiveVertexEditRequest`, `NetworkIdentity` manually).

### 15b. RouteWaypointGizmoTests.cs

Create `Hrot/Engine/Hrot.Presentation.Tests/RouteWaypointGizmoTests.cs`.

**Required test cases:**

| ID | Name | What to assert |
|----|------|----------------|
| RWG-001 | `OnInteractionStarted_SetsActiveVertex` | After `OnInteractionStarted(SubElementId=1)`, `SelectedVertexIndex == 0` |
| RWG-002 | `OnCommit_WritesBackToEcs` | After drag + `OnCommit`, the `RoutePlan.Waypoints` in the repo reflects the moved waypoint |
| RWG-003 | `OnCancel_RevertsWaypoint` | After drag + `OnCancel`, waypoint position is unchanged |
| RWG-004 | `Current_SetOnConstruction_ClearedOnDispose` | `RouteWaypointGizmo.Current` is non-null after construction, null after `Dispose()` |

---

## Build Verification

After completing all tasks:

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln
dotnet test IOS-IG-SimHost.sln --no-build
```

Expected outcome:
- Zero build errors
- All tests pass
- `EditTool`, `RouteEditTool`, `EditToolConstants`, `RouteEditToolConstants` classes
  no longer exist in the solution

---

## Common Pitfalls

1. **`prim.BoxAnchorId` vs `prim.AnchorIndex/AnchorGeneration`:** For the ECS routing path
   (`DataDrivenGizmoSystem`), use `AnchorIndex`/`AnchorGeneration`. `BoxAnchorId` is used
   by the GizmoMap.Contracts path only.

2. **`AnchorGeneration` must be non-zero** for `PickToken.IsValid` to return `true`.
   `entity.Generation` is always > 0 for live entities (generation 0 means "never existed").

3. **`EditablePolyline.Points` are RELATIVE** to `SimTransform.Position` (XY plane).
   The gizmo loads them as absolute by adding `_originOffset`, and writes them back as
   relative by subtracting `_originOffset`.

4. **`RouteWaypoint.Position.Z` = North** (map Y). When reading drag worldPos:
   `waypoint.Position.X = worldPos.X` (East), `waypoint.Position.Z = worldPos.Y` (North/canvas-Y).

5. **`RoutePlan.Mutate()`** must be called to update waypoints — do not assign a new
   `RoutePlan` object, as `Mutate()` increments the version stamp for downstream replication.

6. **`IgApplication.cs` using statement:** The line
   `using StandardInteractionTool = Hrot.ScenarioEditor.Tools.StandardInteractionTool;`
   at the top of `IgApplication.cs` must NOT be removed even after removing
   `using Hrot.ScenarioEditor.Tools;`. Check whether removing the global using breaks
   other references in the file.

7. **`WaypointEditorPanel` test `GetSelectedWaypointRef()`:** The stub returns a `ref` to
   array element — ensure `_waypoints` is an array (`RouteWaypoint[]`), not a `List<T>`
   (lists don't support `ref` indexing directly without `CollectionsMarshal`).
