using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-4270</c>-era editing UX, condition language C1 (📄 <c>docs/UX/DESIGN_Condition_Editor.md</c> §5, R-4142): the ONE walk "a unit and
    /// everything under it", down its <see cref="UnitRoster"/>, at most <see cref="CombatEffective.MaxDepth"/> levels (which also ends a
    /// mis-authored cycle). Lifted from the objectives' census (rules S1-d, <c>ObjectiveCensus.RosterOf</c>) so a condition can say it
    /// (<c>InUnit</c>) — ⛔ never a second walker.
    /// </summary>
    public static class UnitTree
    {
        /// <summary>Is <paramref name="e"/> the <paramref name="unit"/> itself (when <paramref name="includeSelf"/>) or anywhere under it? 0 B.</summary>
        public static bool Contains(ISimulationView view, Entity unit, Entity e, bool includeSelf = true)
        {
            if (unit.IsNull || e.IsNull || !view.IsAlive(unit)) return false;
            if (unit == e) return includeSelf;
            return Under(view, unit, e, 0);
        }

        private static bool Under(ISimulationView view, Entity unit, Entity e, int depth)
        {
            if (depth > CombatEffective.MaxDepth || !view.HasComponent<UnitRoster>(unit)) return false;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var member = roster.SubordinateEntities[i];
                if (member.IsNull || !view.IsAlive(member)) continue;
                if (member == e || Under(view, member, e, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>Adds <paramref name="subject"/> and every entity under it (units and bodies alike, in the fight or not) to <paramref name="into"/>, once each.</summary>
        public static void Collect(ISimulationView view, Entity subject, List<Entity> into) => Collect(view, subject, into, 0);

        private static void Collect(ISimulationView view, Entity e, List<Entity> into, int depth)
        {
            if (e.IsNull || !view.IsAlive(e) || depth > CombatEffective.MaxDepth || into.Contains(e)) return;
            into.Add(e);
            if (!view.HasComponent<UnitRoster>(e)) return;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(e);
            for (int i = 0; i < roster.Count; i++) Collect(view, roster.SubordinateEntities[i], into, depth + 1);
        }
    }
}
