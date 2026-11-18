using System;
using Fdp.Core;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4657</c> (thermal drivers TD-S1; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4, §6) — the two new drivers cross the wire BYTE FOR BYTE to a late
    /// joiner: <see cref="HeatSource"/> (<c>dtHeatSource</c> 240) and <see cref="FireState"/> (<c>dtFireState</c> 241, 8 spots). An unknown id makes a ghost, an unchanged
    /// component matches its last sample (no resend), and the recorded owner never takes its own sample back. The heat-marks pair's shape (CE-4664).
    /// </summary>
    public sealed class EntityThermalDriverTranslatorTests
    {
        private static EntityRepository LateJoiner()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<NetworkAuthority>(); w.RegisterComponent<GhostStateTracker>();
            w.RegisterComponent<HeatSource>(); w.RegisterComponent<FireState>();
            return w;
        }

        private static bool SameBytes<T>(in T a, in T b) where T : unmanaged
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in a)).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in b)));

        /// <summary>The ordinals fixed at dispatch (HANDOFF_Coordinator_To_Backend_Thermal_Drivers.md): 240 / 241, and the component ids 487 / 488.</summary>
        [Fact]
        public void CE4657_TheIds_AreTheOnesFixedAtDispatch()
        {
            Assert.Equal(240L, (long)Hrot.NED.Descriptors.EDescriptorType.dtHeatSource);
            Assert.Equal(241L, (long)Hrot.NED.Descriptors.EDescriptorType.dtFireState);
            Assert.Equal(487, GlobalComponentIds.HeatSource);
            Assert.Equal(488, GlobalComponentIds.FireState);
        }

        /// <summary>
        /// ⭐ An idling engine's state and both times reach a late joiner; a newer local owner's state is not overwritten by its own old sample.
        /// 🔴 Red-proof: drop the <c>MotionSince</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4657_AHeatSource_IsOnEveryNode_ALateJoinerIncluded_ByteForByte()
        {
            using var lateJoiner = LateJoiner();
            var original = HeatSource.Of(HeatSourceState.Idle, (SimInstant)612.5, (SimInstant)600.25);
            var sample = EntityHeatSourceEgressTranslator.ToSample(7100, in original);

            var map = new NetworkEntityMap();
            var ingress = new EntityHeatSourceIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 3);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.True(map.TryGetEntity(7100, out var ghost));
            Assert.True(SameBytes(in original, lateJoiner.GetComponentRO<HeatSource>(ghost)));

            lateJoiner.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 3, localNodeId: 3));
            var newer = HeatSource.Of(HeatSourceState.Running, (SimInstant)700.0, (SimInstant)700.0);
            lateJoiner.SetComponent(ghost, newer);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.Equal(HeatSourceState.Running, lateJoiner.GetComponentRO<HeatSource>(ghost).State);
        }

        /// <summary>
        /// ⭐ A burning holder's spots — every field of every spot, all 8 — reach a late joiner; <c>Matches</c> holds for the unchanged component and fails once a spot is
        /// added (a resend). 🔴 Red-proof: drop the <c>Intensity</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4657_AFireState_IsOnEveryNode_ALateJoinerIncluded_ByteForByte()
        {
            using var lateJoiner = LateJoiner();
            var original = new FireState();
            for (int i = 0; i < FireState.Capacity; i++)
                Assert.True(original.TryAdd(new FireSpot(i, -2f * i, 0.5f + i, 1f + 0.25f * i, (SimInstant)(100.0 + i), 300f + i, 0.1f * (i + 1)), now: 100.0));
            Assert.False(original.TryAdd(new FireSpot(9, 9, 9, 1, (SimInstant)100.0, 300f, 1f), now: 100.0));   // full: every spot burns
            var sample = EntityFireStateEgressTranslator.ToSample(7200, in original);
            Assert.True(EntityFireStateEgressTranslator.Matches(in sample, in original));

            var map = new NetworkEntityMap();
            var ingress = new EntityFireStateIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 3);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.True(map.TryGetEntity(7200, out var ghost));
            Assert.True(SameBytes(in original, lateJoiner.GetComponentRO<FireState>(ghost)));

            var fewer = new FireState();
            fewer.TryAdd(new FireSpot(0, 0, 0, 2f, (SimInstant)50.0, 60f, 1f), now: 50.0);
            Assert.False(EntityFireStateEgressTranslator.Matches(in sample, in fewer));
        }

        /// <summary>⭐ A spot that has gone out frees its slot: a full holder takes a new spot once one of its spots burned out.</summary>
        [Fact]
        public void CE4657_ASpotThatWentOut_FreesItsSlot()
        {
            var fire = new FireState();
            for (int i = 0; i < FireState.Capacity; i++) fire.TryAdd(new FireSpot(i, 0, 0, 1, (SimInstant)0.0, i == 3 ? 10f : 1000f, 1f), now: 0.0);
            Assert.True(fire.IsBurning(20.0));
            Assert.True(fire.TryAdd(new FireSpot(42, 0, 0, 1, (SimInstant)20.0, 100f, 0.5f), now: 20.0));
            Assert.Equal(FireState.Capacity, fire.Count);
            Assert.Equal(42f, fire.Get(3).X);
        }
    }
}
