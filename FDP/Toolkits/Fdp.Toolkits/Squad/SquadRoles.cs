using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-2281</c> DA-K (DA-19) — a squad member's ROLE, read in ONE place: his own <see cref="UnitSubordinate.Designation"/> (what
    /// the wire and succession write), else the roster's copy. ⭐ Succession, the medic pick and the plays' element split all read it here.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §12, §12c.
    /// </summary>
    public static class SquadRoles
    {
        /// <summary>The role of the member in roster slot <paramref name="slot"/>.</summary>
        public static TacticalDesignation Of(EntityRepository world, in UnitRoster roster, int slot)
        {
            var m = roster.SubordinateEntities[slot];
            return world.HasComponent<UnitSubordinate>(m) ? world.GetComponentRO<UnitSubordinate>(m).Designation
                                                          : (TacticalDesignation)roster.TacticalDesignations[slot];
        }

        /// <summary>DA-19 — the squad's automatic weapon: the automatic rifleman or the machine gunner. They go to the base of fire first.</summary>
        public static bool CarriesSupportWeapon(TacticalDesignation role)
            => role is TacticalDesignation.AutomaticRifleman or TacticalDesignation.MachineGunner;
    }
}
