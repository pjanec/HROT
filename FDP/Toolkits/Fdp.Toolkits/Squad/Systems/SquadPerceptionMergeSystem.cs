using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Merges TargetMemory contacts from all squad subordinates into the commander's
    /// SquadCognitiveState.Contacts (SquadContactPool).
    /// <para>
    /// Runs on the Brain node. Call Run(...) once per world tick with the current tick
    /// and the desired merge interval. The method skips work when neither condition holds:
    ///   (a) currentTick - state.Contacts.LastMergeTick >= mergeIntervalTicks  (10 Hz cadence), OR
    ///   (b) XOR of all member TargetMemory.ChangeEpoch values changed since last merge
    ///       (event-driven forced re-merge on any structural perception change).
    /// </para>
    /// </summary>
    public static unsafe class SquadPerceptionMergeSystem
    {
        /// <summary>
        /// Runs the perception merge pass for the squad commanded by <paramref name="commander"/>.
        /// </summary>
        /// <param name="repo">The entity repository.</param>
        /// <param name="commander">The squad commander entity.</param>
        /// <param name="currentTick">The current simulation tick.</param>
        /// <param name="mergeIntervalTicks">Minimum tick interval between cadence merges (default 6 = ~10 Hz at 60 tps).</param>
        public static void Run(
            EntityRepository repo,
            Entity commander,
            uint currentTick,
            uint mergeIntervalTicks = 6)
        {
            // 1. Guard: both UnitRoster and Blackboard1024 must be present.
            if (!repo.HasComponent<UnitRoster>(commander)) return;
            if (!repo.HasComponent<Blackboard1024>(commander)) return;

            // 2. Project state.
            ref var state = ref SquadCognitiveState.Project(ref repo.GetComponentRW<Blackboard1024>(commander));

            // 3. Compute XOR epoch checksum across all subordinates.
            ulong checksum = 0;
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            for (int m = 0; m < roster.Count; m++)
            {
                var member = new Entity((ulong)roster.SubordinateEntities[m]);
                if (!repo.HasComponent<TargetMemory>(member)) continue;
                ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(member);
                checksum ^= mem.ChangeEpoch;
            }

            // 4. Decide whether to merge.
            // LastMergeTick == 0 means the pool has never been populated; always run once.
            bool epochChanged = checksum != state.Contacts._memberEpochChecksum;
            bool dwellElapsed = state.Contacts.LastMergeTick == 0 ||
                                currentTick - state.Contacts.LastMergeTick >= mergeIntervalTicks;
            if (!epochChanged && !dwellElapsed) return;

            // 5. Build new contact pool on the stack.
            SquadContactPool localPool = default;

            for (int m = 0; m < roster.Count; m++)
            {
                var member = new Entity((ulong)roster.SubordinateEntities[m]);
                if (!repo.HasComponent<TargetMemory>(member)) continue;
                ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(member);
                ushort sourceBit = (ushort)(1 << m);

                for (int k = 0; k < mem.Count; k++)
                {
                    MergeContact(
                        ref localPool,
                        mem.EntityIds[k],
                        mem.PositionsX[k],
                        mem.PositionsY[k],
                        mem.PositionsZ[k],
                        mem.ThreatScores[k],
                        mem.LastSeenTick[k],
                        mem.Modalities[k],
                        sourceBit);
                }
            }

            // 6. Sort pool contacts descending by ThreatScore (insertion sort; at most 16 entries).
            var sortSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref localPool.Contacts), 16);

            for (int i = 1; i < localPool.Count; i++)
            {
                SquadContact tmp = sortSpan[i];
                int j = i - 1;
                while (j >= 0 && sortSpan[j].ThreatScore < tmp.ThreatScore)
                {
                    sortSpan[j + 1] = sortSpan[j];
                    j--;
                }
                sortSpan[j + 1] = tmp;
            }

            // 7. Write back.
            state.Contacts.LastMergeTick        = currentTick;
            state.Contacts._memberEpochChecksum = checksum;
            state.Contacts.Count                = localPool.Count;

            var dst = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts), 16);
            var src = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref localPool.Contacts), 16);
            src.Slice(0, localPool.Count).CopyTo(dst);
        }

        private static void MergeContact(
            ref SquadContactPool pool,
            long entityId,
            float posX, float posY, float posZ,
            float threatScore,
            uint lastSeenTick,
            byte modalities,
            ushort sourceMemberBit)
        {
            var span = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref pool.Contacts), 16);

            // Scan for existing entry.
            for (int i = 0; i < pool.Count; i++)
            {
                if (span[i].EntityId != entityId) continue;

                // Found: update with max threat score and position, OR modalities and mask.
                if (threatScore > span[i].ThreatScore)
                {
                    span[i].ThreatScore = threatScore;
                    span[i].PositionX   = posX;
                    span[i].PositionY   = posY;
                    span[i].PositionZ   = posZ;
                }
                span[i].SourceMembersMask = (ushort)(span[i].SourceMembersMask | sourceMemberBit);
                span[i].Flags             = (ushort)(span[i].Flags | modalities);
                if (lastSeenTick > span[i].LastSeenTick)
                    span[i].LastSeenTick = lastSeenTick;
                return;
            }

            // Not found.
            if (pool.Count < 16)
            {
                // Append at Count.
                int slot = pool.Count++;
                span[slot] = new SquadContact
                {
                    EntityId          = entityId,
                    PositionX         = posX,
                    PositionY         = posY,
                    PositionZ         = posZ,
                    ThreatScore       = threatScore,
                    LastSeenTick      = lastSeenTick,
                    Flags             = modalities,
                    SourceMembersMask = sourceMemberBit,
                };
                return;
            }

            // Pool full: replace lowest-score entry if new score wins.
            int lowestIdx   = 0;
            float lowestScore = span[0].ThreatScore;
            for (int i = 1; i < 16; i++)
            {
                if (span[i].ThreatScore < lowestScore)
                {
                    lowestScore = span[i].ThreatScore;
                    lowestIdx   = i;
                }
            }

            if (threatScore > lowestScore)
            {
                span[lowestIdx] = new SquadContact
                {
                    EntityId          = entityId,
                    PositionX         = posX,
                    PositionY         = posY,
                    PositionZ         = posZ,
                    ThreatScore       = threatScore,
                    LastSeenTick      = lastSeenTick,
                    Flags             = modalities,
                    SourceMembersMask = sourceMemberBit,
                };
            }
            // Otherwise: new contact loses the eviction race — do nothing.
        }
    }
}
