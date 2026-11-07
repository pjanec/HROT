using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Macros;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐⭐ A5 / LC-3 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §3) — the PASTE tool: the entity placement's own gesture (the ONE <see cref="PlacementSession"/>,
    /// CE-4309) over the copied group's ghosts (the macro preview's own drawing, <see cref="MacroGhostDraw"/>). The ghosts follow the cursor; PRESS sets where
    /// the anchor goes; DRAG turns the group about it with the ghosts still shown; RELEASE creates (<see cref="EntityClipboard.Paste"/>); a plain click keeps
    /// the group's own heading. ⛔ No wheel (the map zooms while placing). Right-click / Esc cancel. One paste per arming.
    /// <para>⛔ Hot path: the ghosts are expanded ONCE (when armed); a frame only turns their offsets.</para>
    /// </summary>
    public sealed class PasteGizmo : IEntityStatefulGizmo
    {
        private readonly EntityClipboard _clipboard;
        private readonly EntityRepository _world;
        private readonly Action _onRemove;
        private readonly PlacementSession _session = new();
        private readonly MacroPreviewResult? _preview;
        private Vector3 _cursor;

        public PasteGizmo(EntityClipboard clipboard, EntityRepository world, Action onRemove)
        {
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
            _preview = clipboard.Preview(world);
            _hint = $"paste {clipboard.Count}: click to place, drag to turn, Esc to cancel";   // built once (0 B a frame)
        }

        private readonly string _hint;

        /// <summary>What a release now would do: where the anchor lands and the group's heading (compass degrees) — the draw and the release share it.</summary>
        public (Vector3 At, float HeadingDeg) PendingAt(Vector3 cursor)
        {
            var (at, drag) = _session.Pending(cursor);
            return (at, drag is { } yaw ? SimMath.YawRadToCompassDeg(yaw) : _clipboard.Heading.Degrees);
        }

        /// <summary>How many pastes were sent (a rail reads it).</summary>
        public int Pasted { get; private set; }

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (_preview is null) return;
            var (at, heading) = PendingAt(_cursor);
            float turn = -(heading - _clipboard.Heading.Degrees) * (MathF.PI / 180f);   // compass is clockwise; the draw turns counter-clockwise
            MacroGhostDraw.Draw(draw, _preview, at, turn);
            draw.DrawTextLong(at.X, at.Y - 18f, _hint, Rgba32.White);
        }

        public void OnDragUpdate(Vector3 worldPos) => _cursor = worldPos;

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            button &= ~(MapMouseButton.ShiftMask | MapMouseButton.CtrlMask | MapMouseButton.AltMask);
            _cursor = worldPos;
            if (button == MapMouseButton.Left && isPressed) { _session.Press(worldPos); return; }
            if (button == MapMouseButton.Left && !isPressed)
            {
                var (at, heading) = PendingAt(worldPos);
                _session.ClearPress();
                if (_clipboard.Paste(_world, new WorldPoint(at.X, at.Y, at.Z), new Bearing(heading)) > 0) Pasted++;
                _onRemove();
                return;
            }
            if (button == MapMouseButton.Right && isPressed) _onRemove();
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed) _onRemove();
        }

        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void Dispose() { }
    }
}
