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

        /// <summary>
        /// ⭐ WL-2b (CE-1049, 📄 DESIGN_Weapon_Layers §5d, W-2/W-3/W-11) — the round trip: the Brain's order to a turret reaches the
        /// Muscle's turret part with the target resolved to the Muscle's own entity; the Muscle's weapon muscle slews and reports; the
        /// report reaches the Brain's turret part. Each side owns only its half (<c>dtWeaponTaskStatus</c> granted to the Muscle).
        /// ⭐ ui handoff P2 / P4 — the order's appended <see cref="WeaponTask.Arc"/> (the laid gun's arc) and the posture's appended
        /// <see cref="WeaponPosture.ActiveMount"/> cross too. (Red by construction — not run: a translator without its field delivers Auto / 0.)
        /// </summary>
        [Fact]
        public void WL2b_TheBrainsOrder_ReachesTheMuscle_AndTheReportComesBack()
        {
            using var participant = new DdsParticipant(230u);
            var statusKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)Hrot.NED.Descriptors.EDescriptorType.dtWeaponTaskStatus, 0);
            var poseKey   = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)Hrot.NED.Descriptors.EDescriptorType.dtTurretPose, 0);
            EntityRepository Node(int local, out NetworkEntityMap map, out Entity turret)
            {
                var w = World();
                w.RegisterComponent<WeaponTask>();
                w.RegisterComponent<WeaponTaskStatus>();
                w.RegisterComponent<WeaponPosture>();
                w.RegisterComponent<SimTransform>();
                w.RegisterComponent<TkbIdentity>();
                w.RegisterManagedComponent<DescriptorOwnership>();
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                var type = new Fdp.Interfaces.TkbTemplate("Tank", 1);
                type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { new Fdp.Toolkit.Tkb.Domain.TurretDto { PivotZ = 2f } } });
                db.Register(type);
                w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                var (hull, t) = Tank(w, 6300L, primaryOwner: 1, localNode: local);
                w.AddComponent(hull, new SimTransform { Rotation = System.Numerics.Quaternion.Identity });
                w.AddComponent(hull, new TkbIdentity { TkbType = 1 });
                w.SetManagedComponent(hull, new DescriptorOwnership { Map = { [statusKey] = 2, [poseKey] = 2 } });
                var target = w.CreateEntity();
                w.AddComponent(target, new NetworkIdentity(6301L));
                w.AddComponent(target, new SimTransform { Position = new System.Numerics.Vector3(0, 300, 0), Rotation = System.Numerics.Quaternion.Identity });
                map = new NetworkEntityMap();
                map.Register(6300L, hull);
                map.Register(6301L, target);
                turret = t;
                return w;
            }

            using var brain = Node(1, out var brainMap, out var brainTurret);
            using var muscle = Node(2, out var muscleMap, out var muscleTurret);
            var taskOut = new EntityWeaponTaskEgressTranslator(participant, brainMap);
            var taskIn = new EntityWeaponTaskIngressTranslator(participant, muscleMap);
            var statusOut = new EntityWeaponTaskStatusEgressTranslator(participant);
            var statusIn = new EntityWeaponTaskStatusIngressTranslator(participant, brainMap);
            Assert.True(brainMap.TryGetEntity(6301L, out var brainTarget));
            brain.AddComponent(brainTurret, new WeaponTask { Mode = WeaponTaskMode.Track, Target = brainTarget, Epoch = 3, Arc = ArcPreference.High });
            Assert.True(brainMap.TryGetEntity(6300L, out var brainHull));
            Assert.True(muscleMap.TryGetEntity(6300L, out var muscleHull));
            brain.AddComponent(brainHull, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = brainTarget, ActiveMount = 33 });
            var postureOut = new EntityWeaponPostureEgressTranslator(participant, brainMap);
            var postureIn = new EntityWeaponPostureIngressTranslator(participant, muscleMap);

            Thread.Sleep(200);
            taskOut.ScanAndPublish(brain);
            statusOut.ScanAndPublish(brain);   // the Brain does not own the report: sends nothing
            postureOut.ScanAndPublish(brain);
            Thread.Sleep(300);
            using (var cmd = new EntityCommandBuffer()) { taskIn.PollIngress(cmd, muscle); cmd.Playback(muscle); }
            using (var cmd = new EntityCommandBuffer()) { postureIn.PollIngress(cmd, muscle); cmd.Playback(muscle); }
            Assert.True(muscleMap.TryGetEntity(6301L, out var muscleTarget));
            Assert.Equal(muscleTarget, muscle.GetComponentRO<WeaponTask>(muscleTurret).Target);
            Assert.Equal(3u, muscle.GetComponentRO<WeaponTask>(muscleTurret).Epoch);
            Assert.Equal(ArcPreference.High, muscle.GetComponentRO<WeaponTask>(muscleTurret).Arc);   // ⭐ ui P2
            Assert.Equal((byte)33, muscle.GetComponentRO<WeaponPosture>(muscleHull).ActiveMount);    // ⭐ ui P4
            Assert.Equal(muscleTarget, muscle.GetComponentRO<WeaponPosture>(muscleHull).Target);

            new Fdp.Toolkit.Combat.Systems.WeaponTaskDispatcherSystem().Execute(muscle, 1f / 60f);
            Assert.Equal(WeaponTaskState.Slewing, muscle.GetComponentRO<WeaponTaskStatus>(muscleTurret).State);
            taskOut.ScanAndPublish(muscle);    // the Muscle does not own the order: sends nothing
            statusOut.ScanAndPublish(muscle);
            Thread.Sleep(300);
            using (var cmd = new EntityCommandBuffer()) { statusIn.PollIngress(cmd, brain); cmd.Playback(brain); }

            Assert.Equal(new WeaponTaskStatus { Epoch = 3, State = WeaponTaskState.Slewing }, brain.GetComponentRO<WeaponTaskStatus>(brainTurret));
            Assert.Equal(1, taskOut.SentSampleCount);
            Assert.Equal(1, statusOut.SentSampleCount);
        }
    }
}
