using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Hrot.Common.Actions;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Exclusive-focus gizmo that lets the operator drag individual waypoints of
    // a RoutePlan entity. Implements IRouteWaypointEditorState so WaypointEditorPanel
    // can read per-waypoint TargetSpeed and ExtensionJson without depending on this class.
    //
    // Coordinate convention (⭐ CE-2172, R-182 — was "Z = North", which put a drag's north into the altitude):
    //   RouteWaypoint.Position = (X=East, Y=North, Z=Up) -- same as SimTransform and the draw tool.
    //   2D map canvas = (X=East, Y=North). So a drag moves Position.X / Position.Y and keeps Position.Z.
    //
    // Static Current: exposes the active gizmo instance so WaypointEditorPanel can
    // bind to it without requiring a DI lookup. Set on construction, cleared on Dispose.
    public sealed class RouteWaypointGizmo : IEntityStatefulGizmo, IRouteWaypointEditorState
    {
        // Context menu JSON: insert / delete waypoint.
        // ⭐ B-17 P2 — the editor's own two rows, then the catalogue's Finish · Cancel (right-click is the MENU; it no longer ends the edit).
        private static readonly string MenuJson = DrawingActions.JsonWith(
            new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 1, Label = "Insert waypoint after" },
            new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 2, Label = "Delete waypoint" });

        private static readonly Rgba32 IdleColor   = new Rgba32(0, 160, 255, 220);
        private static readonly Rgba32 ActiveColor = Rgba32.Red;

        // Tracks the single active instance so WaypointEditorPanel can bind.
        public static RouteWaypointGizmo? Current { get; private set; }

        private readonly EntityRepository    _repo;
        private readonly Entity              _entity;
        private readonly int                _networkId;
        private readonly Action              _onRemove;

        // Working copy of waypoints.
        private readonly List<RouteWaypoint> _waypoints;
        private readonly bool                _isLoop;

        // ⭐ B-17 P2 — the ONE drawing core: the waypoints' map positions (X, Y), the selected waypoint, insert / delete, and the finish / cancel
        //   convention. ⚠ The waypoint list above keeps what the core cannot (speed, extension) and is kept in step by index.
        private readonly ShapeEditSession _session;

        /// <summary>⭐ B-17 P2 — the drawing core this editor is a view of.</summary>
        public ShapeEditSession Session => _session;

        // Selected / dragging vertex — the session's selected point.
        private int _activeVertex { get => _session.Selected; set => _session.Selected = value; }
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

        public RouteWaypointGizmo(ISimulationView view, Entity entity, int networkId, Action onRemove)
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

            _session = new ShapeEditSession(minPoints: 2, closed: _isLoop, noun: "a route");
            var xy = new List<Vector2>(_waypoints.Count);
            foreach (var wp in _waypoints) xy.Add(new Vector2(wp.Position.X, wp.Position.Y));
            _session.Load(xy);
            _session.Menu = new ShapeMenu(MenuJson, DrawingMenu.CommandOf);   // the SAME ids the menu rows carry

            Current = this;
        }

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active || _waypoints.Count == 0) return;

            // 🔒🔒 §4.7i (user ruling 2026-09-10) — A SUSPENDED TOOL DRAWS ITS WORK, NOT ITS HANDLES.
            //   The route line below ALWAYS draws (it is the work in progress); the waypoint handles and
            //   the handle-anchored context menu vanish entirely while focus is elsewhere. ⛔ Not dimmed.
            //   ⭐⭐ Also load-bearing for CE-259r — see the twin comment in VertexEditGizmo.UpdateAndDraw
            //   and docs/UX/UX_Feature_Tool_Model.md §4.7i.

            // ContextMenuBinding so right-clicking a handle shows the waypoint menu.
            if (IsFocused)
                draw.DrawContextMenuBinding(_networkId, MenuJson);

            // Route line segments.
            int segCount = _isLoop ? _waypoints.Count : _waypoints.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a = _waypoints[i];
                var b = _waypoints[(i + 1) % _waypoints.Count];
                draw.DrawLine(RouteGizmo.PointOf(view, a), RouteGizmo.PointOf(view, b),
                    new Rgba32(0x44, 0x88, 0xFF, 0xFF), 1.5f, SizeMode.ScreenPixels);
            }

            // Box2D handle per waypoint. ⛔ §4.7i — handles only while this gizmo holds focus.
            if (!IsFocused) return;

            ShapeDoneMark.Draw(draw, _session);   // ⭐ B-17 P2 — the on-map ✓

            for (int i = 0; i < _waypoints.Count; i++)
            {
                bool isActive = (i == _activeVertex);
                var pos = _waypoints[i].Position;
                var prim = default(DebugPrimitive);
                prim.Shape            = DebugPrimitiveShape.Box2D;
                prim.Space            = CoordinateSpace.World;
                prim.TargetView       = PipelineTarget.Map2D;
                prim.BoxCenterX       = pos.X;
                prim.BoxCenterY       = pos.Y;     // ⭐ CE-2172 — Y is north
                prim.BoxExtentX       = 8f;
                prim.BoxExtentY       = 8f;
                prim.Color            = isActive ? ActiveColor : IdleColor;
                prim.SubElementId     = (ushort)(i + 1);
                // 🔴 §6.7 — the `AnchorIndex`/`AnchorGeneration` writes are DELETED: identity is
                //   BoxAnchorId, and the ECS handle they carried is read by nothing.
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
            // ⭐ CE-2172 — the map's X / Y are the waypoint's; its height (Z) is not the drag's to change
            wp.Position = new Vector3(worldPos.X, worldPos.Y, wp.Position.Z);
            _waypoints[_activeVertex] = wp;
            _session.Set(_activeVertex, new Vector2(worldPos.X, worldPos.Y));
        }

        public void OnCommit(Vector3 worldPos)
        {
            // Finalize drag. Marker stays so more waypoints can be edited.
            // ⭐ CE-1096 (ROUTES1 §11.4 W-4) — and the waypoint STAYS SELECTED: a click is a press and a release, so deselecting here
            //   left the waypoint panel empty the moment the mouse came up (Escape / a deletion clear it)
            WriteBackAndPublish();
        }

        /// <summary>⭐ CE-1096 (W-3) — the waypoint panel changed a field: the route written back and published, as a drag's end does.</summary>
        public void CommitEdits() => WriteBackAndPublish();

        public void OnCancel()
        {
            if (_activeVertex >= 0 && _activeVertex < _waypoints.Count)
            {
                _waypoints[_activeVertex] = _savedWaypoint;
                _session.Set(_activeVertex, new Vector2(_savedWaypoint.Position.X, _savedWaypoint.Position.Y));
            }
            _activeVertex = -1;
        }

        public void OnMenuAction(int actionId)
        {
            // ⭐ B-17 P2 — the drawing rows (Finish · Cancel) are the session's; Insert / Delete are this editor's own and need a selected waypoint.
            if (DrawingMenu.CommandOf(actionId) != null) { Resolve(_session.ApplyMenuAction(actionId)); return; }
            if (_activeVertex < 0 || _activeVertex >= _waypoints.Count) return;

            if (actionId == 1) // Insert after
            {
                int at = _activeVertex;
                int next = (_waypoints.Count == 1) ? 0 : (at + 1) % _waypoints.Count;
                var midPos = (_waypoints[at].Position + _waypoints[next].Position) * 0.5f;
                var newWp  = new RouteWaypoint
                {
                    Position    = midPos,
                    TargetSpeed = _waypoints[at].TargetSpeed,
                };
                _waypoints.Insert(at + 1, newWp);
                _session.InsertOnSegment(at, new Vector2(midPos.X, midPos.Y));
                WriteBackAndPublish();
            }
            else if (actionId == 2) // Delete
            {
                _waypoints.RemoveAt(_activeVertex);
                _session.DeleteSelected();
                WriteBackAndPublish();
            }
        }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            // ⭐ B-17 P2 (D11; overrules ROUTES1 §11.1's right-click commit) — right-click is the MENU: it no longer writes back and exits.
            //   The on-map ✓ (a left click) finishes.
            if (button == MapMouseButton.Left && !isPressed && _session.HitDone(new Vector2(worldPos.X, worldPos.Y)))
                Resolve(_session.Finish());
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            // Enter finishes · Esc cancels (undo the drag in progress, then stop). ⛔ Backspace is not here: an editor has no "last point".
            if (key == MapKeyboardKey.Backspace) return;
            Resolve(_session.OnKey(key, isPressed));
        }

        // ⭐ ONE tail for every finish door (Enter, ✓, the menu) and every cancel door (Esc, the menu).
        private void Resolve(ShapeOutcome outcome)
        {
            if (outcome == ShapeOutcome.Finished)
            {
                WriteBackAndPublish();
                _onRemove();
            }
            else if (outcome == ShapeOutcome.Cancelled)
            {
                OnCancel();
                _onRemove();
            }
            // TooFew (deleted below the minimum): stays armed; the reason is on the session
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
            // CE-4046 — RoutePlan is an immutable record: build the new value and Set it (the recorder sees a changed version).
            plan = plan.WithWaypoints(wps =>
            {
                wps.Clear();
                wps.AddRange(_waypoints);
            });
            _repo.SetManagedComponent(_entity, plan);

            _repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId          = _networkId,
                ComponentsToUpdate = new List<object> { plan },
                RequestId          = Guid.NewGuid(),
            });
        }
    }
}
