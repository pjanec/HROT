using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated verbs over <see cref="MemberSlotList"/> for the Hill-attack wave core (architect Q#8-A).
    /// Every mutator takes the list BY VALUE and returns the mutated copy so the graph writes it back via
    /// the shipped GetVariable→FunctionCall→SetVariable pattern
    /// (<c>ws.Tracker = MemberSlotListOps.Add(ws.Tracker, …)</c>) — no raw-array nodes, no by-ref/new
    /// compiler capability. Readers are pure. All accessors clamp the index into [0, Count) defensively;
    /// the wave graphs only ever call them with a valid <c>FlowForEach.CurrentIndex</c> / live index.
    /// Does not modify the C# oracle.
    /// </summary>
    public static unsafe class MemberSlotListOps
    {
        private static int Clamp(int i, int count)
        {
            if (i < 0) return 0;
            if (i >= count) return count > 0 ? count - 1 : 0;
            return i;
        }

        /// <summary>Live entry count (oracle <c>ActiveAttackerCount</c>).</summary>
        public static int Count(MemberSlotList list) => list.Count;

        /// <summary>
        /// Appends a runner (packed entity + firing slot + reserved baseline slot, run-started = 0) and
        /// returns the grown list. No-op (returns the input unchanged) when already at capacity 8 —
        /// matches the oracle only reaching here while <c>ActiveAttackerCount &lt; 8</c>.
        /// </summary>
        public static MemberSlotList Add(MemberSlotList list, long entityPacked, int firingSlot, int baselineSlot)
        {
            if (list.Count >= 8) return list;
            int i = list.Count;
            list.EntityPacked[i]     = entityPacked;
            list.SlotIndex[i]         = (byte)firingSlot;
            list.BaselineSlotIndex[i] = (byte)baselineSlot;
            list.Started[i]           = 0;
            list.Count                = i + 1;
            return list;
        }

        /// <summary>
        /// Swap-removes the entry at <paramref name="index"/> (moves the last live entry into its slot and
        /// decrements <see cref="MemberSlotList.Count"/>), returning the compacted list — the oracle's
        /// <c>SwapRemove</c>. Used by <c>IsWaveCompleted</c>'s reverse walk (removing at <c>i</c> while
        /// iterating downward never reprocesses the swapped-in entry).
        /// </summary>
        public static MemberSlotList SwapRemoveAt(MemberSlotList list, int index)
        {
            if (list.Count <= 0) return list;
            int i = Clamp(index, list.Count);
            int last = list.Count - 1;
            list.EntityPacked[i]      = list.EntityPacked[last];
            list.SlotIndex[i]         = list.SlotIndex[last];
            list.BaselineSlotIndex[i] = list.BaselineSlotIndex[last];
            list.Started[i]           = list.Started[last];
            list.Count                = last;
            return list;
        }

        /// <summary>Packed <c>Entity</c> handle of the <paramref name="index"/>-th live runner.</summary>
        public static long GetEntityPacked(MemberSlotList list, int index)
            => list.EntityPacked[Clamp(index, list.Count)];

        /// <summary>The <paramref name="index"/>-th runner as an <see cref="Entity"/> (unpacked).</summary>
        public static Entity GetEntity(MemberSlotList list, int index)
            => new Entity((ulong)list.EntityPacked[Clamp(index, list.Count)]);

        /// <summary>Assigned firing-slot index of the <paramref name="index"/>-th runner.</summary>
        public static int GetSlotIndex(MemberSlotList list, int index)
            => list.SlotIndex[Clamp(index, list.Count)];

        /// <summary>Reserved return-baseline slot index of the <paramref name="index"/>-th runner.</summary>
        public static int GetBaselineSlotIndex(MemberSlotList list, int index)
            => list.BaselineSlotIndex[Clamp(index, list.Count)];

        /// <summary>Run-started latch (0/1) of the <paramref name="index"/>-th runner.</summary>
        public static int GetStarted(MemberSlotList list, int index)
            => list.Started[Clamp(index, list.Count)];

        /// <summary>Sets the run-started latch of the <paramref name="index"/>-th runner, returning the
        /// mutated list (oracle <c>s.HasStartedRun[i] = 1</c>).</summary>
        public static MemberSlotList SetStarted(MemberSlotList list, int index, int started)
        {
            if (list.Count <= 0) return list;
            list.Started[Clamp(index, list.Count)] = (byte)(started != 0 ? 1 : 0);
            return list;
        }
    }
}
