using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Patching;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Exclusive-focus gizmo that lets the operator rotate a SimTransform entity by
    // repositioning the mouse. Triggered by context menu ActionId=20 ("Rotate").
    //
    // Design (gizmo-input-focus-design.md):
    // - RequiresExclusiveFocus = true: the IG terminal streams all raw HW events here.
    // - View and entity are stored at construction; UpdateAndDraw has no ECS arguments.
    // - On left-release: commits new heading to SimTransform.Rotation and removes itself.
    // - On right-press or Escape: cancels without writing back and removes itself.
    // - Self-removal: calls the onRemove Action provided by the owning system.
    public sealed class EntityRotatorGizmo : IEntityStatefulGizmo
    {
        private readonly EntityRepository _repo;
        private readonly Entity _entity;
        private readonly Action _onRemove;

        // ⭐⭐⭐ Axis-B item ④ — the subsystem-agnostic commit path.
        //    📄 docs/DESIGN_Cgf_AxisB_Rotation_Slice.md §2 (the routing model) · §6 ④.
        //    ⭐ When supplied, the commit goes through the shared writer: owned -> direct ECS write,
        //      unowned -> a change-request the OWNER applies. ⛔ Without it the gizmo could only ever
        //      work on a node that owns the entity — the SimHost-only ECS poke this slice removes.
        //    ⚠ OPTIONAL, and that is a deliberate compatibility choice rather than a silent default:
        //      the existing SimHost call site is unchanged and keeps the direct write it always had.
        //      📌 A production caller that HAS a writer must pass it — CgfSubsystem does.
        private readonly IEntityComponentWriter? _writer;

        // ⭐ B-17 P5 (DESIGN_Map_Interaction §2.1 / §2.5 "placing — heading") — on a MULTI-selection the rotate turns them all, ONE gesture / ONE undo step:
        //   each about its own centre (headings only) or the whole group about its centre; Tab toggles, the hint says so.
        private readonly GroupTransform? _group;
        private readonly Action<string?>? _onHint;
        private GroupRotateMode _mode = GroupRotateMode.Each;
        private float _yaw0;

        private Vector3 _entityPos;
        private Vector3 _currentCursorPos;
        private float _currentYawRad;
        private bool _active = true;

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        // view must be an EntityRepository (all SimHost ECS views are).
        // onRemove is called when the gizmo wants to stop (commit or cancel).
        public EntityRotatorGizmo(
            ISimulationView view,
            Entity entity,
            Action onRemove,
            IEntityComponentWriter? writer = null,
            Action<string?>? onHint = null)
        {
            _writer = writer;
            _repo    = view as EntityRepository
                ?? throw new ArgumentException("EntityRotatorGizmo requires direct EntityRepository access.", nameof(view));
            _entity  = entity;
            _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));

            if (_repo.HasComponent<SimTransform>(_entity))
            {
                ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(_entity);
                _entityPos = tf.Position;
                _currentCursorPos = _entityPos;
                _currentYawRad = SimMath.ExtractYaw(tf.Rotation);
                _yaw0 = _currentYawRad;
            }
            _onHint = onHint;
            _group = Hrot.ScenarioEditor.Map.MapUiStates.SelectionOf(_repo) is { } selection
                ? GroupTransform.ForGrab(_repo, selection, writer, entity)
                : null;
            if (_group != null) _centre = _group.Centre;
        }

        private Vector2 _centre;

        /// <summary>⭐ B-17 P5 — how a group rotate turns the selection now (<see cref="GroupRotateMode.Each"/> until Tab); meaningless for one unit.</summary>
        public GroupRotateMode Mode => _mode;

        /// <summary>⭐ B-17 P5 — whether this rotate acts on a multi-selection.</summary>
        public bool IsGroup => _group != null;

        /// <summary>⭐ B-17 P5 — the tool's line while a group is selected (P1's hint): names the key and the mode in force.</summary>
        public string? Hint => _group == null ? null
            : "drag: rotate · Tab: each / group centre (now: " + (_mode == GroupRotateMode.Each ? "each" : "group centre") + ") · right-click or Esc: cancel";

        // Draws a yellow heading arrow from the entity center in the current yaw direction.
        // Called every frame regardless of focus state.
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active) return;
            if (_group != null)
            {
                _onHint?.Invoke(Hint);
                DrawGroup(draw);
                return;
            }
            draw.DrawLine(_entityPos, _currentCursorPos, Rgba32.Yellow, thickness: 2f, sizeMode: SizeMode.ScreenPixels);

            float compassDeg = SimMath.YawRadToCompassDeg(_currentYawRad);
            string label = $"{compassDeg:F0}deg";
            float midX = (_entityPos.X + _currentCursorPos.X) * 0.5f;
            float midY = (_entityPos.Y + _currentCursorPos.Y) * 0.5f;
            draw.DrawTextLong(midX, midY + 15f, label, Rgba32.White);
        }

        // ⭐ B-17 P5 — the group's picture: a ring round the whole group in group-centre mode, an arrow from the pivot to the cursor, and the angle.
        private void DrawGroup(IDebugDrawBuilder draw)
        {
            bool about = _mode == GroupRotateMode.AboutCentre;
            var pivot = about ? new Vector3(_centre.X, _centre.Y, 0f) : _entityPos;
            if (about) draw.DrawSphere(pivot, MathF.Max(_group!.Radius + 3f, 6f), Rgba32.Yellow, thickness: 1f);   // thickness ⇒ an outline only (CE-3154)
            draw.DrawLine(pivot, _currentCursorPos, Rgba32.Yellow, thickness: 2f, sizeMode: SizeMode.ScreenPixels);
            float deg = (_currentYawRad - _yaw0) * (180f / MathF.PI);
            deg -= 360f * MathF.Round(deg / 360f);   // −180…180
            draw.DrawTextLong((pivot.X + _currentCursorPos.X) * 0.5f, (pivot.Y + _currentCursorPos.Y) * 0.5f + 15f,
                (about ? "group " : "each ") + $"{deg:+0;-0;0}deg", Rgba32.White);
        }

        // DragUpdate: recompute heading from the cursor world position.
        public void OnDragUpdate(Vector3 worldPos)
        {
            _currentCursorPos = worldPos;
            if (_group != null && _mode == GroupRotateMode.AboutCentre)
            {
                // the angle is measured at the GROUP's centre; the grabbed unit's heading follows the cursor from there
                float gx = worldPos.X - _centre.X, gy = worldPos.Y - _centre.Y;
                if (MathF.Abs(gx) > 0.001f || MathF.Abs(gy) > 0.001f) _currentYawRad = MathF.Atan2(gy, gx);
                return;
            }
            float dx = worldPos.X - _entityPos.X;
            float dy = worldPos.Y - _entityPos.Y;
            if (MathF.Abs(dx) > 0.001f || MathF.Abs(dy) > 0.001f)
                _currentYawRad = MathF.Atan2(dy, dx);
        }

        // Left released: commit the new heading to SimTransform.
        // Right pressed: cancel without writing back.
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (!_active) return;
            _currentCursorPos = worldPos;
            if (button == MapMouseButton.Left && !isPressed)
            {
                // ⭐ B-14 / undo U2 — the rotation is ONE step (it writes once, at the release)
                if (_group != null)
                    _group.Rotate(_currentYawRad - _yaw0, _mode);   // ⭐ B-17 P5 — the whole selection, ONE step
                else
                {
                    Fdp.Toolkit.Replication.Edits.EditSteps.Begin(_repo, "Rotate");
                    try { CommitRotation(); }
                    finally { Fdp.Toolkit.Replication.Edits.EditSteps.End(_repo); }
                }
                RequestRemove();
            }
            else if (button == MapMouseButton.Right && isPressed)
            {
                RequestRemove();
            }
        }

        // Escape pressed: cancel.
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (!_active) return;
            if (key == MapKeyboardKey.Escape && isPressed)
                RequestRemove();
            // ⭐ B-17 P5 — Tab toggles each / about the group centre. ⛔ Not "R": R is the Rotate tool's own shortcut and would re-arm (reset) this gizmo.
            else if (key == MapKeyboardKey.Tab && isPressed && _group != null)
                _mode = _mode == GroupRotateMode.Each ? GroupRotateMode.AboutCentre : GroupRotateMode.Each;
        }

        // These are unused for this exclusive-capture gizmo.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
        {
            _currentCursorPos = worldPos;
        }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }

        public void Dispose() { }

        // ---- private helpers ---------------------------------------------------

        private void CommitRotation()
        {
            if (!_repo.IsAlive(_entity)) return;
            if (!_repo.HasComponent<SimTransform>(_entity)) return;

            // ⭐⭐⭐ Axis-B: route through the shared writer when one was supplied.
            //    ⭐ It converts via the installer's own HeadingDegToRotation — ⛔ NOT a second copy of the
            //      compass convention — and decides owned-vs-unowned itself.
            //    ⚠ COMPASS DEGREES, not math yaw: the attribute is defined in the convention the wire and
            //      the DebugApi already use, and SimMath.YawRadToCompassDeg is the conversion this file
            //      ALREADY uses to draw its own label. ⛔ Sending radians here would be a second unit on
            //      one path.
            if (_writer != null)
            {
                float compassDeg = SimMath.YawRadToCompassDeg(_currentYawRad);
                _writer.Write(_entity, AttributeIds.Heading, compassDeg);
                return;
            }

            // ⚠ Legacy direct path, for a caller that supplied no writer. 📐 Correct only where this node
            //   owns SimTransform — which is why the writer exists.
            ref var tf = ref _repo.GetComponentRW<SimTransform>(_entity);
            tf.Rotation = SimMath.FromYaw(_currentYawRad);
        }

        private void RequestRemove()
        {
            _active = false;
            _onRemove();
        }
    }
}
