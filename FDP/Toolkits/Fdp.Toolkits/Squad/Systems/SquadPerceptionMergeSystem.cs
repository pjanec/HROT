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
        /// <returns>⭐ <c>CE-3088</c> — true when the pool was re-merged this call (what the fire assignment keys on).</returns>
        public static bool Run(
            EntityRepository repo,
            Entity commander,
            uint currentTick,
            uint mergeIntervalTicks = 6)
        {
            // 1. Guard: both UnitRoster and Blackboard1024 must be present.
            if (!repo.HasComponent<UnitRoster>(commander)) return false;
            if (!repo.HasComponent<SquadCognitiveState>(commander)) return false;

            // 2. Project state.
            ref var state = ref repo.GetComponentRW<SquadCognitiveState>(commander);

            // 3. Compute XOR epoch checksum across all subordinates.
            ulong checksum = 0;
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            for (int m = 0; m < roster.Count; m++)
            {
                var member = roster.SubordinateEntities[m];
                if (!repo.HasComponent<TargetMemory>(member)) continue;
                ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(member);
                checksum ^= mem.ChangeEpoch;
            }

            // 4. Decide whether to merge.
            // LastMergeTick == 0 means the pool has never been populated; always run once.
            bool epochChanged = checksum != state.Contacts._memberEpochChecksum;
            bool dwellElapsed = state.Contacts.LastMergeTick == 0 ||
                                currentTick - state.Contacts.LastMergeTick >= mergeIntervalTicks;
            if (!epochChanged && !dwellElapsed) return false;

            // 5. Build new contact pool on the stack.
            SquadContactPool localPool = default;
            // ⭐ CE-3063 ② — a heard contact's radius and class, per pool slot, for the merge only (SquadContact keeps its 32 bytes).
            float* heardRadius = stackalloc float[16];
            byte*  heardClass  = stackalloc byte[16];

            for (int m = 0; m < roster.Count; m++)
            {
                var member = roster.SubordinateEntities[m];
                if (!repo.HasComponent<TargetMemory>(member)) continue;
                ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(member);
                ushort sourceBit = (ushort)(1 << m);

                for (int k = 0; k < mem.Count; k++)
                {
                    if (TargetMemory.IsAnonymous(in mem, k))
                    {
                        MergeHeard(ref localPool, heardRadius, heardClass, mem.EntityIds[k],
                                   mem.PositionsX[k], mem.PositionsY[k], mem.PositionsZ[k], mem.Radius[k], mem.SourceClass[k],
                                   mem.Freshness[k], mem.LastSeenTick[k], mem.Modalities[k], sourceBit);
                        continue;
                    }
                    MergeContact(
                        ref localPool,
                        mem.EntityIds[k],
                        mem.PositionsX[k],
                        mem.PositionsY[k],
                        mem.PositionsZ[k],
                        mem.Freshness[k],
                        mem.LastSeenTick[k],
                        mem.Modalities[k],
                        sourceBit);
                }
            }

            // ⭐ CE-2107 — a member's HEARD contact that another member SEES is one contact, as it is in each member's own memory
            //   (TargetMemory.AbsorbBySighting): fold it into the sighting, or the squad counts one enemy twice (SquadEnemyStrengthRatio).
            AbsorbHeardBySightings(repo, ref localPool, heardRadius, heardClass);

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
            return true;   // ⭐ CE-3088 — the driver re-assigns fire only when the pool was re-merged
        }

        /// <summary>
        /// ⭐ <c>CE-3063</c> ② — pool a member's HEARD contact by POSITION: one already pooled whose circle meets it (and whose
        /// sound class is compatible) is the same contact, fused as <see cref="TargetMemory.HearContact"/> fuses (inverse-variance
        /// mean, the radius shrinks); else a new pooled contact flagged <see cref="SquadContact.AnonymousFlag"/>. Two members
        /// hearing one shot are ONE squad contact. 📄 <c>docs/DESIGN_Thermal_And_Acoustic_Sensing.md</c> §8.
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-2107</c> — the squad twin of <see cref="TargetMemory.AbsorbBySighting"/>: a pooled heard contact whose circle
        /// holds an identified pooled contact that can sound like it (<c>ThreatEvaluationSystem.ClassesOf</c>, any match; an
        /// unknown class on either side matches) is folded into that contact — the higher score, both members' bits, the heard
        /// modalities — and leaves the pool. The sighting keeps its position. The nearest sighting wins.
        /// 📄 docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.
        /// </summary>
        private static void AbsorbHeardBySightings(EntityRepository repo, ref SquadContactPool pool, float* radii, byte* classes)
        {
            var span = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref pool.Contacts), 16);
            System.Span<byte> soundClasses = stackalloc byte[3];
            bool emitters = repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>();   // none ⇒ class unknown
            for (int i = pool.Count - 1; i >= 0; i--)   // downward: the slot swapped into i was already visited
            {
                if ((span[i].Flags & SquadContact.AnonymousFlag) == 0) continue;
                float r = radii[i];
                int best = -1; float bestD2 = float.MaxValue;
                for (int j = 0; j < pool.Count; j++)
                {
                    if ((span[j].Flags & SquadContact.AnonymousFlag) != 0 || span[j].EntityId <= 0) continue;
                    float dx = span[j].PositionX - span[i].PositionX, dy = span[j].PositionY - span[i].PositionY, d2 = dx * dx + dy * dy;
                    if (d2 > r * r || d2 >= bestD2) continue;
                    var canSound = emitters
                        ? Fdp.Toolkit.Perception.Systems.ThreatEvaluationSystem.ClassesOf(repo, new Entity((ulong)span[j].EntityId), soundClasses)
                        : System.ReadOnlySpan<byte>.Empty;
                    bool compatible = classes[i] == 0 || canSound.Length == 0;
                    for (int k = 0; !compatible && k < canSound.Length; k++) compatible = TargetMemory.ClassesCompatible(classes[i], canSound[k]);
                    if (!compatible) continue;
                    best = j; bestD2 = d2;
                }
                if (best < 0) continue;

                ref var seen = ref span[best];
                if (span[i].ThreatScore > seen.ThreatScore) seen.ThreatScore = span[i].ThreatScore;
                seen.SourceMembersMask = (ushort)(seen.SourceMembersMask | span[i].SourceMembersMask);
                seen.Flags             = (ushort)(seen.Flags | (span[i].Flags & ~SquadContact.AnonymousFlag));

                int last = --pool.Count;
                span[i] = span[last]; radii[i] = radii[last]; classes[i] = classes[last];
                span[last] = default;
            }
        }

        private static void MergeHeard(
            ref SquadContactPool pool, float* radii, byte* classes,
            long id, float x, float y, float z, float radius, byte sourceClass,
            float threatScore, uint lastSeenTick, byte modalities, ushort sourceMemberBit)
        {
            var span = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref pool.Contacts), 16);
            radius = radius > 0f ? radius : 1f;

            int best = -1; float bestD2 = float.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                if ((span[i].Flags & SquadContact.AnonymousFlag) == 0 || !TargetMemory.ClassesCompatible(classes[i], sourceClass)) continue;
                float dx = span[i].PositionX - x, dy = span[i].PositionY - y, d2 = dx * dx + dy * dy;
                float reach = radii[i] + radius;
                if (d2 <= reach * reach && d2 < bestD2) { best = i; bestD2 = d2; }
            }
            if (best >= 0)
            {
                ref var c = ref span[best];
                float w1 = 1f / (radii[best] * radii[best]), w2 = 1f / (radius * radius), w = w1 + w2;
                c.PositionX = (c.PositionX * w1 + x * w2) / w;
                c.PositionY = (c.PositionY * w1 + y * w2) / w;
                c.PositionZ = (c.PositionZ * w1 + z * w2) / w;
                radii[best] = 1f / System.MathF.Sqrt(w);
                if (classes[best] == 0) classes[best] = sourceClass;
                if (threatScore > c.ThreatScore) c.ThreatScore = threatScore;
                if (lastSeenTick > c.LastSeenTick) c.LastSeenTick = lastSeenTick;
                c.SourceMembersMask = (ushort)(c.SourceMembersMask | sourceMemberBit);
                c.Flags             = (ushort)(c.Flags | modalities);
                return;
            }

            int slot;
            if (pool.Count < 16) slot = pool.Count++;
            else
            {
                slot = 0;
                for (int i = 1; i < 16; i++) if (span[i].ThreatScore < span[slot].ThreatScore) slot = i;
                if (threatScore <= span[slot].ThreatScore) return;   // loses the eviction race
            }
            span[slot] = new SquadContact
            {
                EntityId          = id,
                PositionX         = x,
                PositionY         = y,
                PositionZ         = z,
                ThreatScore       = threatScore,
                LastSeenTick      = lastSeenTick,
                Flags             = (ushort)(modalities | SquadContact.AnonymousFlag),
                SourceMembersMask = sourceMemberBit,
            };
            radii[slot] = radius;
            classes[slot] = sourceClass;
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

                // Found: update max threat score; update position/lastSeenTick independently.
                if (threatScore > span[i].ThreatScore)
                {
                    span[i].ThreatScore = threatScore;
                }
                span[i].SourceMembersMask = (ushort)(span[i].SourceMembersMask | sourceMemberBit);
                span[i].Flags             = (ushort)(span[i].Flags | modalities);
                // Position is pinned to the most-recent sighting, independent of threat score.
                if (lastSeenTick > span[i].LastSeenTick)
                {
                    span[i].LastSeenTick = lastSeenTick;
                    span[i].PositionX   = posX;
                    span[i].PositionY   = posY;
                    span[i].PositionZ   = posZ;
                }
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
