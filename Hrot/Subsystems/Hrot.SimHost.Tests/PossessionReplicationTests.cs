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
        /// <summary>
        /// ⭐⭐ <c>CE-11027</c> S12-1 (Q114 A, R-11011) — retyping the four possession fields from <c>NodeId</c> to <c>StationId</c> must NOT change the layout hash of the three events and the component
        /// (<c>SchemaValidator</c> refuses a recording whose recorded hash differs): the hashes below were computed at the base, BEFORE the retype, and are hard-coded.
        /// 🔴 Mutation: the hasher ignores <c>[LayoutHashAs]</c> ⇒ all four differ.
        /// ⭐ R-4301 (user, `2026-11-27`: no recording compatibility) — the control frame's hash is UPDATED when its layout changes, never routed around:
        /// devices S1d-3 dropped CE-1219's <c>Binoculars</c> byte (D-75) ⇒ <c>0x353FF5AEC55DF7B8</c> → <c>0x5F11007E565C7600</c>.
        /// </summary>
        [Fact]
        public void CE11027_TheRetypeKeepsTheRecordedLayoutHashes_OfTheThreeEventsAndTheComponent()
        {
            Assert.Equal(0xEDB181D40B5C0FA7UL, Fdp.Core.FlightRecorder.ComponentLayoutHasher.ComputeHash(typeof(PossessRequestEvent)));
            Assert.Equal(0x6A8A8992039E9D2FUL, Fdp.Core.FlightRecorder.ComponentLayoutHasher.ComputeHash(typeof(ReleasePossessionEvent)));
            Assert.Equal(0x5F11007E565C7600UL, Fdp.Core.FlightRecorder.ComponentLayoutHasher.ComputeHash(typeof(OperatorControlFrameEvent)));
            Assert.Equal(0x32AEB11D78EBE82BUL, Fdp.Core.FlightRecorder.ComponentLayoutHasher.ComputeHash(typeof(PossessedBy)));
        }

        /// <summary>
        /// ⭐ <c>CE-11027</c> S12-1 — a recording made BEFORE the retype plays: its manifest carries the hashes of the base (hard-coded in the rail above) and the sizes; the validator finds all four types by id and reports
        /// NO changed component and throws for no event. ⚠ No committed recording asset holds possession events, so this is the manifest form the brief allows (the hashes ARE what an old recording stores).
        /// 🔴 Mutation: the hasher ignores <c>[LayoutHashAs]</c> ⇒ the component is reported changed and the event check throws.
        /// </summary>
        [Fact]
        public void CE11027_AManifestRecordedAtTheOldType_StillValidates()
        {
            var meta = new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata
            {
                SchemaManifest = new System.Collections.Generic.Dictionary<int, Fdp.Core.FlightRecorder.ComponentSchemaInfo>
                {
                    [GlobalComponentIds.PossessedBy] = new() { Name = typeof(PossessedBy).FullName!, Size = Fdp.Core.TypeLayout.SizeOf(typeof(PossessedBy)), LayoutHash = 0x32AEB11D78EBE82BUL, IsManaged = false },
                },
                EventManifest = new System.Collections.Generic.Dictionary<int, Fdp.Core.FlightRecorder.ComponentSchemaInfo>
                {
                    [PossessionEventIds.PossessRequest] = new() { Name = typeof(PossessRequestEvent).FullName!, Size = Fdp.Core.TypeLayout.SizeOf(typeof(PossessRequestEvent)), LayoutHash = 0xEDB181D40B5C0FA7UL, IsManaged = false },
                    [PossessionEventIds.ReleasePossession] = new() { Name = typeof(ReleasePossessionEvent).FullName!, Size = Fdp.Core.TypeLayout.SizeOf(typeof(ReleasePossessionEvent)), LayoutHash = 0x6A8A8992039E9D2FUL, IsManaged = false },
                    [PossessionEventIds.ControlFrame] = new() { Name = typeof(OperatorControlFrameEvent).FullName!, Size = Fdp.Core.TypeLayout.SizeOf(typeof(OperatorControlFrameEvent)), LayoutHash = 0x5F11007E565C7600UL, IsManaged = false },
                },
            };
            Assert.Empty(Fdp.Core.FlightRecorder.SchemaValidator.Validate(meta));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-11027</c> S12-1 (Q114 A) — two stations on ONE node (<c>Of(node,1)</c>, <c>Of(node,2)</c>) possess two units; each control frame reaches only the unit its station holds, a frame for a unit another
        /// seat holds is dropped, and the component keeps the whole station (node AND seat). 🔴 Mutation: a control frame accepted for another station ⇒ the foreign frame lands.
        /// </summary>
        [Fact]
        public void CE11027_TwoStationsOnOneNode_PossessTwoUnits_AndEachFrameReachesOnlyItsOwnUnit()
        {
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(w);
            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            var possession = new PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);
            void Frame() { w.Bus.SwapBuffers(); possession.Execute(w, 0.016f); w.Bus.SwapBuffers(); ingress.Execute(w, 0.016f); }
            Entity Unit()
            {
                var u = w.CreateEntity();
                w.AddComponent(u, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                w.SetAuthority<Fdp.Toolkit.Behavior.Components.BehaviorState>(u, true);
                Fdp.Toolkit.Behavior.RootStateAccess.EnsureRootState(w, u);
                return u;
            }
            var one = Unit();
            var two = Unit();
            var node = (NodeId)7;
            var s1 = StationId.Of(node, 1);
            var s2 = StationId.Of(node, 2);

            w.Bus.Publish(new PossessRequestEvent { Unit = one, StationNodeId = s1, Mode = PossessionMode.Fps });
            w.Bus.Publish(new PossessRequestEvent { Unit = two, StationNodeId = s2, Mode = PossessionMode.Fps });
            Frame();
            Assert.Equal(s1, w.GetComponentRO<PossessedBy>(one).StationNodeId);
            Assert.Equal(s2, w.GetComponentRO<PossessedBy>(two).StationNodeId);
            Assert.Equal(node, w.GetComponentRO<PossessedBy>(one).StationNodeId.Node);

            w.Bus.Publish(new OperatorControlFrameEvent { Unit = one, StationNodeId = s1, Seq = 1, Forward = 1f });
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = two, StationNodeId = s2, Seq = 1, Forward = -1f });
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = one, StationNodeId = s2, Seq = 2, Forward = 0.5f });   // seat 2 sends a frame for seat 1's unit
            Frame();
            Assert.Equal(1f, w.GetComponentRO<OperatorInput>(one).Forward);
            Assert.Equal(1u, w.GetComponentRO<OperatorInput>(one).Seq);        // the foreign frame (Seq 2) did not land
            Assert.Equal(-1f, w.GetComponentRO<OperatorInput>(two).Forward);

            // the node's station 0 is neither seat: it cannot take a unit a seat holds, and a request for a free unit by seat 1 is a re-take of its own
            w.Bus.Publish(new PossessRequestEvent { Unit = one, StationNodeId = StationId.Of(node, 0), Mode = PossessionMode.Fps });
            Frame();
            Assert.Equal(s1, w.GetComponentRO<PossessedBy>(one).StationNodeId);

            // each seat can be released on its own
            w.Bus.Publish(new ReleasePossessionEvent { Unit = one, StationNodeId = s2 });   // not its holder: ignored
            Frame();
            Assert.True(w.HasComponent<PossessedBy>(one));
            w.Bus.Publish(new ReleasePossessionEvent { Unit = one, StationNodeId = s1 });
            Frame();
            Assert.False(w.HasComponent<PossessedBy>(one));
            Assert.True(w.HasComponent<PossessedBy>(two));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-11027</c> S12-1 (Q114 A′) — <c>NodeDeparted(node)</c> releases the units of EVERY station on the node (<c>Of(node,0..2)</c>), a unit of another node is kept.
        /// 🔴 Mutation: the departure compares the whole station with the node id ⇒ only station 0's unit is released.
        /// </summary>
        [Fact]
        public void CE11027_ADepartedNode_ReleasesEveryStationOnIt()
        {
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(w);
            w.Bus.Register<Fdp.Toolkit.Replication.Messages.NodeDeparted>();
            var possession = new PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);
            Entity Held(StationId station)
            {
                var u = w.CreateEntity();
                w.AddComponent(u, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                w.SetAuthority<Fdp.Toolkit.Behavior.Components.BehaviorState>(u, true);
                w.AddComponent(u, new PossessedBy { StationNodeId = station, Mode = PossessionMode.Fps, Epoch = 1 });
                return u;
            }
            var node = (NodeId)7;
            var mine = new[] { Held(StationId.Of(node, 0)), Held(StationId.Of(node, 1)), Held(StationId.Of(node, 2)) };
            var other = Held(StationId.Of((NodeId)8, 1));
            w.Bus.Publish(new Fdp.Toolkit.Replication.Messages.NodeDeparted { NodeId = node });
            w.Bus.SwapBuffers();
            possession.Execute(w, 0.016f);
            foreach (var u in mine) Assert.False(w.HasComponent<PossessedBy>(u));
            Assert.True(w.HasComponent<PossessedBy>(other));
        }

        private static EntityRepository PossessionWorld(out PossessionSystem possession, out System.Func<Entity> newUnit)
        {
            var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(w);
            possession = new PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl);
            newUnit = () =>
            {
                var u = w.CreateEntity();
                w.AddComponent(u, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                w.SetAuthority<Fdp.Toolkit.Behavior.Components.BehaviorState>(u, true);
                return u;
            };
            return w;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-11027</c> S12-3 (Q114 E) — a station that RIDES a man and DRIVES a vehicle leaves (<c>ReleaseStationEvent</c>): BOTH units are free, another station's unit is kept; a station that holds nothing is a no-op.
        /// 🔴 Mutation: <c>ReleaseStation</c> releases only the first unit ⇒ the second stays held.
        /// </summary>
        [Fact]
        public void CE11027_AStationThatRidesAndDrives_Leaves_BothUnitsAreFree_AndANoOpForAStationThatHoldsNothing()
        {
            using var w = PossessionWorld(out var possession, out var newUnit);
            w.Bus.Register<ReleaseStationEvent>();
            var seat = StationId.Of((NodeId)7, 1);
            Entity Held(StationId st, PossessionMode mode) { var u = newUnit(); w.AddComponent(u, new PossessedBy { StationNodeId = st, Mode = mode, Epoch = 1 }); return u; }
            var man = Held(seat, PossessionMode.Riding);
            var vehicle = Held(seat, PossessionMode.Fps);
            var other = Held(StationId.Of((NodeId)7, 2), PossessionMode.Fps);
            void Frame() { w.Bus.SwapBuffers(); possession.Execute(w, 0.016f); }

            w.Bus.Publish(new ReleaseStationEvent { StationNodeId = StationId.Of((NodeId)9, 0) });   // holds nothing
            Frame();
            Assert.True(w.HasComponent<PossessedBy>(man) && w.HasComponent<PossessedBy>(vehicle) && w.HasComponent<PossessedBy>(other));

            w.Bus.Publish(new ReleaseStationEvent { StationNodeId = seat });
            Frame();
            Assert.False(w.HasComponent<PossessedBy>(man));
            Assert.False(w.HasComponent<PossessedBy>(vehicle));
            Assert.True(w.HasComponent<PossessedBy>(other));          // the other seat of the node keeps its unit
        }

        /// <summary>
        /// ⭐⭐ <c>CE-11027</c> S12-3 (Q114 G) — a KILLED rider (held in Riding) frees his station's VEHICLE as well as his own body. A killed unit held in Fps frees only itself. 🔴 Mutation: the killed-rider sweep releases only the body ⇒ the vehicle stays held.
        /// </summary>
        [Fact]
        public void CE11027_AKilledRider_FreesTheVehicleHisStationDrove()
        {
            using var w = PossessionWorld(out var possession, out var newUnit);
            var seat = StationId.Of((NodeId)7, 1);
            Entity Held(StationId st, PossessionMode mode, float health) { var u = newUnit(); w.AddComponent(u, new PossessedBy { StationNodeId = st, Mode = mode, Epoch = 1 }); w.AddComponent(u, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f }); return u; }
            var rider = Held(seat, PossessionMode.Riding, 100f);
            var vehicle = Held(seat, PossessionMode.Fps, 100f);
            var otherRider = Held(StationId.Of((NodeId)7, 2), PossessionMode.Riding, 100f);
            var otherVehicle = Held(StationId.Of((NodeId)7, 2), PossessionMode.Fps, 100f);
            void Frame() { w.Bus.SwapBuffers(); possession.Execute(w, 0.016f); }

            w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(rider).Current = 0f;
            Frame();
            Assert.False(w.HasComponent<PossessedBy>(rider));
            Assert.False(w.HasComponent<PossessedBy>(vehicle));        // the vehicle the dead man drove
            Assert.True(w.HasComponent<PossessedBy>(otherRider)); Assert.True(w.HasComponent<PossessedBy>(otherVehicle));   // the other seat is untouched

            w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(otherVehicle).Current = 0f;       // a killed unit held in Fps: only itself
            Frame();
            Assert.False(w.HasComponent<PossessedBy>(otherVehicle));
            Assert.True(w.HasComponent<PossessedBy>(otherRider));
        }

        /// <summary>
        /// ⭐ <c>CE-11027</c> S12-3 — the NEW event is a recorder citizen: a recording that contains <c>ReleaseStationEvent</c> plays back with the station (index and node) intact, a manifest that already names it validates, and an OLD
        /// manifest (without it) still validates — a manifest only GAINS an event. Its layout hash is hashed under the id <c>5203</c>.
        /// </summary>
        [Fact]
        public void CE11027_TheNewEvent_RecordsAndPlaysBack_AndOldManifestsStillValidate()
        {
            var seat = StationId.Of((NodeId)7, 2);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ce11027_" + System.Guid.NewGuid().ToString("N") + ".fdp");
            try
            {
                using (var fs = System.IO.File.Create(path))
                using (var writer = new System.IO.BinaryWriter(fs))
                using (var repo = new EntityRepository())
                using (var bus = new FdpEventBus())
                {
                    writer.Write((uint)1); writer.Write((ulong)0);
                    bus.Publish(new ReleaseStationEvent { StationNodeId = seat });
                    repo.Tick();
                    new Fdp.Core.FlightRecorder.RecorderSystem().RecordKeyframe(repo, writer, 0L, bus);
                }
                using (var fs = System.IO.File.OpenRead(path))
                using (var reader = new System.IO.BinaryReader(fs))
                using (var repo = new EntityRepository())
                using (var bus = new FdpEventBus())
                {
                    reader.ReadUInt32(); reader.ReadUInt64();
                    new Fdp.Core.FlightRecorder.PlaybackSystem().ApplyFrame(repo, reader, bus);
                    var got = Assert.Single(bus.Read<ReleaseStationEvent>().ToArray());
                    Assert.Equal(seat, got.StationNodeId);
                    Assert.Equal(2, got.StationNodeId.Index);
                }
            }
            finally { try { System.IO.File.Delete(path); } catch { } }

            var newType = typeof(ReleaseStationEvent);
            var meta = new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata
            {
                EventManifest = new System.Collections.Generic.Dictionary<int, Fdp.Core.FlightRecorder.ComponentSchemaInfo>
                {
                    [PossessionEventIds.ReleaseStation] = new() { Name = newType.FullName!, Size = Fdp.Core.TypeLayout.SizeOf(newType), LayoutHash = Fdp.Core.FlightRecorder.ComponentLayoutHasher.ComputeHash(newType), IsManaged = false },
                    [PossessionEventIds.PossessRequest] = new() { Name = typeof(PossessRequestEvent).FullName!, Size = Fdp.Core.TypeLayout.SizeOf(typeof(PossessRequestEvent)), LayoutHash = 0xEDB181D40B5C0FA7UL, IsManaged = false },
                },
            };
            Assert.Empty(Fdp.Core.FlightRecorder.SchemaValidator.Validate(meta));
            Assert.Equal(5203, PossessionEventIds.ReleaseStation);
            // (the old-manifest half is CE11027_AManifestRecordedAtTheOldType_StillValidates: no entry for the new event ⇒ nothing to check)
        }

        private static (EntityRepository World, Entity Unit, NetworkEntityMap Map) Node(int netId)
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

            w.Bus.Publish(new PossessRequestEvent { Unit = mine, StationNodeId = (Fdp.Core.StationId)(7), Mode = PossessionMode.Fps });
            w.Bus.Publish(new PossessRequestEvent { Unit = theirs, StationNodeId = (Fdp.Core.StationId)(9), Mode = PossessionMode.Fps });
            Frame();
            foreach (var u in new[] { mine, theirs })
                Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Possession, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(u).Origin);

            w.Bus.Publish(new Fdp.Toolkit.Replication.Messages.NodeDeparted { NodeId = (Fdp.Core.NodeId)(7) });
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

            w.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Mode = PossessionMode.Fps });
            Frame();
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Seq = 1, Forward = 1f });
            Frame();
            Assert.Equal(1u, w.GetComponentRO<OperatorInput>(unit).Seq);                       // Fps: frames arrive

            w.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Mode = PossessionMode.Riding });
            Frame();
            Assert.Equal(PossessionMode.Riding, w.GetComponentRO<PossessedBy>(unit).Mode);
            var riding = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Possession, riding.Origin);   // the task stays paused
            Assert.Equal(op, riding.ActiveBehaviorHash);                                         // the operator behaviour kept running
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Seq = 2, Forward = 1f, UseSeq = 1 });
            Frame();
            Assert.False(w.HasComponent<OperatorInput>(unit));                                   // a rider takes no frames

            w.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Mode = PossessionMode.Fps });
            Frame();
            Assert.Equal(op, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash);
            w.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Seq = 3, Forward = 1f });
            Frame();
            Assert.Equal(3u, w.GetComponentRO<OperatorInput>(unit).Seq);                       // Fps again: frames route

            w.Bus.Publish(new ReleasePossessionEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7) });
            Frame();
            Assert.Equal(idle, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash);   // the task resumes
        }

        [Fact]
        public void CE3173_ManualDrive_TheAxesOfTheBrain_AreAppliedOnTheMuscle()
        {
            var drive = new ManualDrive { Forward = 1f, Lateral = -1f, Steer = 0.5f, Yaw = 2f, Gait = 1, Seq = 9 };
            var sample = EntityManualDriveEgressTranslator.ToSample(77, drive);
            Assert.Equal(77L, sample.EntityId);

            var (muscle, unit, map) = Node(77);
            using (muscle)
            {
                var ingress = new EntityManualDriveIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, muscle);
                cmd.Playback(muscle);
                Assert.Equal(drive, muscle.GetComponentRO<ManualDrive>(unit));

                // the brain's let-go (axes zeroed, sequence restarted) still lands: the latest sample wins
                var stop = EntityManualDriveEgressTranslator.ToSample(77, new ManualDrive { Yaw = 2f });
                ingress.Apply(in stop, cmd, muscle);
                cmd.Playback(muscle);
                Assert.Equal(0f, muscle.GetComponentRO<ManualDrive>(unit).Forward);
            }
        }

        [Fact]
        public void CE3172_PossessedBy_IsSetOnEveryNode_AndRemovedByOneReleaseSample()
        {
            var (brain, brainsUnit, _) = Node(55);
            var (station, stationsUnit, stationMap) = Node(55);
            using (brain)
            using (station)
            {
                var egress = new EntityPossessedByEgressTranslator(participant: null);
                var ingress = new EntityPossessedByIngressTranslator(participant: null, stationMap);
                using var cmd = new EntityCommandBuffer();

                brain.AddComponent(brainsUnit, new PossessedBy { StationNodeId = (Fdp.Core.StationId)(300), Mode = PossessionMode.Fps, Epoch = 1 });
                var held = Assert.Single(egress.Scan(brain));
                Assert.Equal((byte)1, held.Held);
                Assert.Empty(egress.Scan(brain));   // unchanged ⇒ nothing sent
                ingress.Apply(in held, cmd, station);
                cmd.Playback(station);
                Assert.Equal(300L, station.GetComponentRO<PossessedBy>(stationsUnit).StationNodeId);

                brain.SetComponent(brainsUnit, new PossessedBy { StationNodeId = (Fdp.Core.StationId)(400), Mode = PossessionMode.Fps, Epoch = 2 });   // a take-over
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
            var sample = EntityOperatorUseStatusEgressTranslator.ToSample(66, verdict);
            var (station, unit, map) = Node(66);
            using (station)
            {
                station.RegisterComponent<OperatorUseStatus>();
                var ingress = new EntityOperatorUseStatusIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, station);
                var stranger = EntityOperatorUseStatusEgressTranslator.ToSample(99, verdict);   // nobody here has id 99
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
            var sample = EntityWeaponAmmoEgressTranslator.ToSample(66, ammo);
            var (station, unit, map) = Node(66);
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

            public void Request(Entity unit, int station, PossessionMode mode = PossessionMode.Fps)
                => W.Bus.Publish(new PossessRequestEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(station), Mode = mode });

            public int? Holder(Entity unit) => PossessionSystem.HolderOf(W, unit);
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
            n.W.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(9), Seq = 1, Forward = -1f });
            n.W.Bus.Publish(new OperatorControlFrameEvent { Unit = unit, StationNodeId = (Fdp.Core.StationId)(7), Seq = 1, Forward = 1f });
            n.Frame();

            Assert.Equal(7, n.Holder(unit));                                                    // still his
            Assert.Equal(1u, n.W.GetComponentRO<PossessedBy>(unit).Epoch);                       // never re-taken, so never bumped
            Assert.Equal(PossessionMode.Fps, n.W.GetComponentRO<PossessedBy>(unit).Mode);
            Assert.Equal(op, n.W.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash);
            Assert.Equal(1f, n.W.GetComponentRO<OperatorInput>(unit).Forward);                   // the holder's frame, not the intruder's
            Assert.Equal(2L, n.Possession.Refused);

            n.Request(unit, station: 7, PossessionMode.Riding);                                  // the HOLDER may still change its own mode
            n.Frame();
            Assert.Equal((7, PossessionMode.Riding), (n.Holder(unit), n.W.GetComponentRO<PossessedBy>(unit).Mode));
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
            Assert.Equal((7, 8), (n.Holder(killed), n.Holder(departed)));

            n.Kill(killed);
            n.W.Bus.Publish(new Fdp.Toolkit.Replication.Messages.NodeDeparted { NodeId = (Fdp.Core.NodeId)(8) });
            n.Request(departed, station: 9);
            n.Request(killed, station: 9);                                                      // a killed unit itself is nobody's to take
            n.Frame();

            Assert.Equal(9, n.Holder(departed));                                               // the departed holder let go, the next took it
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
            Entity Rider(ForceId side, int station, Entity truck)
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
            Assert.Equal(9, n.Holder(withFriend));                                              // a blue player sits in this one: fine

            n.Request(withEnemy, station: 5);                                                    // station 5 holds nothing: no side to be refused for
            n.Frame();
            Assert.Equal(5, n.Holder(withEnemy));
        }

        /// <summary>
        /// ⭐⭐ Player Join D-8 (<c>DESIGN_Player_Join.md</c> §9) — a unit RESERVED for a station (its player slot, <see cref="StationReservations"/>) is
        /// refused to every other station and granted to that one; and a station's side comes from its reservation before it holds anything,
        /// so a blue player with only a slot is already refused an enemy-crewed vehicle. With no reservation the rule is R-4096's (the other rails).
        /// 🔴 Mutation: <c>ForPossess</c> without the reservation test ⇒ station 9 takes the unit; <c>TrySideOf</c> without the slot ⇒ station 4 boards.
        /// </summary>
        [Fact]
        public void D8_AReservedUnit_IsOnlyItsStations_AndTheSlotNamesTheStationsSide()
        {
            using var n = new BrainNode();
            if (!n.W.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) n.W.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var unit = n.Unit();
            n.W.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(501));
            StationReservations.Of(n.W).Set(7, ForceId.Friend, 501);

            n.Request(unit, station: 9);
            n.Frame();
            Assert.Null(n.Holder(unit));                                                         // reserved for 7: refused to 9
            Assert.Equal(ClaimRefusal.ReservedForAnother, StationClaims.ForPossess(n.W, unit, 9, out int holder));
            Assert.Equal(7, holder);

            n.Request(unit, station: 7);
            n.Frame();
            Assert.Equal(7, n.Holder(unit));                                                     // its own station takes it

            // the side from the slot: station 4 holds nothing, but its slot says Friend — an enemy player's vehicle is refused
            var truck = n.Unit(ForceId.Neutral);
            n.W.AddComponent(truck, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            n.W.AddComponent(truck, new PassengerBuffer());
            var enemy = n.Unit(ForceId.Hostile);
            n.W.AddComponent(enemy, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            n.Request(enemy, station: 6);
            n.Frame();
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(n.W, enemy, truck));
            StationReservations.Of(n.W).Set(4, ForceId.Friend, 0);
            Assert.Equal(ClaimRefusal.OtherSide, StationClaims.ForPossess(n.W, truck, 4, out _));

            StationReservations.Of(n.W).Remove(4);
            Assert.Equal(ClaimRefusal.None, StationClaims.ForPossess(n.W, truck, 4, out _));      // no slot, no unit: no side (R-4096)
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
