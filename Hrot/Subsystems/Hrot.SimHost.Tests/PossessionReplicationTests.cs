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

        /// <summary>
        /// ⭐⭐ <c>CE-2231</c> (Q91 §4-F, F1; <c>HANDOFF_Coordinator_To_Behaviors_Possession_Release</c> PR1) — a Player node is disposable:
        /// when the node holding a unit DEPARTS, the brain releases it by the same path as Esc and the task it paused RESUMES; a unit
        /// held from another node is untouched. Drives the real <see cref="PossessionSystem"/> and the real
        /// <see cref="Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem"/> (the pause and the resume are the ingress's).
        /// </summary>
        [Fact]
        public void CE2231_TheHoldingNodeDeparts_ItsUnitIsReleased_AndItsTaskResumes_AnotherNodesUnitIsKept()
        {
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(w);
            w.Bus.Register<Fdp.Toolkit.Replication.Messages.NodeDeparted>();
            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            var possession = new PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);

            void Frame()
            {
                w.Bus.SwapBuffers(); possession.Execute(w, 0.016f);
                w.Bus.SwapBuffers(); ingress.Execute(w, 0.016f);
            }
            Entity Unit()
            {
                var u = w.CreateEntity();
                w.AddComponent(u, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                w.SetAuthority<Fdp.Toolkit.Behavior.Components.BehaviorState>(u, true);   // this node runs its brain
                Fdp.Toolkit.Behavior.RootStateAccess.EnsureRootState(w, u);
                w.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
                {
                    Entity = u, BehaviorName = Hrot.Map.Definitions.Behavior.BehaviorNames.Idle, JsonParams = "{}",
                    Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,
                });
                return u;
            }
            var mine = Unit();
            var theirs = Unit();
            w.Bus.SwapBuffers(); ingress.Execute(w, 0.016f);
            int idle = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.Idle);
            Assert.Equal(idle, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(mine).ActiveBehaviorHash);

            w.Bus.Publish(new PossessRequestEvent { Unit = mine, StationNodeId = 7, Mode = PossessionMode.Fps });
            w.Bus.Publish(new PossessRequestEvent { Unit = theirs, StationNodeId = 9, Mode = PossessionMode.Fps });
            Frame();
            foreach (var u in new[] { mine, theirs })
                Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Possession, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(u).Origin);

            w.Bus.Publish(new Fdp.Toolkit.Replication.Messages.NodeDeparted { NodeId = 7 });
            Frame();

            Assert.False(w.HasComponent<PossessedBy>(mine));                     // released — Held = 0 replicates to every node
            var resumed = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(mine);
            Assert.Equal(idle, resumed.ActiveBehaviorHash);                     // the paused task is back
            Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior, resumed.Origin);

            Assert.Equal(9L, w.GetComponentRO<PossessedBy>(theirs).StationNodeId);   // node 9 is still here — untouched
            Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Possession, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(theirs).Origin);
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
            // ⭐ CE-1079 — the breech crosses too: the loaded round, its count, the LOADING bit (the whole struct is compared below)
            ammo.Set(1, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 2, MaxAmmo = 4 },
                     new Fdp.Toolkit.Combat.Components.AmmoLoadout { Count = 2, Ammo0 = 6006, Rounds0 = 1, Ammo1 = 6018, Rounds1 = 1, Loaded = 1, SwitchRemaining = 2f });
            Assert.Equal((6018L, 1, true), (ammo.Load1, ammo.LoadRounds1, ammo.IsBusy(1)));
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
