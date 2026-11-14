using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Xunit;

namespace Fdp.Core.Tests.CommandHierarchy
{
    /// <summary>
    /// Unit tests for <see cref="UnitRoster.Add"/> and <see cref="UnitRoster.IndexOf"/> helpers (P0.04).
    /// </summary>
    public class UnitRosterTests
    {
        // CE-467: the roster holds Entity handles (it held packed longs before).
        private static readonly Entity E42  = new Entity(42, 1);
        private static readonly Entity E100 = new Entity(100, 1);
        private static readonly Entity E200 = new Entity(200, 1);
        private static readonly Entity E300 = new Entity(300, 1);
        private static readonly Entity E500 = new Entity(500, 1);

        // SC-P0-04-1: Fill 16 entries → slots 0..15 returned; 17th call returns -1 without mutating Count
        [Fact]
        public unsafe void Add_Fill16_Returns0To15_Then17thReturnsNegativeOne()
        {
            var roster = new UnitRoster();

            for (int i = 0; i < UnitRoster.Capacity; i++)
            {
                int slot = UnitRoster.Add(ref roster, new Entity(i + 1, 1));
                Assert.Equal(i, slot);
            }

            Assert.Equal(UnitRoster.Capacity, roster.Count);

            // 17th add must return -1 without mutating Count
            int overflow = UnitRoster.Add(ref roster, new Entity(9999, 1));
            Assert.Equal(-1, overflow);
            Assert.Equal(UnitRoster.Capacity, roster.Count);
        }

        // SC-P0-04-2: IndexOf returns correct slot for present entity, -1 for absent
        [Fact]
        public unsafe void IndexOf_ReturnsCorrectSlot_OrNegativeOneWhenAbsent()
        {
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, E100);
            UnitRoster.Add(ref roster, E200);
            UnitRoster.Add(ref roster, E300);

            Assert.Equal(0, UnitRoster.IndexOf(ref roster, E100));
            Assert.Equal(1, UnitRoster.IndexOf(ref roster, E200));
            Assert.Equal(2, UnitRoster.IndexOf(ref roster, E300));
            Assert.Equal(-1, UnitRoster.IndexOf(ref roster, new Entity(999, 1)));
        }

        // SC-P0-04-3: After Add(e) → IndexOf(e) returns the same slot index
        [Fact]
        public unsafe void Add_ThenIndexOf_ReturnsSameSlot()
        {
            var roster = new UnitRoster();
            var entity = new Entity(0x0EADBEEF, 0xBABE);

            int addedSlot = UnitRoster.Add(ref roster, entity);
            int foundSlot = UnitRoster.IndexOf(ref roster, entity);

            Assert.Equal(addedSlot, foundSlot);
        }

        // Edge case: empty roster IndexOf returns -1; after Add, IndexOf finds it
        [Fact]
        public unsafe void IndexOf_EmptyRoster_ReturnsNegativeOne_ThenAfterAdd_FindsIt()
        {
            var roster = new UnitRoster();

            Assert.Equal(-1, UnitRoster.IndexOf(ref roster, E42));
            Assert.Equal(0, roster.Count);

            UnitRoster.Add(ref roster, E42);

            Assert.Equal(0, UnitRoster.IndexOf(ref roster, E42));
            Assert.Equal(1, roster.Count);
        }

        // Designations are stored correctly alongside entity handles
        [Fact]
        public unsafe void Add_WithDesignation_StoresDesignationInParallelSlot()
        {
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, E500, designation: 7);

            Assert.Equal(E500, roster.SubordinateEntities[0]);
            Assert.Equal(7, roster.TacticalDesignations[0]);
        }
    }
}
