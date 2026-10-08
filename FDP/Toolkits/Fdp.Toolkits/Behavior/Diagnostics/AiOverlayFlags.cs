using System;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// ⭐ <c>CE-3120</c> (R-227) — the map GIZMO FAMILIES, and per unit (<see cref="DebugState.Ai"/>) which families are PINNED on
    /// it: a pinned family draws for that unit even when the family's scope is "selected only" and the unit is not selected.
    /// A projector names its family in <c>[GizmoProjector(Family = ...)]</c>. Off by default. ⚠ These bits first served the
    /// <c>IGizmoSource</c> overlay family, folded into gizmos by <c>CE-3121</c>. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
    /// </summary>
    [Flags]
    public enum AiOverlayFlags : ushort
    {
        None            = 0,
        Perception      = 1 << 0,   // FOV cone, LOS rays, sensor ring
        TargetMemory    = 1 << 1,   // known contacts, aging, threat value
        Eqs             = 1 << 2,   // scored candidate points, Top-K highlight
        UtilityDecision = 1 << 3,   // per-option bars, winner, consideration breakdown
        SquadAssignment = 1 << 4,   // leader-member-target assignment lines
        Channels        = 1 << 5,   // active locomotion/weapon/interaction action
        /// <summary>⭐ <c>CE-3120</c> — the planned path and its look-ahead (<c>PlannedPathGizmo</c>).</summary>
        Path            = 1 << 6,
    }
}
