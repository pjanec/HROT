using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
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

        /// <summary>
        /// ⭐⭐ <c>CE-1156</c> (docs/DESIGN_Entity_Control.md §8h U-4, O-2) — a unit held in <see cref="PossessionMode.Riding"/> (its human
        /// is driving a vehicle) keeps its task paused under origin Possession but takes NO control frame; switching Fps ↔ Riding on the
        /// same station is a take-over, so the operator behaviour keeps running and frames route again in Fps. Drives the real
        /// <see cref="PossessionSystem"/> and <see cref="Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem"/>.
        /// </summary>
        [Fact]
        public void CE1156_ARidingUnit_StaysPaused_TakesNoFrames_AndFpsRidingIsATakeOver()
        {
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(w);
            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            var possession = new PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);
            void Frame()
            {
                w.Bus.SwapBuffers(); possession.Execute(w, 0.016f);
                w.Bus.SwapBuffers(); ingress.Execute(w, 0.016f);
            }
            var unit = w.CreateEntity();
            w.AddComponent(unit, new Fdp.Toolkit.Behavior.Components.BehaviorState());
            w.SetAuthority<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit, true);
            Fdp.Toolkit.Behavior.RootStateAccess.EnsureRootState(w, unit);
            w.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
            {
                Entity = unit, BehaviorName = Hrot.Map.Definitions.Behavior.BehaviorNames.Idle, JsonParams = "{}",
                Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,
            });
            w.Bus.SwapBuffers(); ingress.Execute(w, 0.016f);
            int op = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);
            int idle = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.Idle);

            w.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = 7, Mode = PossessionMode.Fps });
            Frame();
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = 7, Seq = 1, Forward = 1f });
            Frame();
            Assert.Equal(1u, w.GetComponentRO<OperatorInput>(unit).Seq);                       // Fps: frames arrive

            w.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = 7, Mode = PossessionMode.Riding });
            Frame();
            Assert.Equal(PossessionMode.Riding, w.GetComponentRO<PossessedBy>(unit).Mode);
            var riding = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Possession, riding.Origin);   // the task stays paused
            Assert.Equal(op, riding.ActiveBehaviorHash);                                         // the operator behaviour kept running
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = 7, Seq = 2, Forward = 1f, UseSeq = 1 });
            Frame();
            Assert.False(w.HasComponent<OperatorInput>(unit));                                   // a rider takes no frames

            w.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = 7, Mode = PossessionMode.Fps });
            Frame();
            Assert.Equal(op, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash);
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = 7, Seq = 3, Forward = 1f });
            Frame();
            Assert.Equal(3u, w.GetComponentRO<OperatorInput>(unit).Seq);                       // Fps again: frames route

            w.Bus.Publish(new ReleasePossessionEvent { Unit = unit, StationNodeId = 7 });
            Frame();
            Assert.Equal(idle, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash);   // the task resumes
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

        /// <summary>
        /// ⭐ <c>CE-1153</c> (docs/DESIGN_Entity_Control.md §8h.6) — the brain's verdict on a use press crosses to the station's node as
        /// STATE: <see cref="OperatorUseStatus"/> written on the brain, published by the egress (it owns <c>dtOperatorUseStatus</c>) and
        /// applied whole on the other node. An unknown unit is dropped.
        /// </summary>
        [Fact]
        public void CE1153_OperatorUseStatus_TheBrainsVerdict_ReachesTheStation()
        {
            var verdict = new OperatorUseStatus
            {
                UseSeq = 7, ActionId = Fdp.Toolkit.Behavior.BehaviorConstants.ActionIdOpenDoor,
                Outcome = UseOutcome.Refused, Reason = UseReason.Locked,
            };
            var sample = EntityOperatorUseStatusEgressTranslator.ToSample(66L, verdict);
            var (station, unit, map) = Node(66L);
            using (station)
            {
                station.RegisterComponent<OperatorUseStatus>();
                var ingress = new EntityOperatorUseStatusIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, station);
                var stranger = EntityOperatorUseStatusEgressTranslator.ToSample(99L, verdict);   // nobody here has id 99
                ingress.Apply(in stranger, cmd, station);
                cmd.Playback(station);
                Assert.Equal(verdict, station.GetComponentRO<OperatorUseStatus>(unit));
                Assert.Equal(1, ingress.DroppedSampleCount);
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
            ammo.LaidDown = 0b0001;   // ⭐ CE-3232 (B-10) — the laid-down mounts cross too
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

        // ── R-4096 (docs/DESIGN_Entity_Control.md C-3') — the brain never lets one player take what another LIVE player holds ──────

        /// <summary>One brain node with the REAL possession, ingress and embarkation systems; each unit has its brain here.</summary>
        private sealed class BrainNode : System.IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly PossessionSystem Possession = new(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);
            private readonly Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem _ingress;
            private readonly Fdp.Toolkit.Behavior.Systems.EmbarkationSystem _embark = new();

            public BrainNode()
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(W);
                if (!W.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()) W.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
                if (!W.IsComponentTypeRegistered<Fdp.Core.EntityInfo>()) W.RegisterComponent<Fdp.Core.EntityInfo>();
                W.Bus.Register<Fdp.Toolkit.Replication.Messages.NodeDeparted>();
                var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
                Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
                _ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            }

            public void Frame()
            {
                W.Bus.SwapBuffers(); Possession.Execute(W, 0.016f);
                W.Bus.SwapBuffers(); _ingress.Execute(W, 0.016f); _embark.Execute(W, 0.016f);   // one readable window: what possession just published
            }

            public Entity Unit(ForceId side = ForceId.Friend, float health = 100f)
            {
                var u = W.CreateEntity();
                W.AddComponent(u, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                W.SetAuthority<Fdp.Toolkit.Behavior.Components.BehaviorState>(u, true);   // this node runs its brain
                Fdp.Toolkit.Behavior.RootStateAccess.EnsureRootState(W, u);
                W.AddComponent(u, new Fdp.Core.EntityInfo { ForceId = side });
                W.AddComponent(u, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
                W.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
                {
                    Entity = u, BehaviorName = Hrot.Map.Definitions.Behavior.BehaviorNames.Idle, JsonParams = "{}",
                    Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,
                });
                W.Bus.SwapBuffers(); _ingress.Execute(W, 0.016f);
                return u;
            }

            public void Request(Entity unit, long station, PossessionMode mode = PossessionMode.Fps)
                => W.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = station, Mode = mode });

            public long? Holder(Entity unit) => PossessionSystem.HolderOf(W, unit);
            public void Kill(Entity unit) => W.SetComponent(unit, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });

            public void Dispose() => W.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>R-4096</c> S1 — a second station's request for a unit the first holds is REFUSED, never a take-over: the holder keeps the
        /// unit (same <c>Epoch</c>, still the operator behaviour), his frames still arrive and the intruder's are ignored; the refusal is
        /// counted. ✅ Red-proof: <c>PossessionSystem</c> without the <c>StationClaims.ForPossess</c> guard (the old C-3) ⇒ the unit is
        /// station 9's and the first assert fails.
        /// </summary>
        [Fact]
        public void R4096_ASecondStationsRequest_IsRefused_TheHolderKeepsTheUnitAndHisFrames()
        {
            using var n = new BrainNode();
            var unit = n.Unit();
            int op = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);

            n.Request(unit, station: 7);
            n.Frame();
            var held = n.W.GetComponentRO<PossessedBy>(unit);
            Assert.Equal((7L, 1u), (held.StationNodeId, held.Epoch));

            n.Request(unit, station: 9);
            n.Request(unit, station: 9, PossessionMode.Riding);
            n.W.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = 9, Seq = 1, Forward = -1f });
            n.W.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = 7, Seq = 1, Forward = 1f });
            n.Frame();

            Assert.Equal(7L, n.Holder(unit));                                                    // still his
            Assert.Equal(1u, n.W.GetComponentRO<PossessedBy>(unit).Epoch);                       // never re-taken, so never bumped
            Assert.Equal(PossessionMode.Fps, n.W.GetComponentRO<PossessedBy>(unit).Mode);
            Assert.Equal(op, n.W.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash);
            Assert.Equal(1f, n.W.GetComponentRO<OperatorInput>(unit).Forward);                   // the holder's frame, not the intruder's
            Assert.Equal(2L, n.Possession.Refused);

            n.Request(unit, station: 7, PossessionMode.Riding);                                  // the HOLDER may still change its own mode
            n.Frame();
            Assert.Equal((7L, PossessionMode.Riding), (n.Holder(unit), n.W.GetComponentRO<PossessedBy>(unit).Mode));
        }

        /// <summary>
        /// ⭐ <c>R-4096</c> S2 — a unit whose holder was KILLED, or whose holder's node DEPARTED, is free again: another station's request in
        /// the SAME tick gets it (the releases come before the requests are read). ✅ Red-proof: the two sweeps back after the request loop
        /// (the old order) ⇒ the departed holder's unit is refused in that tick.
        /// </summary>
        [Fact]
        public void R4096_AKilledHolder_OrADepartedOne_FreesTheUnit_TheNextStationTakesItAtOnce()
        {
            using var n = new BrainNode();
            var killed = n.Unit();
            var departed = n.Unit();
            n.Request(killed, station: 7);
            n.Request(departed, station: 8);
            n.Frame();
            Assert.Equal((7L, 8L), (n.Holder(killed), n.Holder(departed)));

            n.Kill(killed);
            n.W.Bus.Publish(new Fdp.Toolkit.Replication.Messages.NodeDeparted { NodeId = 8 });
            n.Request(departed, station: 9);
            n.Request(killed, station: 9);                                                      // a killed unit itself is nobody's to take
            n.Frame();

            Assert.Equal(9L, n.Holder(departed));                                               // the departed holder let go, the next took it
            Assert.Null(n.Holder(killed));                                                      // released on death, and not possessable dead
        }

        /// <summary>
        /// ⭐ <c>R-4096</c> — a vehicle that carries a player of ANOTHER side is not for this station (the arbiter's side rule, for the race
        /// the station's own answer cannot see); one carrying a player of its OWN side is possessed as ever. A station holding nothing has no
        /// side, so it is not refused for it. ✅ Red-proof: the side comparison dropped in <c>StationClaims.ForPossess</c>.
        /// </summary>
        [Fact]
        public void R4096_AVehicleCarryingAnEnemyPlayer_IsRefused_OneCarryingAFriendIsNot_AStationWithNoUnitHasNoSide()
        {
            using var n = new BrainNode();
            Entity Truck(Vector3 at)
            {
                var v = n.Unit(ForceId.Neutral);
                n.W.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                n.W.AddComponent(v, new PassengerBuffer());
                return v;
            }
            Entity Rider(ForceId side, long station, Entity truck)
            {
                var r = n.Unit(side);
                n.W.AddComponent(r, new SimTransform { Position = n.W.GetComponentRO<SimTransform>(truck).Position, Rotation = Quaternion.Identity });
                n.Request(r, station);
                n.Frame();
                Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(n.W, r, truck));
                return r;
            }
            var withEnemy = Truck(new Vector3(0, 0, 0));
            Rider(ForceId.Hostile, 7, withEnemy);
            var withFriend = Truck(new Vector3(0, 20, 0));
            Rider(ForceId.Friend, 8, withFriend);

            var mine = n.Unit(ForceId.Friend);
            n.Request(mine, station: 9);                                                         // station 9 is a blue player
            n.Frame();
            n.Request(withEnemy, station: 9);
            n.Request(withFriend, station: 9);
            n.Frame();
            Assert.Null(n.Holder(withEnemy));                                                    // refused: an enemy player sits in it
            Assert.Equal(9L, n.Holder(withFriend));                                              // a blue player sits in this one: fine

            n.Request(withEnemy, station: 5);                                                    // station 5 holds nothing: no side to be refused for
            n.Frame();
            Assert.Equal(5L, n.Holder(withEnemy));
        }

        /// <summary>
        /// ⭐⭐ <c>R-4096</c> S5 — a KILLED player aboard a vehicle frees his seat: the brain releases him and asks the vehicle's owner to put
        /// him out (<c>DisembarkEntityCommand</c>), so the next player can take it. A dead man NO player held (an AI crewman) keeps his seat as
        /// ever — the body stays aboard. ✅ Red-proof: <c>FreeSeatOf</c> not called ⇒ the dead player stays in the buffer.
        /// </summary>
        [Fact]
        public void R4096_AKilledPlayersSeat_IsFreed_ADeadAiMansIsNot()
        {
            using var n = new BrainNode();
            var truck = n.Unit(ForceId.Neutral);
            n.W.AddComponent(truck, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            n.W.AddComponent(truck, new PassengerBuffer());
            var player = n.Unit();
            var ai = n.Unit();
            foreach (var m in new[] { player, ai }) n.W.AddComponent(m, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            n.Request(player, station: 7);
            n.Frame();
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(n.W, player, truck));   // seat 0
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(n.W, ai, truck));       // seat 1
            Assert.Equal(2, n.W.GetComponentRO<PassengerBuffer>(truck).Count);

            n.Kill(player);
            n.Kill(ai);
            n.Frame();

            Assert.Null(n.Holder(player));                                                       // released on death, as before
            Assert.False(n.W.HasComponent<IsEmbarkedTag>(player));                               // and out of the vehicle
            Assert.Equal(1, n.W.GetComponentRO<PassengerBuffer>(truck).Count);
            Assert.True(n.W.GetComponentRO<PassengerBuffer>(truck).Passengers[0].IsNull);        // seat 0 is free again
            Assert.Equal(0, Embarkation.FreeSeat(n.W, truck));
            Assert.True(n.W.HasComponent<IsEmbarkedTag>(ai));                                    // the dead AI man keeps his seat
        }
    }
}
