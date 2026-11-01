using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    /// <summary>
    /// The CALLER of <c>PocRunActions</c> for hosts that have no generated binding: it keeps the loop's own
    /// <see cref="RunParams"/> + <see cref="RunState"/> in an occurrence slot (the WORST case for the "outer ref stays valid" trap —
    /// the outer and the inner step slots then live in the SAME store). A BTree leaf, the trigger driver and the direct rails all use it.
    /// </summary>
    public static unsafe class PocOuter
    {
        public const int OuterSlotKey = 0x7C0000FF;
        private const ulong OuterHash = 0x0C0FFEE;

        /// <summary>Ticks the loop. On a terminal status the outer slot is detached too (so the next run starts fresh) and the final state is handed back.</summary>
        public static NodeStatus Tick(EntityRepository world, Entity self, int listRef, out RunState final, int outerKey = OuterSlotKey)
        {
            ref RunState rs = ref OccurrenceWorkingState.ResolveOrAttach<RunParams, RunState>(
                world, self, outerKey, OuterHash, OccurrenceKind.Hsm, out bool fresh, out RunParams* pp);
            if (fresh) pp->ListRef = listRef;
            var st = PocActions.PocRunActions(ref *pp, ref rs, self, world);
            final = rs;
            if (st != NodeStatus.Running) OccurrenceStoreAccess.TryDetachSlot(world, self, outerKey);
            return st;
        }

        /// <summary>How many slots (of any kind) the unit's store holds across ALL its blocks.</summary>
        public static int SlotCount(EntityRepository world, Entity self)
        {
            OccurrenceStoreAccess.GetBlocksReadOnly(world, self, out var blocks);
            int n = 0;
            for (int b = 0; b < blocks.Count; b++) n += BlueprintBlackboardPartitions.GetSlotCount(blocks.Memory(b));
            return n;
        }

        public static int BlockCount(EntityRepository world, Entity self)
        {
            OccurrenceStoreAccess.GetBlocksReadOnly(world, self, out var blocks);
            return blocks.Count;
        }

        public static bool Holds(EntityRepository world, Entity self, int key)
            => OccurrenceStoreAccess.TryFindSlotReadOnly(world, self, key, out _, out _, out _);
    }
}
