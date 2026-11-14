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
    /// ⭐⭐ CE-4244 (MA-10 MP-3; DESIGN_Macro_Authoring_Editor §10) — how a macro PREVIEW is drawn, ONE routine for both ways to see it: each ghost a dimmed marker in its side's colour with its name, a thin line back to the anchor, a ring at the
    /// anchor, the expander's failure in red. A gizmo emits only primitives, so the same lines show on the 2-D map and in the 3-D one (the placement gizmo's existing path, ⛔ no second one).
    /// </summary>
    public static class MacroGhostDraw
    {
        public const byte GhostAlpha = 110;
        public const float MarkerRadius = 2f;

        public static void Draw(IDebugDrawBuilder draw, MacroPreviewResult preview, Vector3 anchor) => Draw(draw, preview, anchor, 0f);

        /// <summary>⭐ A5 — the same ghosts turned by <paramref name="turnRad"/> about the anchor (math sense, counter-clockwise): the paste tool's drag.
        /// ⛔ No allocation: each offset is rotated in place of drawing.</summary>
        public static void Draw(IDebugDrawBuilder draw, MacroPreviewResult preview, Vector3 anchor, float turnRad)
        {
            float cos = MathF.Cos(turnRad), sin = MathF.Sin(turnRad);
            if (!preview.Ok)
            {
                draw.DrawTextLong(anchor.X, anchor.Y + 12f, preview.FailureText, new Rgba32(230, 90, 80, 255));
                return;
            }
            var ring = new Rgba32(200, 200, 200, GhostAlpha);
            draw.DrawSphere(anchor, MarkerRadius * 1.5f, ring);
            var ghosts = preview.Ghosts;
            for (int i = 0; i < ghosts.Count; i++)   // ⛔ R-2084 — an index loop: a foreach over the interface boxed its enumerator every frame
            {
                var g = ghosts[i];
                var colour = Hrot.Common.Diagnostics.Gizmos.SidePalette.Of(g.Side);
                colour.A = GhostAlpha;
                var at = anchor + new Vector3(g.Offset.X * cos - g.Offset.Y * sin, g.Offset.X * sin + g.Offset.Y * cos, g.Offset.Z);
                draw.DrawSphere(at, MarkerRadius, colour);
                draw.DrawLine(anchor, at, ring, 1f);
                draw.DrawTextLong(at.X, at.Y - 6f, g.Name, new Rgba32(255, 255, 255, GhostAlpha + 60));
            }
            draw.DrawTextLong(anchor.X, anchor.Y + 12f, preview.Summary, Rgba32.White);
        }
    }

    /// <summary>
    /// ⭐ CE-4244 (MP-2 b) — the Do tab's <i>Preview</i> of an <c>InsertMacro</c> step: ghosts at the step's anchor, no interaction (the bar in the Do tab has <i>Insert now</i> / <i>Close</i>). Registered with the global gizmo manager by the spawn adapter,
    /// removed by <c>Close</c>.
    /// </summary>
    public sealed class MacroPreviewGizmo : IEntityStatefulGizmo
    {
        private readonly MacroPreviewResult _preview;
        private readonly Vector3 _anchor;

        public MacroPreviewGizmo(MacroPreviewResult preview, Vector3 anchor) { _preview = preview; _anchor = anchor; }

        public MacroPreviewResult Preview => _preview;
        public bool RequiresExclusiveFocus => false;
        public bool WantsRawInput => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw) => MacroGhostDraw.Draw(draw, _preview, _anchor);
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void Dispose() { }
    }
}
