using System.Numerics;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Shared mutable state for the rubber-band (box) selection overlay.
    /// Held by both <see cref="RubberBandGizmo"/> and
    /// <see cref="Hrot.ScenarioEditor.Systems.SelectionInteractionSystem"/>.
    /// </summary>
    public sealed class RubberBandState
    {
        public bool IsActive;
        public Vector2 Start;
        public Vector2 Current;
    }
}
