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
    /// <para>⭐ <c>CE-3170</c> — an attack order's <see cref="Fdp.Toolkit.Behavior.Components.DesignatedTarget"/> answers first.</para>
    /// </summary>
    public static class SquadAssignment
    {
        /// <summary>The packed handle assigned to <paramref name="self"/>; false (and 0) when it is no squad member, the squad has no
        /// assignment state, or nothing is assigned yet.</summary>
        public static bool TargetOf(EntityRepository repo, Entity self, out long handle)
        {
            handle = 0L;
            // ⭐ CE-3170 (docs/DESIGN_Entity_Control.md C-5) — an ATTACK order's target comes first: the operator above the squad
            //   named it. Only while it is still in the fight; a downed one leaves the squad's distribution in charge again.
            if (Fdp.Toolkit.Behavior.Components.DesignatedTarget.Of(repo, self, out var designated)
                && Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, designated))
            {
                handle = (long)designated.PackedValue;
                return true;
            }
            if (!repo.IsComponentTypeRegistered<UnitSubordinate>() || !repo.IsComponentTypeRegistered<SquadCognitiveState>()
                || !repo.IsComponentTypeRegistered<UnitRoster>()) return false;
            if (!repo.HasComponent<UnitSubordinate>(self)) return false;
            var commander = repo.GetComponentRO<UnitSubordinate>(self).Commander;
            if (!repo.IsAlive(commander) || !repo.HasComponent<SquadCognitiveState>(commander) || !repo.HasComponent<UnitRoster>(commander))
                return false;
            // ⭐ The parallel experiment (M-1 + the brain write inventory) — a READ, called every tick by every soldier's threat ranking and
            //   PeekAndFire: it took the COMMANDER's roster and squad state writable, stamping both chunks dirty every frame (UnitRoster 95 %,
            //   SquadCognitiveState 75 % unchanged) and racing the squad brain's real writes. ⭐ Read-only refs; the `ref` alias exists only
            //   because IndexOf / GetSlot take `ref` (the InlineArray defensive-copy rule) — nothing is written through it.
            ref var roster = ref System.Runtime.CompilerServices.Unsafe.AsRef(in repo.GetComponentRO<UnitRoster>(commander));
            int idx = UnitRoster.IndexOf(ref roster, self);
            if (idx < 0) return false;
            handle = System.Runtime.CompilerServices.Unsafe.AsRef(in repo.GetComponentRO<SquadCognitiveState>(commander)).Assignment.GetAssignedTarget(idx);
            return handle != 0L;
        }
    }
}
