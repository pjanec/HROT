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
            if (_group != null) { _centre = _group.Centre; _aboutRing = MathF.Max(_group.Radius + 3f, 6f); }   // ⭐ once: the units do not move during the drag, and Radius walks the members (an allocating enumerator) — not per frame
        }

        private Vector2 _centre;
        private float _aboutRing = 6f;

        /// <summary>⭐ B-17 P5 — how a group rotate turns the selection now (<see cref="GroupRotateMode.Each"/> until Tab); meaningless for one unit.</summary>
        public GroupRotateMode Mode => _mode;

        /// <summary>⭐ B-17 P5 — whether this rotate acts on a multi-selection.</summary>
        public bool IsGroup => _group != null;

        /// <summary>⭐ B-17 P5 — the tool's line while a group is selected (P1's hint): names the key and the mode in force.</summary>
        public string? Hint => _group == null ? null : _mode == GroupRotateMode.Each ? HintEach : HintAbout;   // ⭐ constants: the hint is read every frame, so it must not concatenate (0 B steady)
        private const string HintEach = "drag: rotate · Tab: each / group centre (now: each) · right-click or Esc: cancel";
        private const string HintAbout = "drag: rotate · Tab: each / group centre (now: group centre) · right-click or Esc: cancel";

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

            float midX = (_entityPos.X + _currentCursorPos.X) * 0.5f;
            float midY = (_entityPos.Y + _currentCursorPos.Y) * 0.5f;
            draw.DrawTextLong(midX, midY + 15f, LabelFor(unit: true, about: false, (int)MathF.Round(SimMath.YawRadToCompassDeg(_currentYawRad))), Rgba32.White, style: GizmoTextStyle.Backdrop);
        }

        // ⭐ B-17 P7 ⑤ (sheet map-gizmo-look-2.svg row ⑤) — the group's picture: an amber DASHED ring round the pivot, the swept wedge from where the drag started (a fan of
        //   translucent triangles), the start ray dashed and the current ray solid, a knob where the current ray meets the ring, and the angle on a backdrop.
        //   ⛔ "Shift: 15° steps" is NOT in the label: the rotator has no Shift snapping (grep), so the label does not promise it.
        private static readonly Rgba32 Amber = new(0xF2, 0xC0, 0x33, 255);
        private static readonly Rgba32 AmberWedge = new(0xF2, 0xC0, 0x33, 40);
        private const int MaxWedgeTriangles = 24;
        private const float EachRingRadius = 6f;

        private void DrawGroup(IDebugDrawBuilder draw)
        {
            bool about = _mode == GroupRotateMode.AboutCentre;
            var pivot = about ? new Vector3(_centre.X, _centre.Y, 0f) : _entityPos;
            float ring = about ? _aboutRing : EachRingRadius;
            draw.DrawSphere(pivot, ring, Amber, thickness: 1f, style: LineStyle.Dashed);   // thickness ⇒ an outline only (CE-3154)

            float sweep = _currentYawRad - _yaw0;
            sweep -= 2f * MathF.PI * MathF.Round(sweep / (2f * MathF.PI));   // −π…π
            int tris = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / (2f * MathF.PI / MaxWedgeTriangles)), 1, MaxWedgeTriangles);
            var centre2 = new Vector2(pivot.X, pivot.Y);
            var prev = centre2 + new Vector2(MathF.Cos(_yaw0), MathF.Sin(_yaw0)) * ring;
            for (int i = 1; i <= tris; i++)
            {
                float a = _yaw0 + sweep * i / tris;
                var next = centre2 + new Vector2(MathF.Cos(a), MathF.Sin(a)) * ring;
                draw.DrawFilledTriangle(centre2, prev, next, AmberWedge);
                prev = next;
            }
            var startEnd = new Vector3(centre2.X + MathF.Cos(_yaw0) * ring, centre2.Y + MathF.Sin(_yaw0) * ring, 0f);
            var nowEnd = new Vector3(centre2.X + MathF.Cos(_currentYawRad) * ring, centre2.Y + MathF.Sin(_currentYawRad) * ring, 0f);
            draw.DrawLine(pivot, startEnd, Amber, thickness: 1f, sizeMode: SizeMode.ScreenPixels, style: LineStyle.Dashed);
            draw.DrawLine(pivot, nowEnd, Amber, thickness: 2f, sizeMode: SizeMode.ScreenPixels);
            draw.DrawSphere(pivot, 3f, Rgba32.White, sizeMode: SizeMode.ScreenPixels);   // the pivot dot
            draw.DrawSphere(nowEnd, 5f, Amber, sizeMode: SizeMode.ScreenPixels);          // the knob
            draw.DrawTextLong(nowEnd.X, nowEnd.Y, LabelFor(unit: false, about, (int)MathF.Round(sweep * (180f / MathF.PI))), Rgba32.White,
                lineOffsetPx: -26f, style: GizmoTextStyle.Backdrop);
        }

        // ⭐ B-17 P7 ⑤ — the label text, rebuilt only when the whole-degree value or the mode changes (the old code interpolated a new string EVERY frame:
        //   a steady frame must allocate 0 B — the hot-path rule).
        private string? _label;
        private int _labelDeg = int.MinValue;
        private int _labelKind = -1;
        private string LabelFor(bool unit, bool about, int deg)
        {
            int kind = unit ? 0 : about ? 1 : 2;
            if (_label == null || deg != _labelDeg || kind != _labelKind)
            {
                _labelDeg = deg; _labelKind = kind;
                _label = unit ? deg + "°" : (about ? "group " : "each ") + deg.ToString("+0;-0;0") + "°";
            }
            return _label;
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
