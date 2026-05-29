using System.Runtime.CompilerServices;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Blackboard1024.Project{T}"/> (P0.05).
    /// Verifies memory aliasing through the projection helper.
    /// </summary>
    public class Blackboard1024Tests
    {
        private struct TestState
        {
            public int A;
            public int B;
        }

        private struct TestState2
        {
            public float X;
            public float Y;
        }

        // SC-P0-05-1: Write through Project<T> → values visible in raw Memory bytes
        [Fact]
        public unsafe void Project_WriteThroughProjection_IsVisibleInRawMemory()
        {
            var bb = new Blackboard1024();
            ref var state = ref Blackboard1024.Project<TestState>(ref bb);
            state.A = 0x12345678;
            state.B = unchecked((int)0xDEADBEEFu);

            // Read raw bytes and reconstruct int at offset 0 (little-endian)
            int rawA = bb.Memory[0] | (bb.Memory[1] << 8) | (bb.Memory[2] << 16) | (bb.Memory[3] << 24);
            Assert.Equal(0x12345678, rawA);
        }

        // SC-P0-05-2: Write via first Project, re-read via second Project → same values (aliasing)
        [Fact]
        public unsafe void Project_SecondProjection_SeesFirstWrite()
        {
            var bb = new Blackboard1024();
            ref var write = ref Blackboard1024.Project<TestState>(ref bb);
            write.A = 42;
            write.B = 99;

            ref var reread = ref Blackboard1024.Project<TestState>(ref bb);
            Assert.Equal(42, reread.A);
            Assert.Equal(99, reread.B);
        }

        // SC-P0-05-3: Mutual aliasing with two different struct types at offset 0
        [Fact]
        public unsafe void Project_TwoDifferentStructTypes_AreAliased()
        {
            var bb = new Blackboard1024();

            // Write int A=1234 via TestState
            ref var stateA = ref Blackboard1024.Project<TestState>(ref bb);
            stateA.A = unchecked((int)0x3F800000u); // IEEE 754 bit pattern for 1.0f

            // Read back via TestState2 which overlaps at offset 0 with float X
            ref var stateB = ref Blackboard1024.Project<TestState2>(ref bb);
            // stateB.X occupies the same bytes as stateA.A
            Assert.Equal(1.0f, stateB.X);
        }

        // Additional: mutation through projection is visible on re-projection (aliasing proof)
        [Fact]
        public unsafe void Project_Mutation_IsVisible_OnReProjection()
        {
            var bb = new Blackboard1024();
            ref var proj = ref Blackboard1024.Project<TestState>(ref bb);
            proj.A = 100;

            // Mutate again through a fresh projection reference
            ref var proj2 = ref Blackboard1024.Project<TestState>(ref bb);
            proj2.B = 200;

            // Both mutations must be visible
            ref var verify = ref Blackboard1024.Project<TestState>(ref bb);
            Assert.Equal(100, verify.A);
            Assert.Equal(200, verify.B);
        }
    }
}
