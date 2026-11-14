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

        /// <summary>A body in the fight: alive, not down, positioned. A body-less unit is not one; neither is an objective (a status-keeping entity that happens to carry a side and a position — S7 P-5 serves every "man of a side").</summary>
        public static bool IsMan(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && !CombatEffective.IsUnit(world, e) && world.HasComponent<SimTransform>(e) && Injury.IsInTheFight(world, e)
               && !(world.IsComponentTypeRegistered<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>() && world.HasComponent<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>(e));

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

        /// <summary>⭐ rules S4 E-2 — the subject and EVERY entity under it down its <see cref="UnitRoster"/> (units and bodies alike, in the fight or not): who a report "from the subject's side" may name.
        /// Not <c>MenOf</c> (bodies in the fight only) — a commander has no body.</summary>
        public static void RosterOf(EntityRepository world, Entity subject, List<Entity> into) => Fdp.Toolkit.Squad.UnitTree.Collect(world, subject, into);   // ⭐ the ONE walk (condition editor C1 lifted it)

        [System.ThreadStatic] private static List<Entity>? t_men;
        private static List<Entity> s_men => t_men ??= new();   // ⭐ N-11 (CE-4473) — per THREAD: the brains tick in parallel; scratch is never shared

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

        /// <summary>⭐ S6 G-1 — how many bodies of <paramref name="side"/> (EVERY man of the side, whoever his unit — a respawned bot is in no roster) stand inside <paramref name="area"/>, in the fight.
        /// ⭐ S7 P-4: with <paramref name="into"/>, the men are also added to it (the ONE test — the capture event names the same men the census counted).</summary>
        public static int MenOfSideInside(EntityRepository world, ForceId side, in AreaLink area, List<Entity>? into = null)
        {
            if (side == ForceId.Neutral) return 0;
            // ⭐ CE-2416 — only the bodies inside the area's bounding circle are tested (the shared frame grid, built once per frame for every
            //   objective and side): it tested every positioned entity of the world (cars too) per objective per side — 📐 ~30 % of the brain
            //   tick on the CE-2408 stress world. 📐 Both timed on the SAME 1 633 calls (700 soldiers): grid 246 ms, scan 3 104 ms — 12.6×,
            //   0 mismatches, ~22 ms per CGF frame. The same test (force, IsMan, area.Contains on the LIVE position); the men in entity order.
            if (GridOn && world.IsComponentTypeRegistered<EntityInfo>() && world.IsComponentTypeRegistered<SimTransform>())
            {
                area.BoundingCircle(world, out var centre, out float radius);
                if (radius > 0f) return MenOfSideInsideByGrid(world, side, in area, centre, radius, into);
            }
            int n = 0;
            foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
            {
                if (world.GetComponentRO<EntityInfo>(e).ForceId != side) continue;
                if (!IsMan(world, e) || !area.Contains(world, world.GetComponentRO<SimTransform>(e).Position)) continue;
                into?.Add(e);
                n++;
            }
            return n;
        }

        /// <summary>The rail's seam: false takes the scan, so <c>ScenarioRulesTests.CE2416_…</c> can compare both answers.</summary>
        internal static bool GridOn = true;

        /// <summary>How far a man may have moved within the frame and still be found where the frame's grid placed him.</summary>
        private const float MovedSlackMetres = 4f;

        private sealed class Bodies
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, Bodies> s_of = new();
            public static Bodies Of(EntityRepository world) => s_of.GetValue(world, static _ => new Bodies());
            public readonly Fdp.Toolkit.Spatial.FrameEntityGrid Grid = new();
            public EntityQuery? Query;
            public readonly List<Entity> Hits = new();
            public static readonly System.Func<EntityRepository, Entity, bool> Member = static (w, e) => w.HasComponent<EntityInfo>(e);
        }

        private static int MenOfSideInsideByGrid(EntityRepository world, ForceId side, in AreaLink area, WorldPoint centre, float radius,
                                                 List<Entity>? into)
        {
            var b = Bodies.Of(world);
            lock (b)
            {
                b.Query ??= world.Query().With<EntityInfo>().With<SimTransform>().Build();
                b.Grid.EnsureFresh(world, b.Query, Bodies.Member);
                b.Grid.Circle(new System.Numerics.Vector2((float)centre.X, (float)centre.Y), radius + MovedSlackMetres);
                var hits = b.Hits;
                hits.Clear();
                foreach (int k in b.Grid.Found)
                {
                    var e = b.Grid.EntityAt(k);
                    if (!world.IsAlive(e) || !world.HasComponent<EntityInfo>(e) || world.GetComponentRO<EntityInfo>(e).ForceId != side) continue;
                    if (!IsMan(world, e) || !area.Contains(world, world.GetComponentRO<SimTransform>(e).Position)) continue;
                    hits.Add(e);
                }
                if (into != null)
                {
                    hits.Sort(static (x, y) => x.Index.CompareTo(y.Index));   // as the scan met them
                    into.AddRange(hits);
                }
                return hits.Count;
            }
        }

        /// <summary>⭐ S7 P-5 — every living man of <paramref name="side"/>, whoever his unit (a respawned bot is in no roster): added to <paramref name="into"/>.</summary>
        public static void MenOfSide(EntityRepository world, ForceId side, List<Entity> into)
        {
            if (side == ForceId.Neutral) return;
            foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
                if (world.GetComponentRO<EntityInfo>(e).ForceId == side && IsMan(world, e)) into.Add(e);
        }

        /// <summary>⭐ S6 G-2 — how many bodies of <paramref name="side"/> exist but are OUT of the fight (down or dead, still an entity): not a unit, positioned, alive as an entity, not in the fight.</summary>
        public static int DownOf(EntityRepository world, ForceId side)
        {
            if (side == ForceId.Neutral) return 0;
            int n = 0;
            foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
            {
                if (world.GetComponentRO<EntityInfo>(e).ForceId != side) continue;
                if (CombatEffective.IsUnit(world, e) || !world.IsAlive(e) || Injury.IsInTheFight(world, e)) continue;
                n++;
            }
            return n;
        }
    }
}
