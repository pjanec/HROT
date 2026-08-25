using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Patching;

namespace Hrot.SimHost.Gizmos
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
        private readonly Hrot.SimHost.Installers.IEntityComponentWriter? _writer;

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
            Hrot.SimHost.Installers.IEntityComponentWriter? writer = null)
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
            }
        }

        // Draws a yellow heading arrow from the entity center in the current yaw direction.
        // Called every frame regardless of focus state.
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (!_active) return;
            draw.DrawLine(_entityPos, _currentCursorPos, Rgba32.Yellow, thickness: 2f, sizeMode: SizeMode.ScreenPixels);

            float compassDeg = SimMath.YawRadToCompassDeg(_currentYawRad);
            string label = $"{compassDeg:F0}deg";
            float midX = (_entityPos.X + _currentCursorPos.X) * 0.5f;
            float midY = (_entityPos.Y + _currentCursorPos.Y) * 0.5f;
            draw.DrawTextLong(midX, midY + 15f, label, Rgba32.White);
        }

        // DragUpdate: recompute heading from the cursor world position.
        public void OnDragUpdate(Vector3 worldPos)
        {
            _currentCursorPos = worldPos;
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
                CommitRotation();
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
                _writer.Write(_entity, AttributeIds.GeoHeading, compassDeg);
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
