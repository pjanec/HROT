using Fdp.Interfaces;

// Note: Using MSTest attributes because usually existing tests use them? 
// Wait, prompt said "Tests: We use xUnit".
// But the snippet in TASK-DETAILS.md used [TestClass] and [TestMethod] which is MSTest.
// And "FDP.Toolkit.Tkb.Tests" project I created used xUnit.
// I should use xUnit syntax: [Fact], [Theory].

// Re-reading BATCH-01-INSTRUCTIONS.md: "We use xUnit for testing."
// TASK-DETAILS.md snippets show MSTest which is contradictory.
// I will use xUnit as per the Batch Instructions and my project file.

using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    public class PackedKeyTests
    {
        [Fact]
        public void Create_CombinesOrdinalAndInstanceId()
        {
            int ordinal = 100;
            int instanceId = 5;
            long packed = PackedKey.Create(ordinal, instanceId);
            
            Assert.Equal(ordinal, PackedKey.GetOrdinal(packed));
            Assert.Equal(instanceId, PackedKey.GetInstanceId(packed));
            
            // Verify bits manually to be sure
            // 100 << 32 | 5
            long expected = ((long)100 << 32) | 5;
            Assert.Equal(expected, packed);
        }

        [Fact]
        public void Create_HandlesZeroValues()
        {
            long packed = PackedKey.Create(0, 0);
            Assert.Equal(0, PackedKey.GetOrdinal(packed));
            Assert.Equal(0, PackedKey.GetInstanceId(packed));
        }
        
        [Fact]
        public void Create_HandlesMaxValues()
        {
            long packed = PackedKey.Create(int.MaxValue, int.MaxValue);
            Assert.Equal(int.MaxValue, PackedKey.GetOrdinal(packed));
            // InstanceId is cast to uint then int, so int.MaxValue (positive) is preserved.
            Assert.Equal(int.MaxValue, PackedKey.GetInstanceId(packed));
        }

        [Fact]
        public void Create_HandlesNegativeInstanceId_TreatsAsUnsigned()
        {
            // If instanceId is -1 (0xFFFFFFFF)
            long packed = PackedKey.Create(1, -1);
            Assert.Equal(1, PackedKey.GetOrdinal(packed));
            // GetInstanceId returns int, so it should come back as -1
            Assert.Equal(-1, PackedKey.GetInstanceId(packed));
        }
    }
}
