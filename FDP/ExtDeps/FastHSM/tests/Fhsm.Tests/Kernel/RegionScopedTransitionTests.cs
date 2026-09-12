using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// A transition selected in one orthogonal region must move THAT region's active leaf.
    ///
    /// Before this batch <c>ExecuteTransition</c> ended with an unconditional
    /// <c>activeLeafIds[0] = finalLeafId</c>: it received <c>regionCount</c> but no region index, so a
    /// transition fired in region 1 overwrote region 0's leaf and left region 1 where it was. Two
    /// regions were corrupted by one event — the target region never moved, and a bystander region was
    /// silently teleported into a state its own machine never entered.
    ///
    /// Harmless at regionCount == 1, which is why nothing caught it: every shipped asset with a real
    /// transition has one region.
    ///
    /// <c>SelectTransition</c> already iterates regions to find the winner, so it knows which one; the
    /// index was simply dropped on the way out. It is returned now rather than re-derived at the call
    /// site — re-deriving would be a second home for "which region".
    /// </summary>
    public unsafe class RegionScopedTransitionTests
    {
        private HsmDefinitionBlob Compile(HsmBuilder builder)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            HsmGraphValidator.Validate(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            // Emit() leaves Metadata unset; the name→index map is the sidecar, built separately.
            blob.Metadata = HsmEmitter.BuildMachineMetadata(graph);
            return blob;
        }

        /// <summary>
        /// Two regions, each with two leaves. Only region 1's leaf has a transition on event 1, so the
        /// selected transition can only have come from region 1.
        /// </summary>
        private HsmDefinitionBlob BuildTwoRegionMachine(
            out string r0Initial, out string r1Initial, out string r1Target)
        {
            var builder  = new HsmBuilder("RegionScoped");
            var parallel = builder.State("Parallel");
            parallel.State.IsParallel = true;

            StateBuilder region0 = null, region1 = null;
            parallel.Child("Region0", c => { c.Initial(); region0 = c; });
            parallel.Child("Region1", c => { c.Initial(); region1 = c; });

            StateBuilder r0A = null;
            region0.Child("R0_A", c => { c.Initial(); r0A = c; });
            region0.Child("R0_B", c => { });

            StateBuilder r1A = null, r1B = null;
            region1.Child("R1_A", c => { c.Initial(); r1A = c; });
            region1.Child("R1_B", c => { r1B = c; });

            // The ONLY transition in the machine, and it lives in region 1.
            r1A.On(1).GoTo(r1B.State.Name);

            r0Initial = r0A.State.Name;
            r1Initial = r1A.State.Name;
            r1Target  = r1B.State.Name;
            return Compile(builder);
        }

        private static ushort IndexOf(HsmDefinitionBlob blob, string stateName)
        {
            foreach (var kv in blob.Metadata.StateNames)
                if (kv.Value == stateName) return kv.Key;
            throw new InvalidOperationException($"state '{stateName}' not in the blob");
        }

        /// <summary>
        /// RED before the fix: region 1's transition wrote region 0's slot, so region 0 jumped to
        /// R1_B while region 1 stayed on R1_A.
        ///
        /// Both halves are asserted. Checking only that region 1 moved would pass a fix that wrote
        /// BOTH slots, and checking only that region 0 stayed would pass one that wrote neither.
        /// </summary>
        [Fact]
        public void ATransitionInOneRegion_MovesThatRegion_AndLeavesTheOthersAlone()
        {
            var blob = BuildTwoRegionMachine(out var r0Initial, out var r1Initial, out var r1Target);

            var instance = new HsmInstance128();
            HsmInstanceManager.Initialize(&instance, blob);
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);

            // Measured layout: slot 0 is the ROOT region (holding the Parallel composite) and the two
            // orthogonal regions occupy slots 1 and 2. The owning slot is discovered rather than
            // hard-coded, so the rail survives a change in how slots are allocated.
            ushort r1InitialId = IndexOf(blob, r1Initial);
            int movingSlot = SlotHolding(ref instance, r1InitialId);
            Assert.True(movingSlot >= 0, "region hosting the transition's source is not active");

            ushort[] before = { instance.ActiveLeafIds[0], instance.ActiveLeafIds[1], instance.ActiveLeafIds[2] };
            Assert.Equal(IndexOf(blob, r0Initial), before[SlotHolding(ref instance, IndexOf(blob, r0Initial))]);

            HsmEventQueue.TryEnqueue(&instance, 128, new HsmEvent { EventId = 1 });
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);

            // The mover: the region whose state had the transition.
            Assert.Equal(IndexOf(blob, r1Target), instance.ActiveLeafIds[movingSlot]);

            // The bystanders: every OTHER slot is untouched. Before the fix, slot 0 — the root region,
            // which no event addressed — was overwritten with the target leaf while the real region
            // stayed put, so both halves of this assertion failed at once.
            for (int slot = 0; slot < 3; slot++)
            {
                if (slot == movingSlot) continue;
                Assert.Equal(before[slot], instance.ActiveLeafIds[slot]);
            }
        }

        private static int SlotHolding(ref HsmInstance128 instance, ushort stateId)
        {
            for (int slot = 0; slot < 3; slot++)
                if (instance.ActiveLeafIds[slot] == stateId) return slot;
            return -1;
        }

        /// <summary>
        /// The single-region case is byte-identical: region 0 is both the selecting and the written
        /// region, so the fix is a no-op for every shipped asset.
        /// </summary>
        [Fact]
        public void ASingleRegionMachine_IsUnaffected()
        {
            var builder = new HsmBuilder("SingleRegion");
            var a = builder.State("A");
            a.Initial();
            var b = builder.State("B");
            a.On(1).GoTo(b.State.Name);

            var blob = Compile(builder);

            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);
            Assert.Equal(IndexOf(blob, "A"), instance.ActiveLeafIds[0]);

            HsmEventQueue.TryEnqueue(&instance, 64, new HsmEvent { EventId = 1 });
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref instance, 0, 0.016f);

            Assert.Equal(IndexOf(blob, "B"), instance.ActiveLeafIds[0]);
        }
    }
}
