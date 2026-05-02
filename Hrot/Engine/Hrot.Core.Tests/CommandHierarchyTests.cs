using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core.CommandHierarchy;
using Hrot.Map.Definitions;
using Fdp.Core;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// Unit tests for all types introduced in CS001, CS002, CS003, CS004, CS015.
    /// </summary>
    public class CommandHierarchyTests
    {
        // ── CS001: TacticalDesignation enum ──────────────────────────────────

        [Fact]
        public void TacticalDesignation_Default_IsUndefined()
        {
            Assert.Equal(TacticalDesignation.Undefined, default(TacticalDesignation));
        }

        [Fact]
        public void TacticalDesignation_UnderlyingValues_MatchExpected()
        {
            Assert.Equal((ushort)0, (ushort)TacticalDesignation.Undefined);
            Assert.Equal((ushort)1, (ushort)TacticalDesignation.Commander);
            Assert.Equal((ushort)2, (ushort)TacticalDesignation.SquadLeader);
            Assert.Equal((ushort)3, (ushort)TacticalDesignation.Wingman);
            Assert.Equal((ushort)4, (ushort)TacticalDesignation.Support);
        }

        // ── CS002: UnitSubordinate ────────────────────────────────────────────

        [Fact]
        public void UnitSubordinate_SizeIs12Bytes()
        {
            // Entity is int(4)+ushort(2)+2pad=8B, 4-byte aligned; ushort designation=2B; 2B pad => 12B total
            Assert.Equal(12, Marshal.SizeOf<UnitSubordinate>());
        }

        [Fact]
        public void UnitSubordinate_ComponentIdAttribute_IsCorrect()
        {
            var attr = typeof(UnitSubordinate)
                .GetCustomAttributes(typeof(ComponentIdAttribute), false)
                .Cast<ComponentIdAttribute>()
                .Single();
            Assert.Equal(HrotComponentIds.UnitSubordinate, attr.Id);
        }

        [Fact]
        public void UnitSubordinate_Default_HasNullCommanderAndUndefinedDesignation()
        {
            var comp = new UnitSubordinate();
            Assert.Equal(Entity.Null, comp.Commander);
            Assert.Equal(TacticalDesignation.Undefined, comp.Designation);
        }

        [Fact]
        public void UnitSubordinate_EcsWorldRegistration_ProvidesNonNullTable()
        {
            var world = new EntityRepository();
            world.RegisterComponent<UnitSubordinate>();
            Assert.NotNull(world.GetComponentTable<UnitSubordinate>());
            world.Dispose();
        }

        // ── CS003: UnitRoster ─────────────────────────────────────────────────

        [Fact]
        public unsafe void UnitRoster_SizeIs168Bytes()
        {
            // Count(4) + 4-byte alignment pad before long[] + SubordinateEntities(16*8=128) + TacticalDesignations(16*2=32) = 168
            Assert.Equal(168, System.Runtime.CompilerServices.Unsafe.SizeOf<UnitRoster>());
        }

        [Fact]
        public void UnitRoster_DataPolicyAttribute_HasNoSave()
        {
            var attr = typeof(UnitRoster)
                .GetCustomAttributes(typeof(DataPolicyAttribute), false)
                .Cast<DataPolicyAttribute>()
                .Single();
            Assert.True((attr.Policy & DataPolicy.NoSave) != 0);
        }

        [Fact]
        public void UnitRoster_Capacity_Is16()
        {
            Assert.Equal(16, UnitRoster.Capacity);
        }

        [Fact]
        public unsafe void UnitRoster_WriteToIndex15_DoesNotCorruptAdjacentMemory()
        {
            // Allocate a buffer larger than the struct so we can check the guard byte
            const int structSize = 168;
            const int guardSize = 8;
            byte* buf = stackalloc byte[structSize + guardSize];

            // Write a sentinel into the guard region
            for (int i = 0; i < guardSize; i++)
                buf[structSize + i] = 0xAB;

            UnitRoster* r = (UnitRoster*)buf;

            // Write to the last SubordinateEntities slot
            r->SubordinateEntities[UnitRoster.Capacity - 1] = long.MaxValue;

            // Guard bytes must be untouched
            for (int i = 0; i < guardSize; i++)
                Assert.Equal(0xAB, buf[structSize + i]);
        }

        // ── CS004: HrotComponentIds uniqueness ───────────────────────────────

        [Fact]
        public void HrotComponentIds_NewIds_ArePresent()
        {
            Assert.Equal((byte)182, HrotComponentIds.UnitRoster);
            Assert.Equal((byte)183, HrotComponentIds.UnitSubordinate);
            Assert.Equal((byte)184, HrotComponentIds.InitialUnitSubordinateIntent);
        }

        // ── CS015: CommandHierarchyEvents ────────────────────────────────────

        [Fact]
        public void CommandHierarchyEvents_AllThreeStructs_AreUnmanaged()
        {
            // If the structs are not unmanaged this will not compile;
            // we verify at runtime by checking they satisfy the generic constraint.
            static void CheckUnmanaged<T>() where T : unmanaged { }
            CheckUnmanaged<CmdAssignSubordinate>();
            CheckUnmanaged<CmdRemoveSubordinate>();
            CheckUnmanaged<CmdAssignSubordinateRejected>();
        }

        [Fact]
        public void CommandHierarchyEvents_EventIds_AreDistinctAndInRange()
        {
            var assignId = GetEventId<CmdAssignSubordinate>();
            var removeId = GetEventId<CmdRemoveSubordinate>();
            var rejectedId = GetEventId<CmdAssignSubordinateRejected>();

            // All three must be distinct
            Assert.NotEqual(assignId, removeId);
            Assert.NotEqual(assignId, rejectedId);
            Assert.NotEqual(removeId, rejectedId);

            // All must be in the 2200-2299 range
            Assert.InRange(assignId,   2200, 2299);
            Assert.InRange(removeId,   2200, 2299);
            Assert.InRange(rejectedId, 2200, 2299);

            // Must not collide with known IDs
            int[] knownIds = { 2104, 2105, 2108, 2109, 9003 };
            Assert.DoesNotContain(assignId,   knownIds);
            Assert.DoesNotContain(removeId,   knownIds);
            Assert.DoesNotContain(rejectedId, knownIds);
        }

        [Fact]
        public void CmdAssignSubordinateRejected_HasOnlySubordinateField()
        {
            var fields = typeof(CmdAssignSubordinateRejected)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.Single(fields);
            Assert.Equal("Subordinate", fields[0].Name);
            Assert.Equal(typeof(Entity), fields[0].FieldType);
        }

        // ── Helper ─────────────────────────────────────────────────────────────

        private static int GetEventId<T>()
        {
            var attr = typeof(T)
                .GetCustomAttributes(typeof(EventIdAttribute), false)
                .Cast<EventIdAttribute>()
                .Single();
            return attr.Id;
        }
    }
}
