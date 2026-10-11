using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ CE-3172 / CE-3173 S4e (docs/DESIGN_Entity_Control.md §8c) — possession's replicated state: the operator's axes reach the
    /// muscle node as <see cref="ManualDrive"/>, and who holds a unit reaches every node as <see cref="PossessedBy"/> — set, re-set on a
    /// take-over, and REMOVED by one <c>Held = 0</c> sample when the brain drops it (no revoke message to lose).
    /// </summary>
    public sealed class PossessionReplicationTests
    {
        private static (EntityRepository World, Entity Unit, NetworkEntityMap Map) Node(long netId)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<ManualDrive>();
            w.RegisterComponent<PossessedBy>();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new NetworkIdentity { Value = netId });
            var map = new NetworkEntityMap();
            map.Register(netId, unit);
            return (w, unit, map);
        }

        [Fact]
        public void CE3173_ManualDrive_TheAxesOfTheBrain_AreAppliedOnTheMuscle()
        {
            var drive = new ManualDrive { Forward = 1f, Lateral = -1f, Steer = 0.5f, Yaw = 2f, Gait = 1, Seq = 9 };
            var sample = EntityManualDriveEgressTranslator.ToSample(77L, drive);
            Assert.Equal(77L, sample.EntityId);

            var (muscle, unit, map) = Node(77L);
            using (muscle)
            {
                var ingress = new EntityManualDriveIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, muscle);
                cmd.Playback(muscle);
                Assert.Equal(drive, muscle.GetComponentRO<ManualDrive>(unit));

                // the brain's let-go (axes zeroed, sequence restarted) still lands: the latest sample wins
                var stop = EntityManualDriveEgressTranslator.ToSample(77L, new ManualDrive { Yaw = 2f });
                ingress.Apply(in stop, cmd, muscle);
                cmd.Playback(muscle);
                Assert.Equal(0f, muscle.GetComponentRO<ManualDrive>(unit).Forward);
            }
        }

        [Fact]
        public void CE3172_PossessedBy_IsSetOnEveryNode_AndRemovedByOneReleaseSample()
        {
            var (brain, brainsUnit, _) = Node(55L);
            var (station, stationsUnit, stationMap) = Node(55L);
            using (brain)
            using (station)
            {
                var egress = new EntityPossessedByEgressTranslator(participant: null);
                var ingress = new EntityPossessedByIngressTranslator(participant: null, stationMap);
                using var cmd = new EntityCommandBuffer();

                brain.AddComponent(brainsUnit, new PossessedBy { StationNodeId = 300, Mode = PossessionMode.Fps, Epoch = 1 });
                var held = Assert.Single(egress.Scan(brain));
                Assert.Equal((byte)1, held.Held);
                Assert.Empty(egress.Scan(brain));   // unchanged ⇒ nothing sent
                ingress.Apply(in held, cmd, station);
                cmd.Playback(station);
                Assert.Equal(300L, station.GetComponentRO<PossessedBy>(stationsUnit).StationNodeId);

                brain.SetComponent(brainsUnit, new PossessedBy { StationNodeId = 400, Mode = PossessionMode.Fps, Epoch = 2 });   // a take-over
                var taken = Assert.Single(egress.Scan(brain));
                ingress.Apply(in taken, cmd, station);
                cmd.Playback(station);
                Assert.Equal(400L, station.GetComponentRO<PossessedBy>(stationsUnit).StationNodeId);

                brain.RemoveComponent<PossessedBy>(brainsUnit);   // released
                var released = Assert.Single(egress.Scan(brain));
                Assert.Equal((byte)0, released.Held);
                Assert.Empty(egress.Scan(brain));                   // said once
                ingress.Apply(in released, cmd, station);
                cmd.Playback(station);
                Assert.False(station.HasComponent<PossessedBy>(stationsUnit));
            }
        }

        [Fact]
        public void CE3174_WeaponAmmo_TheBrainsCount_ReachesTheStation()
        {
            var ammo = new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 2 };
            ammo.Set(0, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 27, MaxAmmo = 30, MagazineSize = 10, MagazineRounds = 7 });
            ammo.Set(1, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 2, MaxAmmo = 4 });
            var sample = EntityWeaponAmmoEgressTranslator.ToSample(66L, ammo);
            var (station, unit, map) = Node(66L);
            using (station)
            {
                station.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>();
                var ingress = new EntityWeaponAmmoIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, station);
                cmd.Playback(station);
                Assert.Equal(ammo, station.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponAmmo>(unit));
            }
        }
    }
}
