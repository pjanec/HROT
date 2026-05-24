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
    // Exclusive-focus gizmo that lets the operator drag individual waypoints of
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
        private int           _activeVertex = -1;
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
        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
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

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
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
                prim.BoxAnchorId      = _networkId;
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

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Right && !isPressed)
            {
                WriteBackAndPublish();
                _onRemove();
            }
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed)
            {
                OnCancel();
                _onRemove();
            }
        }

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
