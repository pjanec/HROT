using System;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Per-entity behavior-subsystem debug toggles. One feature-group enum carried
    /// inside <see cref="DebugState"/> alongside future feature groups for other
    /// subsystems (physics, network, …).
    /// </summary>
    [Flags]
    public enum BehaviorDebugFlags : uint
    {
        None              = 0,

        /// <summary>Provision and write to the per-entity 1KB BTree/HSM trace ring buffer.</summary>
        EnableTraceBuffer = 1u << 0,

        /// <summary>Decode the newest trace records each tick and emit them to the NLog "AI.Behavior" target.</summary>
        EmitToLog         = 1u << 1,

        /// <summary>Use TraceLevel.Tier1 for HSM filter (transitions + events + state changes).</summary>
        HsmTraceTier1     = 1u << 2,

        /// <summary>Use TraceLevel.Tier2 for HSM filter (Tier1 + actions + timers).</summary>
        HsmTraceTier2     = 1u << 3,

        /// <summary>Use TraceLevel.Tier3 for HSM filter (Tier2 + guards + activities).</summary>
        HsmTraceTier3     = 1u << 4,
    }
}
