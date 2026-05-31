using System.Runtime.CompilerServices;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// P0-01: Verifies that <see cref="AssignmentSlot"/> is exactly 16 bytes and that
    /// <see cref="AssignmentSlotArray"/> helpers function correctly.
    /// </summary>
    public class AssignmentSlotLayoutTests
    {
        [Fact]
        public void AssignmentSlot_SizeIs16Bytes()
        {
            Assert.Equal(16, Unsafe.SizeOf<AssignmentSlot>());
        }

        [Fact]
        public void AssignmentSlotArray_SizeIs256Bytes()
        {
            // 16 slots * 16 bytes each = 256 bytes.
            Assert.Equal(256, Unsafe.SizeOf<AssignmentSlotArray>());
        }

        [Fact]
        public void AssignmentSlotArray_GetSlot_RoundTrip()
        {
            var arr = new AssignmentSlotArray();
            arr.GetSlot(3).AssignedTargetHandle = unchecked((long)0xDEADBEEF_CAFEBABE);
            arr.GetSlot(3).AssignmentScore      = 0.75f;
            arr.GetSlot(3).FocusFireCount       = 2;
            arr.GetSlot(3).Flags                = 0x05;

            Assert.Equal(unchecked((long)0xDEADBEEF_CAFEBABE), arr.GetSlot(3).AssignedTargetHandle);
            Assert.Equal(0.75f, arr.GetSlot(3).AssignmentScore);
            Assert.Equal(2, arr.GetSlot(3).FocusFireCount);
            Assert.Equal(0x05, arr.GetSlot(3).Flags);
        }
    }
}
