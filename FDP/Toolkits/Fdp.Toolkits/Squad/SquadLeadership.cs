using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-2125</c> (§7.2 L1/L2) — on a squad commander, on CGF: the member who leads (the one designated <c>SquadLeader</c>,
    /// remembered so its death is an EDGE) and the sim time until which the commander gives no new orders after its leader fell.
    /// Written only by <c>SquadSensing.Succession</c>; read by the squad runner. Not saved, not sent.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SquadLeadership)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct SquadLeadership
    {
        /// <summary>The leading member; <see cref="Entity.Null"/> when the squad has none designated.</summary>
        public Entity Leader;
        /// <summary>Sim time until which the commander is silent (no new orders); 0 = not silent.</summary>
        public double SilentUntil;

        /// <summary>Is <paramref name="commander"/> silent at <paramref name="now"/>?</summary>
        public static bool Silent(ISimulationView view, Entity commander, double now)
            => view.HasComponent<SquadLeadership>(commander) && view.GetComponentRO<SquadLeadership>(commander).SilentUntil > now;
    }
}
