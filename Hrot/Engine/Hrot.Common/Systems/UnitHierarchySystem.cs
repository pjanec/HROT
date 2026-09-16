using CarKinem.Formation;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Utilities;

namespace Hrot.Common.Systems
{
    /// <summary>
    /// Maintains the ECS commander-subordinate hierarchy by processing
    /// <see cref="CmdAssignSubordinate"/>, <see cref="CmdRemoveSubordinate"/>, and
    /// <see cref="DestructionOrder"/> events in a single simulation-phase tick.
    ///
    /// <para>Processing order per tick: destruction cascade → removal → assignment.
    /// This order ensures stale references are cleaned up before any new assignments
    /// are processed.</para>
    ///
    /// <para>Network dirty marking is performed after every successful assignment or removal
    /// so that <c>EntityInfoEgressTranslator</c> broadcasts updated subordination state
    /// to remote nodes.</para>
    /// </summary>
    // ⭐⭐⭐ CE-165 — SINGLETON BY DESIGN, and this is the system the guard was measured on.
    // It is carried by BOTH CgfLogicPack (Brain) and SimHostCoreLogicPack (MuscleGround), so any node
    // running both roles registers it twice unless the root deduplicates. ProcessAssignSubordinates reads
    // CmdAssignSubordinate NON-DESTRUCTIVELY, so a second instance sees the same events in the same frame;
    // for a subordinate already assigned to the SAME commander the guard below takes no branch and does not
    // `continue`, so execution falls through to an unguarded roster append. The subordinate is added twice,
    // UnitRoster.Count is inflated, and at Capacity the system starts publishing
    // CmdAssignSubordinateRejected for LEGITIMATE assignments. Corrupted state, not a wasted tick.
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Simulation)]
    public class UnitHierarchySystem : IEcsModuleSystem
    {
        // Literal ordinal for EntityInfo descriptor; avoids referencing Hrot.Network.NED
        // from Hrot.Common (which would create a circular dependency).
        private const long EntityInfoDescriptorOrdinal = 1L;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            ProcessDestructionOrders(repo);
            ProcessRemoveSubordinates(repo);
            ProcessAssignSubordinates(repo);
        }

        // ── Destruction cascade ───────────────────────────────────────────────

        private static void ProcessDestructionOrders(EntityRepository repo)
        {
            var events = repo.Bus.Read<DestructionOrder>();
            foreach (var evt in events)
            {
                var entity = evt.Entity;
                if (!repo.IsAlive(entity)) continue;

                // If the destroyed entity was a commander, release all its subordinates
                if (repo.HasComponent<UnitRoster>(entity))
                {
                    var roster = repo.GetComponent<UnitRoster>(entity);
                    unsafe
                    {
                        for (int i = 0; i < roster.Count; i++)
                        {
                            var sub = new Entity((ulong)roster.SubordinateEntities[i]);
                            if (!repo.IsAlive(sub)) continue;
                            if (!repo.HasComponent<UnitSubordinate>(sub)) continue;
                            repo.RemoveComponent<UnitSubordinate>(sub);
                            if (repo.HasComponent<FormationFollower>(sub))
                                repo.RemoveComponent<FormationFollower>(sub);
                            SmartEgressUtil.MarkDirty(repo, sub, EntityInfoDescriptorOrdinal);
                        }
                    }
                }

                // If the destroyed entity was a subordinate, remove it from its commander's roster
                if (repo.HasComponent<UnitSubordinate>(entity))
                {
                    RemoveFromRoster(repo, entity);
                    repo.RemoveComponent<UnitSubordinate>(entity);
                    if (repo.HasComponent<FormationFollower>(entity))
                        repo.RemoveComponent<FormationFollower>(entity);
                }
            }
        }

        // ── Remove subordinate ────────────────────────────────────────────────

        private static void ProcessRemoveSubordinates(EntityRepository repo)
        {
            var events = repo.Bus.Read<CmdRemoveSubordinate>();
            foreach (var evt in events)
            {
                var sub = evt.Subordinate;
                if (!repo.IsAlive(sub)) continue;
                RemoveFromHierarchy(repo, sub);
                SmartEgressUtil.MarkDirty(repo, sub, EntityInfoDescriptorOrdinal);
            }
        }

        // ── Assign subordinate ────────────────────────────────────────────────

        private static void ProcessAssignSubordinates(EntityRepository repo)
        {
            var events = repo.Bus.Read<CmdAssignSubordinate>();
            foreach (var evt in events)
            {
                var sub = evt.Subordinate;
                var cmd = evt.Commander;

                if (!repo.IsAlive(sub) || !repo.IsAlive(cmd)) continue;

                // If subordinate already assigned to a different commander, remove first
                if (repo.HasComponent<UnitSubordinate>(sub))
                {
                    var current = repo.GetComponent<UnitSubordinate>(sub);
                    if (!current.Commander.Equals(cmd))
                        RemoveFromHierarchy(repo, sub);
                }

                // Load or create commander's roster
                UnitRoster roster = repo.HasComponent<UnitRoster>(cmd)
                    ? repo.GetComponent<UnitRoster>(cmd)
                    : new UnitRoster();

                // Capacity check — reject and signal the originating BTree node
                if (roster.Count >= UnitRoster.Capacity)
                {
                    repo.Bus.Publish(new CmdAssignSubordinateRejected { Subordinate = sub });
                    continue;
                }

                // ── Atomic writes ─────────────────────────────────────────────
                // a. UnitSubordinate on the subordinate entity
                var subComp = new UnitSubordinate { Commander = cmd, Designation = evt.Designation };
                if (repo.HasComponent<UnitSubordinate>(sub))
                    repo.SetComponent(sub, subComp);
                else
                    repo.AddComponent(sub, subComp);

                // b. Add entry to roster
                unsafe
                {
                    roster.SubordinateEntities[roster.Count]  = (long)sub.PackedValue;
                    roster.TacticalDesignations[roster.Count] = (ushort)evt.Designation;
                    roster.Count++;
                }
                if (repo.HasComponent<UnitRoster>(cmd))
                    repo.SetComponent(cmd, roster);
                else
                    repo.AddComponent(cmd, roster);

                // c. FormationFollower when HasFormationSlot is set
                if (evt.HasFormationSlot == 1)
                {
                    var ff = new FormationFollower
                    {
                        SlotIndex     = evt.SlotIndex,
                        State         = FormationMemberState.Rejoining,
                        IsInFormation = 1,
                    };
                    if (repo.HasComponent<FormationFollower>(sub))
                        repo.SetComponent(sub, ff);
                    else
                        repo.AddComponent(sub, ff);
                }

                SmartEgressUtil.MarkDirty(repo, sub, EntityInfoDescriptorOrdinal);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Removes <paramref name="subordinate"/> from its current commander's
        /// <see cref="UnitRoster"/> and strips <see cref="UnitSubordinate"/> (and
        /// <see cref="FormationFollower"/> when present) from the entity.
        /// Does nothing if the entity has no <see cref="UnitSubordinate"/> component.
        /// </summary>
        private static void RemoveFromHierarchy(EntityRepository repo, Entity subordinate)
        {
            if (!repo.HasComponent<UnitSubordinate>(subordinate)) return;

            RemoveFromRoster(repo, subordinate);

            repo.RemoveComponent<UnitSubordinate>(subordinate);
            if (repo.HasComponent<FormationFollower>(subordinate))
                repo.RemoveComponent<FormationFollower>(subordinate);
        }

        /// <summary>
        /// Removes <paramref name="subordinate"/> from the commander's
        /// <see cref="UnitRoster"/> using an order-preserving left-shift.
        /// The last slot is zeroed after compaction.
        /// </summary>
        private static void RemoveFromRoster(EntityRepository repo, Entity subordinate)
        {
            if (!repo.HasComponent<UnitSubordinate>(subordinate)) return;

            var subComp   = repo.GetComponent<UnitSubordinate>(subordinate);
            var commander = subComp.Commander;

            if (!repo.IsAlive(commander) || !repo.HasComponent<UnitRoster>(commander))
                return;

            var roster   = repo.GetComponent<UnitRoster>(commander);
            int foundIdx = -1;
            unsafe
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    if (roster.SubordinateEntities[i] == (long)subordinate.PackedValue)
                    {
                        foundIdx = i;
                        break;
                    }
                }

                if (foundIdx < 0) return;

                // Order-preserving left-shift using pointer arithmetic
                for (int i = foundIdx; i < roster.Count - 1; i++)
                {
                    roster.SubordinateEntities[i]  = roster.SubordinateEntities[i + 1];
                    roster.TacticalDesignations[i] = roster.TacticalDesignations[i + 1];
                }
                // Zero the vacated last slot
                roster.SubordinateEntities[roster.Count - 1]  = 0;
                roster.TacticalDesignations[roster.Count - 1] = 0;
                roster.Count--;
            }
            repo.SetComponent(commander, roster);
        }
    }
}
