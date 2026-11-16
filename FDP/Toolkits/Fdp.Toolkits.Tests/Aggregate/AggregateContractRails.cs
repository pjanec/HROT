using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-8000</c> (CE-4616 §23.2, SV-12, RV-11) — the aggregate contract's rails: the ids and wire values the other two lanes build
    /// against, every recorded struct unmanaged and ≤ 1024 B (EntityCommandBuffer's limit), a company in ONE slot table.
    /// </summary>
    public sealed class AggregateContractRails
    {
        [Fact]
        public void TheContractIds_AreTheFixedOnes()
        {
            Assert.Equal(281, GlobalComponentIds.AggregateUnit);
            Assert.Equal(282, GlobalComponentIds.UnitSlotTable);
            Assert.Equal(283, GlobalComponentIds.UnitHoldings);
            Assert.Equal(284, GlobalComponentIds.FireAllocation);
            Assert.Equal(285, GlobalComponentIds.UnitDetections);
            Assert.Equal(286, GlobalComponentIds.RunSeed);
            Assert.Equal(1, (int)AggregateResolution.Physical);
            Assert.Equal(2, (int)AggregateResolution.Fusion);
            Assert.Equal(3, (int)AggregateResolution.Stochastic);
        }

        [Fact]
        public void EveryRecordedStruct_IsUnmanaged_AndFitsOneCommandBufferRecord()
        {
            Check<AggregateUnit>(); Check<UnitSlotTable>(); Check<UnitHoldings>(); Check<FireAllocation>(); Check<UnitDetections>(); Check<RunSeed>();
            Assert.Equal(8, Unsafe.SizeOf<UnitSlot>());
            Assert.Equal(24, Unsafe.SizeOf<UnitHolding>());   // 20 B of fields, aligned to the TkbTypeId (8)
            Assert.Equal(24, Unsafe.SizeOf<FireLine>());

            static void Check<T>() where T : unmanaged
            {
                int size = Unsafe.SizeOf<T>();
                Assert.True(size <= 1024, $"{typeof(T).Name} is {size} B — over EntityCommandBuffer's 1024 B (RV-11)");
                Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }
        }

        [Fact]
        public void ACompany_FitsOneSlotTable()
        {
            Assert.Equal(13, UnitSlotTable.CapacityFor(0));
            Assert.Equal(48, UnitSlotTable.CapacityFor(1));
            Assert.Equal(UnitSlotTable.Capacity, UnitSlotTable.CapacityFor(2));
            Assert.True(UnitSlotTable.Capacity >= 120);
        }

        [Fact]
        public void PresentAndAuthorized_DeriveFromTheSlots()
        {
            var t = new UnitSlotTable { Count = 4 };
            t.Slots[0] = new UnitSlot { Role = SlotRole.Leader, Health = (Half)25f };
            t.Slots[1] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)12.5f };
            t.Slots[2] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)0f };
            t.Slots[3] = new UnitSlot { Role = SlotRole.Empty };
            Assert.Equal(2, t.Present());
            Assert.Equal(3, t.Authorized());
            Assert.Equal(12.5f, (float)t.Slots[1].Health);   // a frontal rifle hit (12.5 of 25) is exact in a Half
        }

        [Fact]
        public void UnsetResolution_ReadsAsPhysical()
        {
            Assert.Equal(AggregateResolution.Physical, default(AggregateUnit).Effective);
            Assert.Equal(AggregateResolution.Stochastic, new AggregateUnit { Resolution = AggregateResolution.Stochastic }.Effective);
        }
    }
}
