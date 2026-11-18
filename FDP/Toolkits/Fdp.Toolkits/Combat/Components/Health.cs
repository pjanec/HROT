using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// Hit-point pool. <see cref="Current"/> &lt;= 0 means the entity is destroyed/defeated.
    /// </summary>
    /// <remarks>
    /// <b>BUG2-A001:</b> Moved from <c>FDP.Toolkit.Combat</c> into this thin
    /// <c>FDP.Toolkit.Combat.Contracts</c> assembly so that <c>FDP.Toolkit.Behavior</c>
    /// can reference it without creating a circular dependency with <c>FDP.Toolkit.Combat</c>.
    /// Both Combat and Behavior now reference Contracts; neither references the other
    /// directly.  The component ID and field layout are unchanged.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CombatHealth)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct Health
    {
        public float Current;
        public float Max;

        /// <summary>
        /// ⭐ <c>CE-4661</c> (thermal drivers TD-7; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4h) — the sim time (s) the DERIVED injury state
        /// (<see cref="Fdp.Toolkit.Combat.Injury.StateOf(Fdp.ModuleHost.Abstractions.ISimulationView, Entity)"/>: ok / wounded / incapacitated / dead) last changed —
        /// for a dead body, its TIME OF DEATH. Stamped by the Health owner (<c>HealthStateSinceSystem</c>), replicated in <c>EntityDamage</c>, so every node and a
        /// late joiner read the same. ⛔ The state itself is never stored (CE-2155) — only its time. 0 = since the start (a body a scenario starts dead with).
        /// </summary>
        public double StateSince;
    }
}
