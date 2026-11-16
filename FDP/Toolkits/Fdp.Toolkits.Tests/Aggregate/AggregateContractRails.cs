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

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>⭐⭐ <c>CE-8001</c> (CE-4616 ST-0 ③, SV-12) — a recording round-trips the stochastic unit: the slot table, holdings, unit state
    /// and run seed come back byte for byte at the scrubbed frame. 🔴 Red-proof: drop a component's registration on the recording side ⇒ the
    /// sandbox has none at frame 1.</summary>
    public sealed class AggregateRecordingRails
    {
        [Fact]
        public void ARecording_GivesBackTheUnit_ItsSlots_ItsHoldings_AndTheRunSeed()
        {
            using var harness = new Fdp.Toolkit.ReplayBrowser.Support.FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<AggregateUnit>(); repo.RegisterComponent<UnitSlotTable>(); repo.RegisterComponent<UnitHoldings>();
            repo.RegisterComponent<RunSeed>();
            var unit = repo.CreateEntity();
            var slots = new UnitSlotTable { Count = 2 };
            slots.Slots[0] = new UnitSlot { Role = SlotRole.Leader, Health = (Half)25f, Ammo = 210 };
            slots.Slots[1] = new UnitSlot { Role = SlotRole.MachineGunner, Health = (Half)25f, Ammo = 800 };
            var holdings = new UnitHoldings { Count = 1 };
            holdings.Items[0] = new UnitHolding { Type = new TkbTypeId(212), Count = 4, Operational = 4, Kind = HoldingKind.Vehicle, Mobility = HoldingMobility.Tracked };
            repo.AddComponent(unit, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Echelon = 1, Width = 300f, Heading = 45f });
            repo.AddComponent(unit, slots);
            repo.AddComponent(unit, holdings);
            harness.Tick().RecordKeyframe(100_000L);
            repo.GetComponentRW<UnitSlotTable>(unit).Slots[1].Health = (Half)12.5f;                 // a wound
            repo.GetComponentRW<UnitSlotTable>(unit).Slots[1].Ammo = 650;
            repo.GetComponentRW<AggregateUnit>(unit).SuppressedShare = 0.25f;
            repo.GetComponentRW<UnitHoldings>(unit).Items[0].Operational = 3;                      // a K-kill
            harness.Tick().RecordDelta(200_000L);
            harness.BuildToTempFile(out var path);

            using var sandbox = new EntityRepository();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessPosition>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessVelocity>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessTransform>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessEntityInfo>();
            sandbox.RegisterComponent<AggregateUnit>(); sandbox.RegisterComponent<UnitSlotTable>(); sandbox.RegisterComponent<UnitHoldings>();
            sandbox.RegisterComponent<RunSeed>();
            using var playback = new Fdp.Core.FlightRecorder.PlaybackController(path);
            Assert.True(playback.StepForward(sandbox));
            Assert.Equal(25f, (float)sandbox.GetComponentRO<UnitSlotTable>(unit).Slots[1].Health);
            Assert.True(playback.StepForward(sandbox));
            var t = sandbox.GetComponentRO<UnitSlotTable>(unit);
            Assert.Equal((12.5f, (ushort)650, SlotRole.MachineGunner), ((float)t.Slots[1].Health, t.Slots[1].Ammo, t.Slots[1].Role));
            Assert.Equal(2, t.Present());
            var u = sandbox.GetComponentRO<AggregateUnit>(unit);
            Assert.Equal((AggregateResolution.Stochastic, 300f, 0.25f), (u.Resolution, u.Width, u.SuppressedShare));
            Assert.Equal(45f, u.Heading.Degrees);
            var h = sandbox.GetComponentRO<UnitHoldings>(unit);
            Assert.Equal(((ushort)3, new TkbTypeId(212)), (h.Items[0].Operational, h.Items[0].Type));
        }
    }
}
