using System;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Per-entity AI subsystem overlay toggles. Carried in <see cref="DebugState.Ai"/>
    /// alongside <see cref="BehaviorDebugFlags"/> in the <see cref="DebugState"/> family.
    /// Off-by-default; near-zero cost when all bits are zero (a single flag check per entity
    /// in the gizmo source query).
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
    }
}
