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
        /// Publish <see cref="SensorChange.SquadUnderFire"/> on the commander of every unit that had a <c>Hit</c> or <c>NearMiss</c>
        /// edge last frame — a roster member (its <see cref="UnitSubordinate.Commander"/>) or a commander itself (a soldier-leader
        /// is in the fight). <paramref name="gateOnAuthority"/>: only commanders whose <see cref="BehaviorState"/> this node owns —
        /// the driver's own gate.
        /// </summary>
        public static void UnderFire(EntityRepository repo, bool gateOnAuthority)
        {
            foreach (var evt in repo.Bus.Read<SensorChangedEvent>())
            {
                if (evt.What != SensorChange.Hit && evt.What != SensorChange.NearMiss) continue;
                if (!repo.IsAlive(evt.Unit)) continue;
                var commander = IsCommander(repo, evt.Unit) ? evt.Unit
                              : repo.HasComponent<UnitSubordinate>(evt.Unit) ? repo.GetComponentRO<UnitSubordinate>(evt.Unit).Commander
                              : Entity.Null;
                if (commander.IsNull || !repo.IsAlive(commander) || !IsCommander(repo, commander)) continue;
                if (gateOnAuthority && (!repo.HasComponent<BehaviorState>(commander) || !repo.HasAuthority<BehaviorState>(commander))) continue;
                Publish(repo, commander, SensorChange.SquadUnderFire);
            }
        }

        private static bool IsCommander(EntityRepository repo, Entity e)
            => repo.HasComponent<SquadCognitiveState>(e) && repo.HasComponent<UnitRoster>(e);

        private static void Publish(EntityRepository repo, Entity commander, SensorChange what)
            => repo.Bus.Publish(new SensorChangedEvent { Unit = commander, What = what });
    }
}
