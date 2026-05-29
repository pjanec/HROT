using Fdp.Core.CommandHierarchy;
using Xunit;

namespace Fdp.Core.Tests.CommandHierarchy
{
    /// <summary>
    /// Unit tests for <see cref="UnitRoster.Add"/> and <see cref="UnitRoster.IndexOf"/> helpers (P0.04).
    /// </summary>
    public class UnitRosterTests
    {
        // SC-P0-04-1: Fill 16 entries → slots 0..15 returned; 17th call returns -1 without mutating Count
        [Fact]
        public unsafe void Add_Fill16_Returns0To15_Then17thReturnsNegativeOne()
        {
            var roster = new UnitRoster();

            for (int i = 0; i < UnitRoster.Capacity; i++)
            {
                int slot = UnitRoster.Add(ref roster, (long)(i + 1));
                Assert.Equal(i, slot);
            }

            Assert.Equal(UnitRoster.Capacity, roster.Count);

            // 17th add must return -1 without mutating Count
            int overflow = UnitRoster.Add(ref roster, 9999L);
            Assert.Equal(-1, overflow);
            Assert.Equal(UnitRoster.Capacity, roster.Count);
        }

        // SC-P0-04-2: IndexOf returns correct slot for present entity, -1 for absent
        [Fact]
        public unsafe void IndexOf_ReturnsCorrectSlot_OrNegativeOneWhenAbsent()
        {
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, 100L);
            UnitRoster.Add(ref roster, 200L);
            UnitRoster.Add(ref roster, 300L);

            Assert.Equal(0, UnitRoster.IndexOf(ref roster, 100L));
            Assert.Equal(1, UnitRoster.IndexOf(ref roster, 200L));
            Assert.Equal(2, UnitRoster.IndexOf(ref roster, 300L));
            Assert.Equal(-1, UnitRoster.IndexOf(ref roster, 999L));
        }

        // SC-P0-04-3: After Add(e) → IndexOf(e) returns the same slot index
        [Fact]
        public unsafe void Add_ThenIndexOf_ReturnsSameSlot()
        {
            var roster = new UnitRoster();
            long packedValue = unchecked((long)0xDEADBEEFCAFEBABEUL);

            int addedSlot = UnitRoster.Add(ref roster, packedValue);
            int foundSlot = UnitRoster.IndexOf(ref roster, packedValue);

            Assert.Equal(addedSlot, foundSlot);
        }

        // Edge case: empty roster IndexOf returns -1; after Add, IndexOf finds it
        [Fact]
        public unsafe void IndexOf_EmptyRoster_ReturnsNegativeOne_ThenAfterAdd_FindsIt()
        {
            var roster = new UnitRoster();

            Assert.Equal(-1, UnitRoster.IndexOf(ref roster, 42L));
            Assert.Equal(0, roster.Count);

            UnitRoster.Add(ref roster, 42L);

            Assert.Equal(0, UnitRoster.IndexOf(ref roster, 42L));
            Assert.Equal(1, roster.Count);
        }

        // Designations are stored correctly alongside entity handles
        [Fact]
        public unsafe void Add_WithDesignation_StoresDesignationInParallelSlot()
        {
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, 500L, designation: 7);

            Assert.Equal(500L, roster.SubordinateEntities[0]);
            Assert.Equal(7, roster.TacticalDesignations[0]);
        }
    }
}
