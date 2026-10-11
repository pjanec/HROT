using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ WL-1b (CE-1049, 📄 docs/DESIGN_Weapon_Layers.md §5b, W-9) — a turret's pose crosses the wire from the node owning the hull's
    /// <c>dtTurretPose</c> to the OTHER node's turret part, keyed (hull network id, part id); small wobbles are not sent; a node that
    /// does not own the pose sends nothing; a sample for a turret this node has not built is dropped, never invented.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("SimHostDds")]
    public sealed class EntityTurretPoseTranslatorTests
    {
        private static EntityRepository World()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<PartMetadata>();
            repo.RegisterComponent<TurretPose>();
            return repo;
        }

        private static (Entity Hull, Entity Turret) Tank(EntityRepository world, long netId, int primaryOwner, int localNode)
        {
            var hull = world.CreateEntity();
            world.AddComponent(hull, new NetworkIdentity(netId));
            world.AddComponent(hull, new NetworkAuthority(primaryOwnerId: primaryOwner, localNodeId: localNode));
            return (hull, TurretParts.Ensure(world, hull, 0));
        }

        [Fact]
        public void WL1b_TheOwnersTurretPose_ReachesTheOtherNodesTurretPart_OnChangeOnly()
        {
            using var participant = new DdsParticipant(228u);
            using var owner = World();
            var (_, ownerTurret) = Tank(owner, 6000L, primaryOwner: 1, localNode: 1);
            var egress = new EntityTurretPoseEgressTranslator(participant);

            using var replica = World();
            var (replicaHull, replicaTurret) = Tank(replica, 6000L, primaryOwner: 1, localNode: 2);
            var map = new NetworkEntityMap();
            map.Register(6000L, replicaHull);
            var ingress = new EntityTurretPoseIngressTranslator(participant, map);

            void Tick()
            {
                egress.ScanAndPublish(owner);
                Thread.Sleep(300);
                using var cmd = new EntityCommandBuffer();
                ingress.PollIngress(cmd, replica);
                cmd.Playback(replica);
            }

            Thread.Sleep(200);
            owner.SetComponent(ownerTurret, new TurretPose { Azimuth = 1.0f, Elevation = 0.1f });
            Tick();
            Assert.Equal(new TurretPose { Azimuth = 1.0f, Elevation = 0.1f }, replica.GetComponentRO<TurretPose>(replicaTurret));
            Assert.Equal(1, egress.SentSampleCount);

            owner.SetComponent(ownerTurret, new TurretPose { Azimuth = 1.0f + EntityTurretPoseEgressTranslator.AngleThreshold / 2f, Elevation = 0.1f });
            Tick();
            Assert.Equal(1, egress.SentSampleCount);   // below the threshold: not sent

            owner.SetComponent(ownerTurret, new TurretPose { Azimuth = -0.5f, Elevation = 0.2f });
            Tick();
            Assert.Equal(2, egress.SentSampleCount);
            Assert.Equal(new TurretPose { Azimuth = -0.5f, Elevation = 0.2f }, replica.GetComponentRO<TurretPose>(replicaTurret));
        }

        /// <summary>A node that does not own the hull's pose publishes nothing; a sample for a hull this node has no turret part for
        /// is dropped (the spawn builds the part; the heartbeat lands later).</summary>
        [Fact]
        public void WL1b_ANonOwnerSendsNothing_AndASampleForAnUnbuiltTurretIsDropped()
        {
            using var participant = new DdsParticipant(229u);
            using var notOwner = World();
            var (_, turret) = Tank(notOwner, 6100L, primaryOwner: 1, localNode: 2);
            notOwner.SetComponent(turret, new TurretPose { Azimuth = 0.8f });
            var quiet = new EntityTurretPoseEgressTranslator(participant);
            quiet.ScanAndPublish(notOwner);
            Assert.Equal(0, quiet.SentSampleCount);

            using var owner = World();
            var (_, ownerTurret) = Tank(owner, 6200L, primaryOwner: 1, localNode: 1);
            owner.SetComponent(ownerTurret, new TurretPose { Azimuth = 0.3f });
            var egress = new EntityTurretPoseEgressTranslator(participant);

            using var replica = World();
            var bare = replica.CreateEntity();   // the hull is known but its turret part was never built
            replica.AddComponent(bare, new NetworkIdentity(6200L));
            var map = new NetworkEntityMap();
            map.Register(6200L, bare);
            var ingress = new EntityTurretPoseIngressTranslator(participant, map);

            Thread.Sleep(200);
            egress.ScanAndPublish(owner);
            Thread.Sleep(300);
            using var cmd = new EntityCommandBuffer();
            ingress.PollIngress(cmd, replica);
            cmd.Playback(replica);
            Assert.Equal(1, ingress.ReceivedSampleCount);
            Assert.Equal(1, ingress.DroppedSampleCount);
            int parts = 0;
            foreach (var _ in replica.Query().With<TurretPose>().Build()) parts++;
            Assert.Equal(0, parts);
        }
    }
}
