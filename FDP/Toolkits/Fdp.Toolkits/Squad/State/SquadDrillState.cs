using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-4047</c> (ui, cross-lane — the coordinator's replay-UI-state handoff ③; Brain View R-1040 / BV-62) — the RECORDED headline of a
    /// commander's squad drill: whether a play runs, which, its phase and since when. Written beside <see cref="SquadDrillTrace"/> by its one
    /// <c>Put</c>, so every trace change updates it; read by the Brain tab's Squad section, which therefore shows in a replay at any scrubbed
    /// frame. ⭐ The trace itself (orders, phase history, gates, care) stays brain-local and transient — an immutable managed class whose
    /// arrays would not record as values; this fixed-size struct is what replay needs to read.
    /// </summary>
    [ComponentId(GlobalComponentIds.SquadDrillState)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadDrillState
    {
        /// <summary>1 while a drill runs.</summary>
        public byte Active;
        /// <summary>The drill (the trace's <see cref="SquadDrillTrace.Kind"/>).</summary>
        public ushort Kind;
        /// <summary>Its current phase.</summary>
        public ushort Phase;
        /// <summary>Sim time the phase began.</summary>
        public double PhaseSince;
    }
}
