using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (vehicle appearance VA-S2, VA-12; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — a vehicle's openings cross the
    /// wire BYTE FOR BYTE to a late joiner (<c>dtVehicleOpenings</c> 243, component 290); an unchanged component matches its last sample (no resend); the recorded
    /// owner never takes its own sample back.
    /// </summary>
    public sealed class EntityVehicleOpeningsTranslatorTests
    {
        private static EntityRepository LateJoiner()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<NetworkAuthority>(); w.RegisterComponent<GhostStateTracker>();
            w.RegisterComponent<VehicleOpenings>();
            return w;
        }

        private static bool SameBytes<T>(in T a, in T b) where T : unmanaged
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in a)).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in b)));

        /// <summary>The ids fixed at dispatch (HANDOFF_Coordinator_To_Backend_Vehicle_Appearance.md): ordinal 243, component 290 (291 reserved for the lights).</summary>
        [Fact]
        public void CE4670_TheIds_AreTheOnesFixedAtDispatch()
        {
            Assert.Equal(243L, (long)Hrot.NED.Descriptors.EDescriptorType.dtVehicleOpenings);
            Assert.Equal(290, GlobalComponentIds.VehicleOpenings);
            Assert.Equal(291, GlobalComponentIds.VehicleLights);
        }

        /// <summary>
        /// ⭐ A ramp mid-opening and a hatch closing reach a late joiner — every slot's state and time; <c>Matches</c> holds for the unchanged component and fails once a
        /// slot changes; the owner keeps its newer state. 🔴 Red-proof: drop the <c>Since</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4670_AVehiclesOpenings_AreOnEveryNode_ALateJoinerIncluded_ByteForByte()
        {
            using var lateJoiner = LateJoiner();
            var original = new VehicleOpenings { Count = 4 };
            original.Slots[0] = new OpeningSlot { State = OpeningState.Opening, Since = 1300.5 };
            original.Slots[1] = new OpeningSlot { State = OpeningState.Closing, Since = 1290.25 };
            original.Slots[3] = new OpeningSlot { State = OpeningState.Open, Since = 12.0 };
            var sample = EntityVehicleOpeningsEgressTranslator.ToSample(7300, in original);
            Assert.True(EntityVehicleOpeningsEgressTranslator.Matches(in sample, in original));

            var map = new NetworkEntityMap();
            var ingress = new EntityVehicleOpeningsIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 3);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.True(map.TryGetEntity(7300, out var ghost));
            Assert.True(SameBytes(in original, lateJoiner.GetComponentRO<VehicleOpenings>(ghost)));

            var later = original;
            later.Slots[0] = new OpeningSlot { State = OpeningState.Closing, Since = 1320.0 };
            Assert.False(EntityVehicleOpeningsEgressTranslator.Matches(in sample, in later));

            lateJoiner.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 3, localNodeId: 3));
            lateJoiner.SetComponent(ghost, later);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.Equal(OpeningState.Closing, lateJoiner.GetComponentRO<VehicleOpenings>(ghost).Slots[0].State);
        }

        /// <summary>
        /// ⭐ <c>CE-4670</c> (VA-S3) — a vehicle's lights (mask, discipline, time, searchlight aim) reach a late joiner byte for byte (<c>dtVehicleLights</c> 244); the
        /// toolkit's ordinal matches the wire's. 🔴 Red-proof: drop the <c>Discipline</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4670_AVehiclesLights_AreOnEveryNode_ALateJoinerIncluded_ByteForByte()
        {
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtVehicleLights, Fdp.Toolkit.Behavior.Systems.LightRuleSystem.DescriptorOrdinal);
            using var lateJoiner = LateJoiner();
            lateJoiner.RegisterComponent<VehicleLights>();
            var original = new VehicleLights { OnMask = 0b1001, Discipline = LightDiscipline.Convoy, Since = 1200.25, SearchAz = 0.5f, SearchEl = -0.1f };
            var sample = EntityVehicleLightsEgressTranslator.ToSample(7400, in original);
            var map = new NetworkEntityMap();
            var ingress = new EntityVehicleLightsIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 3);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.True(map.TryGetEntity(7400, out var ghost));
            Assert.True(SameBytes(in original, lateJoiner.GetComponentRO<VehicleLights>(ghost)));
        }
    }
}
