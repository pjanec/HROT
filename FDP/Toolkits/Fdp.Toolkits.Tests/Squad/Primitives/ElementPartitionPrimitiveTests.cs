using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkit.Squad.Primitives.Tests
{
    /// <summary>
    /// P1-01: Tests for <see cref="ElementPartitionPrimitive"/>.
    /// Covers SC-P1-01-1 through SC-P1-01-4.
    /// </summary>
    public class ElementPartitionPrimitiveTests
    {
        [Fact]
        public void Partition_FavorsHighestScore_ElementAssigned()
        {
            // SC-P1-01-1
            SquadCognitiveState state = default;
            var inputs = new MemberPartitionInput[]
            {
                new MemberPartitionInput(0.9f, 0.2f, 0.1f),
                new MemberPartitionInput(0.1f, 0.8f, 0.2f),
                new MemberPartitionInput(0.2f, 0.1f, 0.7f),
            };

            ElementPartitionPrimitive.Partition(ref state, inputs, elementCount: 3, decisiveGap: 0.15f, out int count);

            // Member 0 should be assigned element 0 (score 0.9 is highest).
            var membersSpan = MemoryMarshal.CreateReadOnlySpan<byte>(
                ref System.Runtime.CompilerServices.Unsafe.As<MemberElementIndexArray, byte>(
                    ref state.Elements.MemberElements), 16);
            Assert.Equal(0, membersSpan[0]);
            Assert.True(count >= 1);
        }

        [Fact]
        public void Partition_SmallGap_HysteresisHolds()
        {
            // SC-P1-01-2
            SquadCognitiveState state = default;
            // First pass: put member 0 into element 0 decisively.
            var inputs1 = new MemberPartitionInput[]
            {
                new MemberPartitionInput(0.9f, 0.1f, 0.0f),
            };
            ElementPartitionPrimitive.Partition(ref state, inputs1, elementCount: 3, decisiveGap: 0.15f, out _);

            // Second pass: element 1 wins by only 0.05 — below decisiveGap=0.15.
            var inputs2 = new MemberPartitionInput[]
            {
                new MemberPartitionInput(0.5f, 0.55f, 0.0f),
            };
            ElementPartitionPrimitive.Partition(ref state, inputs2, elementCount: 3, decisiveGap: 0.15f, out int count);

            var membersSpan = MemoryMarshal.CreateReadOnlySpan<byte>(
                ref System.Runtime.CompilerServices.Unsafe.As<MemberElementIndexArray, byte>(
                    ref state.Elements.MemberElements), 16);
            Assert.Equal(0, membersSpan[0]);
            Assert.Equal(0, count);
        }

        [Fact]
        public void Partition_DecisiveGap_MemberMoves()
        {
            // SC-P1-01-3
            SquadCognitiveState state = default;
            // First pass: put member 0 into element 0 decisively.
            var inputs1 = new MemberPartitionInput[]
            {
                new MemberPartitionInput(0.9f, 0.1f, 0.0f),
            };
            ElementPartitionPrimitive.Partition(ref state, inputs1, elementCount: 3, decisiveGap: 0.15f, out _);

            // Second pass: element 1 wins by 0.30 — above decisiveGap=0.15.
            var inputs2 = new MemberPartitionInput[]
            {
                new MemberPartitionInput(0.3f, 0.6f, 0.0f),
            };
            ElementPartitionPrimitive.Partition(ref state, inputs2, elementCount: 3, decisiveGap: 0.15f, out int count);

            var membersSpan = MemoryMarshal.CreateReadOnlySpan<byte>(
                ref System.Runtime.CompilerServices.Unsafe.As<MemberElementIndexArray, byte>(
                    ref state.Elements.MemberElements), 16);
            Assert.Equal(1, membersSpan[0]);
            Assert.Equal(1, count);
        }

        [Fact]
        public void Partition_ZeroAllocs()
        {
            // SC-P1-01-4: Verify Partition allocates nothing after JIT warm-up.
            // GC.GetAllocatedBytesForCurrentThread() is monotonically increasing
            // and counts only THIS thread's managed allocations, so it is immune
            // to GC compaction, background threads, or finalizer noise.
            SquadCognitiveState state = default;
            Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[2];
            inputs[0] = new MemberPartitionInput(0.9f, 0.2f, 0.1f);
            inputs[1] = new MemberPartitionInput(0.1f, 0.8f, 0.2f);

            // Warm-up to ensure JIT compilation does not skew the measurement.
            ElementPartitionPrimitive.Partition(ref state, inputs, elementCount: 3, decisiveGap: 0.1f, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                ElementPartitionPrimitive.Partition(ref state, inputs, elementCount: 3, decisiveGap: 0.1f, out _);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            long diff = after - before;

            // Thorough GC cleanup after the hot loop so that subsequent tests
            // that measure via GC.GetTotalMemory() start with a clean baseline.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            Assert.Equal(0, diff);
        }
    }
}
