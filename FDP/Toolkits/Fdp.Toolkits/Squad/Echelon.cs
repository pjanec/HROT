using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-4589</c> (HQ-12, 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8e) — a unit's ECHELON by its roster's depth: 0 a unit
    /// of men (a squad) · 1 a unit of squads (a platoon) · 2 a unit of platoons (a company) · … The ONE helper an order chosen by echelon reads
    /// (a company handed a platoon's play would treat its platoons as squads). Allocation-free; a cycle stops at <see cref="CombatEffective.MaxDepth"/>.
    /// </summary>
    public static class Echelon
    {
        public const int Squad = 0, Platoon = 1, Company = 2;

        /// <summary><paramref name="unit"/>'s depth: 0 when no member of its roster is a unit, else 1 + the deepest member unit's.</summary>
        public static int DepthOf(ISimulationView view, Entity unit) => DepthOf(view, unit, 0);

        private static int DepthOf(ISimulationView view, Entity unit, int guard)
        {
            if (guard >= CombatEffective.MaxDepth || unit.IsNull || !view.IsAlive(unit) || !view.HasComponent<UnitRoster>(unit)) return 0;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(unit);
            int deepest = -1;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !view.IsAlive(m) || !CombatEffective.IsUnit(view, m)) continue;
                int d = DepthOf(view, m, guard + 1);
                if (d > deepest) deepest = d;
            }
            return deepest + 1;
        }
    }
}
