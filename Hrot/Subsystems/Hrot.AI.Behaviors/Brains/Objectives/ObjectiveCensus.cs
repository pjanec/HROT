using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Squad;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S1-d (📄 docs/DESIGN_Scenario_Rules.md §00.8) — the objectives' census: "the subject's men" = the subject itself or anyone
    /// under it down its <see cref="UnitRoster"/>, alive, in the fight, with a <see cref="SimTransform"/> — counted AS MEN (a squad commander
    /// has no body; counting bodies is what an area check can see). An enemy = a body in the fight of a force hostile to the subject's
    /// (<see cref="KillZone.Hostile(ForceId, ForceId)"/> — the one side rule), never a neutral.
    /// </summary>
    public static class ObjectiveCensus
    {
        /// <summary>The force of <paramref name="e"/> (Neutral when it has no <see cref="EntityInfo"/>).</summary>
        public static ForceId SideOf(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(e)
                ? world.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;

        /// <summary>A body in the fight: alive, not down, positioned. A body-less unit is not one.</summary>
        public static bool IsMan(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && !CombatEffective.IsUnit(world, e) && world.HasComponent<SimTransform>(e) && Injury.IsInTheFight(world, e);

        /// <summary>Adds the subject's men to <paramref name="into"/> (the subject itself when it is a body, else its roster, recursively).</summary>
        public static void MenOf(EntityRepository world, Entity subject, List<Entity> into) => MenOf(world, subject, into, 0);

        private static void MenOf(EntityRepository world, Entity e, List<Entity> into, int depth)
        {
            if (e.IsNull || !world.IsAlive(e) || depth > CombatEffective.MaxDepth) return;
            if (!CombatEffective.IsUnit(world, e)) { if (IsMan(world, e)) into.Add(e); return; }
            if (!world.HasComponent<UnitRoster>(e)) return;
            var roster = world.GetComponentRO<UnitRoster>(e);
            for (int i = 0; i < roster.Count; i++) MenOf(world, roster.SubordinateEntities[i], into, depth + 1);
        }

        private static readonly List<Entity> s_men = new();

        /// <summary>How many of the subject's men stand inside <paramref name="area"/>.</summary>
        public static int MenInside(EntityRepository world, EntityRef subject, in AreaLink area)
        {
            var e = subject.Resolve(world);
            if (e.IsNull) return 0;
            s_men.Clear();
            MenOf(world, e, s_men);
            int n = 0;
            foreach (var m in s_men) if (area.Contains(world, world.GetComponentRO<SimTransform>(m).Position)) n++;
            s_men.Clear();
            return n;
        }

        /// <summary>
        /// How many enemy bodies stand inside <paramref name="area"/>: in the fight, of a force hostile to <paramref name="subjectSide"/>, and of
        /// <paramref name="enemySide"/> when that is not Neutral. A subject of unknown side (Neutral) with no named enemy side has no enemies.
        /// </summary>
        public static int EnemiesInside(EntityRepository world, ForceId subjectSide, in AreaLink area, ForceId enemySide = ForceId.Neutral)
        {
            if (subjectSide == ForceId.Neutral && enemySide == ForceId.Neutral) return 0;
            int n = 0;
            foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
            {
                var theirs = world.GetComponentRO<EntityInfo>(e).ForceId;
                if (enemySide != ForceId.Neutral ? theirs != enemySide : !KillZone.Hostile(subjectSide, theirs)) continue;
                if (!IsMan(world, e) || !area.Contains(world, world.GetComponentRO<SimTransform>(e).Position)) continue;
                n++;
            }
            return n;
        }
    }
}
