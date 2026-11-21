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

        /// <summary>
        /// ⭐ <c>CE-4670</c> (vehicle appearance VA-7, VA-S1; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §3a) — HOW a vehicle was destroyed:
        /// decided ONCE by the Health owner on the hit that kills it (<c>DestructionKinds.Decide</c>, from the killing hit's part and the type's stowage), cleared
        /// when it lives again. The one fact a renderer cannot derive (a thrown turret, a burnt-out hull). <see cref="DestructionKind.None"/> for a person and
        /// for a body that died without a hit. APPENDED (R-169), replicated in <c>EntityDamage</c>.
        /// </summary>
        public DestructionKind Destruction;
    }

    /// <summary>⭐ <c>CE-4670</c> (VA-7) — how a vehicle was destroyed.</summary>
    public enum DestructionKind : byte
    {
        /// <summary>Not destroyed, a person, or no kind decided (it died without a hit).</summary>
        None = 0,
        /// <summary>The crew or the vehicle is out, the hull intact.</summary>
        KnockedOut = 1,
        /// <summary>A fuel fire burnt it out.</summary>
        BurntOut = 2,
        /// <summary>The ammunition went up — the turret thrown.</summary>
        Catastrophic = 3,
    }
}
