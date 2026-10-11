using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ⭐ <c>CE-3158</c> G4 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — the target the squad's threat matrix assigned to a member: the
    /// member's commander (<see cref="UnitSubordinate.Commander"/>) holds <see cref="SquadCognitiveState.Assignment"/>, indexed by the
    /// member's place in the commander's <see cref="UnitRoster"/>. ONE reader — the utility input <c>IsAssignedTarget</c> (a bias
    /// in the threat ranking) and the fire-from-cover node's locked target both ask here.
    /// </summary>
    public static class SquadAssignment
    {
        /// <summary>The packed handle assigned to <paramref name="self"/>; false (and 0) when it is no squad member, the squad has no
        /// assignment state, or nothing is assigned yet.</summary>
        public static bool TargetOf(EntityRepository repo, Entity self, out long handle)
        {
            handle = 0L;
            if (!repo.IsComponentTypeRegistered<UnitSubordinate>() || !repo.IsComponentTypeRegistered<SquadCognitiveState>()
                || !repo.IsComponentTypeRegistered<UnitRoster>()) return false;
            if (!repo.HasComponent<UnitSubordinate>(self)) return false;
            var commander = repo.GetComponentRO<UnitSubordinate>(self).Commander;
            if (!repo.IsAlive(commander) || !repo.HasComponent<SquadCognitiveState>(commander) || !repo.HasComponent<UnitRoster>(commander))
                return false;
            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            int idx = UnitRoster.IndexOf(ref roster, self);
            if (idx < 0) return false;
            handle = repo.GetComponentRW<SquadCognitiveState>(commander).Assignment.GetAssignedTarget(idx);
            return handle != 0L;
        }
    }
}
