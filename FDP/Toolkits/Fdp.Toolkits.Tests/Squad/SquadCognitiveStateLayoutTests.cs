using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// P0-02: Verifies that <see cref="SquadCognitiveState"/> is exactly 1024 bytes
    /// and that its sub-region offsets match the compile-time constants in
    /// <see cref="SquadCognitiveStateOffsets"/>.
    /// </summary>
    public unsafe class SquadCognitiveStateLayoutTests
    {
        [Fact]
        public void SquadCognitiveState_TotalSizeIs1024()
        {
            Assert.Equal(1024, Unsafe.SizeOf<SquadCognitiveState>());
        }

        [Fact]
        public void SquadCognitiveState_OffsetsMatchConstants()
        {
            // Use a zero-initialised instance on the stack and take address of each field.
            SquadCognitiveState s = default;
            byte* origin = (byte*)&s;

            // Scalars region starts at offset 0 (ManeuverKind is first field).
            Assert.Equal(SquadCognitiveStateOffsets.Scalars,
                (int)((byte*)&s.ManeuverKind - origin));

            // Elements sub-struct at offset 16.
            Assert.Equal(SquadCognitiveStateOffsets.Elements,
                (int)((byte*)&s.Elements - origin));

            // Slots sub-struct at offset 48.
            Assert.Equal(SquadCognitiveStateOffsets.Slots,
                (int)((byte*)&s.Slots - origin));

            // Roles sub-struct at offset 144.
            Assert.Equal(SquadCognitiveStateOffsets.Roles,
                (int)((byte*)&s.Roles - origin));

            // Assignment sub-struct at offset 176.
            Assert.Equal(SquadCognitiveStateOffsets.Assignment,
                (int)((byte*)&s.Assignment - origin));

            // Contacts sub-struct at offset 432.
            Assert.Equal(SquadCognitiveStateOffsets.Contacts,
                (int)((byte*)&s.Contacts - origin));
        }

        [Fact]
        public void SquadCognitiveState_DefaultIsAllZero()
        {
            SquadCognitiveState s = default;
            byte* p = (byte*)&s;
            for (int i = 0; i < 1024; i++)
                Assert.Equal(0, p[i]);
        }

        [Fact]
        public void SquadCognitiveState_ProjectAliasesBb()
        {
            // Write through the projected ref and verify the raw blackboard bytes changed.
            Blackboard1024 bb = default;
            SquadCognitiveState.Project(ref bb).ManeuverKind = 0xABCD;

            // ManeuverKind is at offset 0 (little-endian ushort).
            byte* p = (byte*)&bb;
            ushort readBack = (ushort)(p[0] | (p[1] << 8));
            Assert.Equal((ushort)0xABCD, readBack);
        }
    }
}
