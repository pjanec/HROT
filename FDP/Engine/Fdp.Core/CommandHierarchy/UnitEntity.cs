using System.Runtime.InteropServices;

namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// ⭐⭐ <c>CE-2125</c> (R-272) — this entity is a UNIT: the body-less commander of a squad, platoon or any composite, which runs
    /// the unit's coordination while its members (the leader soldier among them) fight. It is NOT a physical actor: it is never
    /// seen, heard or found by an area search (<c>PhysicalPresence</c>), and its owner places it at its live members' centroid
    /// (<c>UnitCentroidSystem</c>, CE-514). Stamped from the TKB composite description on every node (a type-level fact, so not
    /// saved and not sent). 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md</c> §7.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.UnitEntity)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct UnitEntity
    {
        /// <summary>How long the commander gives no new orders after its leader soldier falls (s) — the lull an observer sees
        /// while the next man takes over (§7.2 L2). From the unit type.</summary>
        public float CommandDisruptionSeconds;
    }
}
