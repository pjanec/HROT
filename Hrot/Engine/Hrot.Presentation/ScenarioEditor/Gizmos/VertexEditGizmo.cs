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
using Hrot.ScenarioEditor.Tools;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Exclusive-focus gizmo that lets the operator drag individual vertices of
    // an EditablePolyline entity. One vertex drag = one interaction session.
    // The marker stays between sessions so multiple vertices can be edited in sequence.
    //
    // Design:
    // - RequiresExclusiveFocus = true: terminal hit-testing is filtered to this entity while active.
    // - WantsRawInput = true: Enter, Escape and the on-map ✓ click are delivered to this gizmo. ⭐ B-17 P2 (D11): right-click is the MENU, it ends nothing.
    // - SubElementId = vertexIndex + 1 (0 is reserved as "no handle").
    // - BoxAnchorId carries the entity's NETWORK id (§6.7); no ECS handle is emitted.
    // - OnCommit: writes back relative points to EditablePolyline, publishes UpdateEntityCommand.
    // - OnCancel: reverts the dragged vertex.
    // - OnMenuAction(1): insert a new vertex after the active one.
    // - OnMenuAction(2): delete the active vertex.
    // - OnCommit does NOT call _onRemove() — the marker stays for multiple drags (:139-144).
    // - ⭐ IT DOES self-remove to END the tool: right-RELEASE (:174-179) and Escape (:182-189) both
    //   write back and then call _onRemove().
    //   🔴 CORRECTED 2026-09-10. This line used to read "The gizmo does NOT call _onRemove() on its own",
    //   which was false from the moment the right-release arm existed — and that wrong comment is why the
    //   lifetime desync below went unnoticed. ⇒ _onRemove() must update BOTH the arbiter and the
    //   ToolController, which is what ScenarioToolRegistrations now composes (IToolController
    //   .NotifyToolEnded, docs/UX/UX_Feature_Tool_Model.md §4.7h). ⛔ A gizmo that self-removes and tells
    //   only the arbiter leaves the controller believing this tool is still armed.
    public sealed class VertexEditGizmo : IEntityStatefulGizmo
    {
        // Context menu JSON: array format required by ContextMenuAdapter.
        // ⭐ B-17 P2 — the editor's own two rows, then the catalogue's Finish · Cancel (right-click is the MENU; it no longer ends the edit).
        private static readonly string MenuJson = DrawingActions.JsonWith(
            new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 1, Label = "Insert point after" },
            new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 2, Label = "Delete point" });

        private static readonly Rgba32 IdleColor   = new Rgba32(0, 210, 120, 220);
        private static readonly Rgba32 ActiveColor = Rgba32.Red;
        private static readonly Rgba32 EdgeColor   = new Rgba32(255, 255, 0, 255);

        private readonly EntityRepository _repo;
        private readonly Entity           _entity;
        private readonly int             _networkId;
        private readonly Action           _onRemove;
        private readonly Vector2          _originOffset;

        // ⭐ B-17 P2 — the working copy lives in the ONE drawing core: ABSOLUTE world points (= relative Points + origin), the selected vertex,
        //   insert / delete, and the finish / cancel convention (Enter · ✓ · the menu finish; Esc cancels; right-click is the menu).
        private readonly ShapeEditSession _session = new(minPoints: 3, closed: true, noun: "an area");

        /// <summary>⭐ B-17 P2 — the drawing core this editor is a view of.</summary>
        public ShapeEditSession Session => _session;

        private IReadOnlyList<Vector2> _points => _session.Points;

        // Index of the vertex being dragged (-1 = none) — the session's selected point.
        private int _activeVertex { get => _session.Selected; set => _session.Selected = value; }
        private Vector2 _savedPos;

        private bool _active = true;

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public VertexEditGizmo(ISimulationView view, Entity entity, int networkId, Action onRemove)
        {
            _repo      = view as EntityRepository
                ?? throw new ArgumentException("VertexEditGizmo requires EntityRepository access.", nameof(view));
            _entity    = entity;
            _networkId = networkId;
            _onRemove  = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
            _session.Menu = new ShapeMenu(MenuJson, DrawingMenu.CommandOf);   // the SAME ids the menu rows carry

            _originOffset = Vector2.Zero;
            if (_repo.HasComponent<SimTransform>(_entity))
            {
                ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(_entity);
                _originOffset = new Vector2(tf.Position.X, tf.Position.Y);
            }

            // Load current points into working copy in absolute world coords.
            if (_repo.HasManagedComponent<EditablePolyline>(_entity))
            {
                var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
                if (poly.Points != null)
                {
                    var abs = new List<Vector2>(poly.Points.Count);
                    foreach (var p in poly.Points)
                        abs.Add(_originOffset + p);
                    _session.Load(abs);
                }
            }
        }

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active || _points.Count == 0) return;

            // 🔒🔒 §4.7i (user ruling 2026-09-10) — A SUSPENDED TOOL DRAWS ITS WORK, NOT ITS HANDLES.
            //   "handles gone entirely while suspended, and re-appear once focus returns. Inactive handle
            //    is best expressed by not being shown at all... The partial route or shape edited should
            //    stay drawn."
            //   ⇒ the edge preview below ALWAYS draws (it is the work in progress); the handles and the
            //     handle-anchored context menu are affordances and vanish entirely while focus is elsewhere.
            //     ⛔ Not dimmed — ABSENT, so "cannot be manipulated" is unambiguous.
            //   ⭐⭐ ALSO LOAD-BEARING FOR CE-259r: once the gizmo group is reordered so the entity
            //     emitters run first, a layer-0 z-order tie is broken by emission order in favour of
            //     HANDLES (DebugGizmoLayer.cs:510). Correct for an ACTIVE tool (user ruling) — but a
            //     SUSPENDED tool's handles would then steal the entity picker's hover, and the picker
            //     hit-tests UNFILTERED by design so it has no capture filter to shield it. Hiding these
            //     deletes the competitor. 📄 docs/UX/UX_Feature_Tool_Model.md §4.7i.
            //   📐 IsFocused is granted at ARM time (DataDrivenGizmoSystem.ActivateGizmo:94 → TryGrant →
            //     SetFocus(true):63), so a freshly armed tool shows its handles on frame one.

            // ContextMenuBinding so right-clicking a vertex handle shows the insert/delete menu.
            if (IsFocused)
                draw.DrawContextMenuBinding(_networkId, MenuJson);

            // Draw live preview edges so the edited shape is visible during drag.
            int n = _points.Count;
            for (int i = 0; i < n; i++)
            {
                var a = new Vector3(_points[i].X, _points[i].Y, 0f);
                var b = new Vector3(_points[(i + 1) % n].X, _points[(i + 1) % n].Y, 0f);
                draw.DrawLine(a, b, EdgeColor, thickness: 2f, sizeMode: SizeMode.ScreenPixels);
            }

            // Box2D handle for each vertex. ⛔ §4.7i — handles only while this gizmo holds focus.
            if (!IsFocused) return;

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
                // 🔴 §6.7 — the `AnchorIndex`/`AnchorGeneration` writes are DELETED: identity is
                //   BoxAnchorId, and the ECS handle they carried is read by nothing.
                prim.BoxAnchorId      = _networkId;
                draw.EmitRaw(in prim);
            }

            ShapeDoneMark.Draw(draw, _session);   // ⭐ B-17 P2 — the on-map ✓
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
            _session.Set(_activeVertex, new Vector2(worldPos.X, worldPos.Y));
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
                _session.Set(_activeVertex, _savedPos);
            _activeVertex = -1;
        }

        public void OnMenuAction(int actionId)
        {
            // ⭐ B-17 P2 — the drawing rows (Finish · Cancel) are the session's; Insert / Delete are this editor's own and need a selected vertex.
            if (DrawingMenu.CommandOf(actionId) != null) { Resolve(_session.ApplyMenuAction(actionId)); return; }
            if (_activeVertex < 0 || _activeVertex >= _points.Count) return;

            if (actionId == 1) // Insert after
            {
                _session.InsertMidpointAfter(_activeVertex);
                WriteBackAndPublish();
            }
            else if (actionId == 2) // Delete
            {
                _session.DeleteSelected();
                WriteBackAndPublish();
            }
        }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            // ⭐ B-17 P2 (D11) — right-click is the MENU: it no longer writes back and exits. The on-map ✓ (a left click) finishes.
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
                _activeVertex = -1;
                _onRemove();
            }
            else if (outcome == ShapeOutcome.Cancelled)
            {
                OnCancel();
                _onRemove();
            }
            // TooFew (deleted below the minimum): stays armed; the reason is on the session
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
