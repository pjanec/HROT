using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7, D-1) — a ground cell's craters cross the wire: the deciding node's
    /// <see cref="EntityGroundDamageEgressTranslator"/> form, a receiver's <see cref="EntityGroundDamageIngressTranslator"/> builds a ghost carrying the craters, and
    /// that node's own view answers the cratered ground (<see cref="DoorStates.Craters"/>) — a standalone brain sees the Muscle's crater. The breach pair's shape.
    /// </summary>
    public sealed class EntityGroundDamageTranslatorTests
    {
        private static EntityRepository World(out TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<GroundDamage>();
            int n = 101;
            terrain = new TerrainWorld { Name = "range", Height = new TerrainHeightGrid(Vector2.Zero, 1f, n, n, new float[n * n]) };
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        private static GroundDamage Two()
        {
            var d = new GroundDamage { CellX = 0, CellY = 0 };
            GroundDamages.Add(ref d, new Crater(new Vector2(30f, 30f), 3.6f, 1.6f, 0.3f, (SimInstant)12.5, CraterKind.Large), out _);
            GroundDamages.Add(ref d, new Crater(new Vector2(50f, 20f), 1f, 0.4f, 0.1f, (SimInstant)13.0, CraterKind.Small), out _);
            return d;
        }

        /// <summary>The toolkit states the wire ordinal (it cannot reference the NED assembly); this pins the two equal.</summary>
        [Fact]
        public void TheToolkitsOrdinal_IsTheDescriptorsOrdinal()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtGroundDamage, GroundDamageSystem.DescriptorOrdinal);

        /// <summary>
        /// The wire form round-trips BYTE FOR BYTE (the same craters, the same birth — V-10: no node recomputes a profile from the charge) and reaches a ghost whose
        /// view reads the crater at once; the recorded owner never takes its own sample back; an unchanged component matches its last sample (no resend).
        /// 🔴 Red-proof: drop the <c>BornAt</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void TheSample_RoundTripsEveryCrater_TheReplicasViewIsCratered_AndTheOwnerNeverTakesItsOwnBack()
        {
            using var replica = World(out var terrain);
            var original = Two();
            var sample = EntityGroundDamageEgressTranslator.ToSample(6100, in original);
            Assert.True(EntityGroundDamageEgressTranslator.Matches(in sample, in original));
            var map = new NetworkEntityMap();
            var ingress = new EntityGroundDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);

            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(6100, out var ghost));
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<GroundDamage>(in original))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<GroundDamage>(replica.GetComponentRO<GroundDamage>(ghost)))));
            Assert.Equal(-1.6f, TerrainWorldQuery.ForView(replica, terrain).GroundHeightAt(30f, 30f), 4);

            replica.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));
            var newer = original;
            GroundDamages.Add(ref newer, new Crater(new Vector2(10f, 10f), 1f, 0.4f, 0.1f, (SimInstant)14.0, CraterKind.Small), out _);
            replica.SetComponent(ghost, newer);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(3, replica.GetComponentRO<GroundDamage>(ghost).Count);
            Assert.False(EntityGroundDamageEgressTranslator.Matches(in sample, in newer));
        }
    }
}
