using System;
using System.Numerics;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Shared mutable state for the rubber-band (box) selection overlay.
    /// Held by both <see cref="RubberBandGizmo"/> and
    /// <see cref="Hrot.ScenarioEditor.Systems.SelectionInteractionSystem"/>.
    /// <para>⭐ CE-1136 (docs/DESIGN_Map_3D_Mode.md §6z) — in 3-D the band is a SCREEN rectangle: <see cref="Projector"/> (set each frame by
    /// the 3-D view's <c>RubberBandOverlay3D</c> while the map is in 3-D, null in 2-D) turns a world point into a pixel; a band started with
    /// one is <see cref="InScreen"/> and selects the entities whose drawn position projects inside it.</para>
    /// </summary>
    public sealed class RubberBandState
    {
        public bool IsActive;
        public Vector2 Start;
        public Vector2 Current;

        /// <summary>⭐ CE-1136 — world → screen pixel while the map shows 3-D (null in 2-D, or behind the eye).</summary>
        public Func<Vector3, Vector2?>? Projector { get; set; }

        /// <summary>⭐ CE-1136 — the band was started in 3-D: <see cref="ScreenStart"/> / <see cref="ScreenCurrent"/> are its corners.</summary>
        public bool InScreen;
        public Vector2 ScreenStart;
        public Vector2 ScreenCurrent;
    }
}
