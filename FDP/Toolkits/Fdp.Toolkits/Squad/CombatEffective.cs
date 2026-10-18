using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐⭐ <c>CE-2262</c> SL-0 (CB-16, R-2030) — is a member IN THE FIGHT, a UNIT member included? A soldier is when
    /// <see cref="Injury.IsInTheFight"/> says so; a body-less unit (<see cref="UnitEntity"/> — a squad under a platoon) has no
    /// <c>Health</c>, so it is in the fight while at least <see cref="MinEffective"/> of its own members are (recursively: a
    /// platoon counts its squads). ⛔ Before this a unit read as in the fight with every man dead — the runner kept ordering a
    /// wiped-out squad and the platoon's centroid counted it. A unit with no <see cref="UnitRoster"/> at all is taken as in the fight
    /// (nothing to judge by); a roster that EMPTIED (its men destroyed) is out of it.
    /// 📄 <c>docs/designs/group-maneuvers/DESIGN_Building_Clearing.md</c> §7 SL-0, CB-16.
    /// </summary>
    public static class CombatEffective
    {
        /// <summary>The design's default: a unit stays effective while 4 of its members fight — or half its roster when that is
        /// fewer (a 4-man team needs 2).</summary>
        public const int DefaultMinEffective = 4;

        /// <summary>How deep a nested unit is followed (squad → platoon → company …); also ends a mis-authored cycle.</summary>
        public const int MaxDepth = 6;

        /// <summary>How many of <paramref name="rosterCount"/> members must fight for the unit to: <c>min(4, ⌈n/2⌉)</c>, at least 1.</summary>
        public static int MinEffective(int rosterCount)
            => Math.Max(1, Math.Min(DefaultMinEffective, (rosterCount + 1) / 2));

        /// <summary>Is <paramref name="member"/> in the fight — a soldier by his injury state, a unit by its members.</summary>
        public static bool IsInTheFight(ISimulationView view, Entity member) => IsInTheFight(view, member, 0);

        /// <summary>How many of <paramref name="unit"/>'s roster members are in the fight (a soldier unit: 0).</summary>
        public static int FightingMembers(ISimulationView view, Entity unit) => Fighting(view, unit, 0, out _);

        private static bool IsInTheFight(ISimulationView view, Entity member, int depth)
        {
            if (member.IsNull || !view.IsAlive(member)) return false;
            if (!IsUnit(view, member)) return Injury.IsInTheFight(view, member);
            if (depth >= MaxDepth) return false;
            if (!view.HasComponent<UnitRoster>(member)) return true;   // composition unknown (no roster yet): as before SL-0
            int fighting = Fighting(view, member, depth, out int count);
            return count > 0 && fighting >= MinEffective(count);       // an EMPTIED roster (its men destroyed) is out of the fight
        }

        private static int Fighting(ISimulationView view, Entity unit, int depth, out int count)
        {
            count = 0;
            if (!view.HasComponent<UnitRoster>(unit)) return 0;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(unit);
            count = roster.Count;
            int fighting = 0;
            for (int i = 0; i < roster.Count; i++)
                if (IsInTheFight(view, roster.SubordinateEntities[i], depth + 1)) fighting++;
            return fighting;
        }

        /// <summary>A body-less unit commander with a roster.</summary>
        public static bool IsUnit(ISimulationView view, Entity e)
            => view is not EntityRepository repo
                ? view.HasComponent<UnitEntity>(e)
                : repo.IsComponentTypeRegistered<UnitEntity>() && repo.HasComponent<UnitEntity>(e);
    }
}
