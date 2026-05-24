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
    // Exclusive-focus gizmo that lets the operator drag individual vertices of
    // an EditablePolyline entity. One vertex drag = one interaction session.
    // The marker stays between sessions so multiple vertices can be edited in sequence.
    //
    // Design:
    // - RequiresExclusiveFocus = true: terminal hit-testing is filtered to this entity while active.
    // - WantsRawInput = true: right-click release and Escape are delivered to this gizmo.
    // - SubElementId = vertexIndex + 1 (0 is reserved as "no handle").
    // - AnchorIndex / AnchorGeneration encode the ECS Entity.
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
        private static readonly Rgba32 EdgeColor   = new Rgba32(255, 255, 0, 255);

        private readonly EntityRepository _repo;
        private readonly Entity           _entity;
        private readonly long             _networkId;
        private readonly Action           _onRemove;
        private readonly Vector2          _originOffset;

        // Working copy in ABSOLUTE world space (= relative Points + origin).
        private readonly List<Vector2> _points;

        // Index of the vertex being dragged (-1 = none).
        private int     _activeVertex = -1;
        private Vector2 _savedPos;

        private bool _active = true;

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
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

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active || _points.Count == 0) return;

            // ContextMenuBinding so right-clicking a vertex handle shows the insert/delete menu.
            draw.DrawContextMenuBinding(_networkId, MenuJson);

            // Draw live preview edges so the edited shape is visible during drag.
            int n = _points.Count;
            for (int i = 0; i < n; i++)
            {
                var a = new Vector3(_points[i].X, _points[i].Y, 0f);
                var b = new Vector3(_points[(i + 1) % n].X, _points[(i + 1) % n].Y, 0f);
                draw.DrawLine(a, b, EdgeColor, thickness: 2f, sizeMode: SizeMode.ScreenPixels);
            }

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
                prim.BoxAnchorId      = _networkId;
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

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Right && !isPressed)
            {
                WriteBackAndPublish();
                _activeVertex = -1;
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
