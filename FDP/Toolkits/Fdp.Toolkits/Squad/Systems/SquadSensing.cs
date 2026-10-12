using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> S2 — the SQUAD's sensing edges, published on the COMMANDER so its standing orders (SOP), its
    /// <see cref="RecentSenses"/> and an HSM's reserved events react exactly as a soldier's do to its own edges — they all key on
    /// <see cref="SensorChangedEvent.Unit"/>. 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md</c> §4.4.
    /// <list type="bullet">
    /// <item><see cref="SensorChange.SquadContact"/> / <see cref="SensorChange.SquadAllClear"/> — the pool's count crossed zero on a
    ///   merge (<see cref="PoolEdges"/>, called by <see cref="SquadCoordinationSystem"/> right after it merges).</item>
    /// <item><see cref="SensorChange.SquadUnderFire"/> — a member's (or the commander's own) <c>Hit</c> / <c>NearMiss</c> edge of
    ///   last frame (<see cref="UnderFire"/>), one per edge, the same grain as the soldier's own.</item>
    /// </list>
    /// <para>⭐ Published straight on the bus (the driver already requires the repository): visible next frame, like every edge.</para>
    /// </summary>
    public static class SquadSensing
    {
        /// <summary>Publish <see cref="SensorChange.SquadContact"/> / <see cref="SensorChange.SquadAllClear"/> when the merge just
        /// took <paramref name="commander"/>'s pool across zero (<paramref name="countBefore"/> → its count now).</summary>
        public static void PoolEdges(EntityRepository repo, Entity commander, int countBefore)
        {
            int now = repo.GetComponentRO<SquadCognitiveState>(commander).Contacts.Count;
            if (countBefore == 0 && now > 0) Publish(repo, commander, SensorChange.SquadContact);
            else if (countBefore > 0 && now == 0) Publish(repo, commander, SensorChange.SquadAllClear);
        }

        /// <summary>
        /// Publish <see cref="SensorChange.SquadUnderFire"/> on the commander of every MEMBER that had a <c>Hit</c> or <c>NearMiss</c>
        /// edge last frame (its <see cref="UnitSubordinate.Commander"/>). <paramref name="gateOnAuthority"/>: only commanders whose <see cref="BehaviorState"/> this node owns —
        /// the driver's own gate.
        /// </summary>
        public static void UnderFire(EntityRepository repo, bool gateOnAuthority)
        {
            foreach (var evt in repo.Bus.Read<SensorChangedEvent>())
            {
                if (evt.What != SensorChange.Hit && evt.What != SensorChange.NearMiss) continue;
                if (!repo.IsAlive(evt.Unit)) continue;
                // ⭐ CE-2125 (R-272) — only MEMBERS: the commander is body-less now and the leader soldier is a member.
                //   ⛔ SUPERSEDED: "or the commander itself (a soldier-leader is in the fight)" (CE-2123 S2).
                var commander = repo.HasComponent<UnitSubordinate>(evt.Unit) ? repo.GetComponentRO<UnitSubordinate>(evt.Unit).Commander
                              : Entity.Null;
                if (commander.IsNull || !repo.IsAlive(commander) || !IsCommander(repo, commander)) continue;
                if (gateOnAuthority && (!repo.HasComponent<BehaviorState>(commander) || !repo.HasAuthority<BehaviorState>(commander))) continue;
                Publish(repo, commander, SensorChange.SquadUnderFire);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2125</c> (§7.2 L1–L3) — the leader soldier fell ⇒ the next man takes over: the first live <c>TeamLeader</c>, else
        /// the first live member, in roster order, becomes <c>SquadLeader</c> (CGF-local — only CGF reads it); the commander goes
        /// SILENT for its unit type's <see cref="UnitEntity.CommandDisruptionSeconds"/> (6 s without a type); and
        /// <see cref="SensorChange.SquadLeaderDown"/> is published on it. A squad that never had a designated leader has none to lose.
        /// </summary>
        public static void Succession(EntityRepository repo, Entity commander, double now)
        {
            if (!repo.IsComponentTypeRegistered<SquadLeadership>()) return;
            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            if (!repo.HasComponent<SquadLeadership>(commander))
            {
                int first = Designated(repo, in roster, TacticalDesignation.SquadLeader);
                repo.AddComponent(commander, new SquadLeadership { Leader = first < 0 ? Entity.Null : roster.SubordinateEntities[first] });
                return;
            }
            ref var leadership = ref repo.GetComponentRW<SquadLeadership>(commander);
            if (leadership.Leader.IsNull)
            {
                int first = Designated(repo, in roster, TacticalDesignation.SquadLeader);
                if (first >= 0) leadership.Leader = roster.SubordinateEntities[first];
                return;
            }
            if (Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, leadership.Leader)) return;

            int next = Designated(repo, in roster, TacticalDesignation.TeamLeader);
            if (next < 0) next = FirstLive(repo, in roster);
            if (next < 0) { leadership.Leader = Entity.Null; return; }   // nobody left to lead
            var successor = roster.SubordinateEntities[next];
            roster.TacticalDesignations[next] = (ushort)TacticalDesignation.SquadLeader;
            if (repo.HasComponent<UnitSubordinate>(successor))
                repo.GetComponentRW<UnitSubordinate>(successor).Designation = TacticalDesignation.SquadLeader;
            leadership.Leader = successor;
            float lull = repo.HasComponent<UnitEntity>(commander) ? repo.GetComponentRO<UnitEntity>(commander).CommandDisruptionSeconds : 6f;
            leadership.SilentUntil = now + (lull > 0f ? lull : 6f);
            Publish(repo, commander, SensorChange.SquadLeaderDown);
        }

        /// <summary>The first LIVE member carrying <paramref name="designation"/>, in roster order; −1 when none.</summary>
        private static int Designated(EntityRepository repo, in UnitRoster roster, TacticalDesignation designation)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                var d = repo.HasComponent<UnitSubordinate>(m) ? repo.GetComponentRO<UnitSubordinate>(m).Designation : (TacticalDesignation)roster.TacticalDesignations[i];
                if (d == designation && Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, m)) return i;
            }
            return -1;
        }

        private static int FirstLive(EntityRepository repo, in UnitRoster roster)
        {
            for (int i = 0; i < roster.Count; i++)
                if (Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, roster.SubordinateEntities[i])) return i;
            return -1;
        }

        private static bool IsCommander(EntityRepository repo, Entity e)
            => repo.HasComponent<SquadCognitiveState>(e) && repo.HasComponent<UnitRoster>(e);

        private static void Publish(EntityRepository repo, Entity commander, SensorChange what)
            => repo.Bus.Publish(new SensorChangedEvent { Unit = commander, What = what });
    }
}
