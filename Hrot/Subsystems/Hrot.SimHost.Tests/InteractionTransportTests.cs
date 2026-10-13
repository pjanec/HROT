using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Interactions;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ R-221 (📄 docs/DESIGN_Entity_Interactions.md) — the ONE interaction transport: a kind's typed FDP event becomes one
    /// <see cref="EntityInteractionRequest"/> whose payload is that kind's union case, and comes back on the owner as the same typed
    /// event marked remote. The loop guard (a remote event, or one whose target this node owns, is never sent) and an unknown kind
    /// (dropped) are the transport's own rules, independent of any kind.
    /// </summary>
    public sealed class InteractionTransportTests
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<DoorState>();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterEvent<DoorCommandEvent>();
            return w;
        }

        [Fact]
        public void R221_ADoorCommand_CrossesAsItsUnionCase_AndArrivesAsTheSameTypedEvent_MarkedRemote()
        {
            using var sender = World();
            var map = new NetworkEntityMap();
            var door = sender.CreateEntity();
            sender.AddComponent(door, new DoorState { State = TerrainDoorState.Closed });
            sender.AddComponent(door, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));   // node 2's door
            var actor = sender.CreateEntity();
            map.Register(700L, door);
            map.Register(42L, actor);
            var writer = new CapturingWriter<EntityInteractionRequest>();
            var egress = new InteractionEgressTranslator(writer, map);

            sender.Bus.Publish(new DoorCommandEvent { Door = door, Verb = DoorVerb.Lock, Actor = actor });
            sender.Bus.SwapBuffers();
            egress.ScanAndPublish(sender);

            var sample = Assert.Single(writer.Written);
            Assert.Equal(700L, sample.TargetId);
            Assert.Equal(42L, sample.ActorId);
            Assert.Equal(EInteractionKind.Door, sample.Payload.Kind);
            Assert.Equal((byte)DoorVerb.Lock, sample.Payload.Door.Verb);

            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var ownersDoor = owner.CreateEntity();
            var ownersActor = owner.CreateEntity();
            ownerMap.Register(700L, ownersDoor);
            ownerMap.Register(42L, ownersActor);
            var ingress = new InteractionIngressTranslator(participant: null, ownerMap);
            using var cmd = new EntityCommandBuffer();
            ingress.ProcessSample(in sample, cmd);
            cmd.Playback(owner);
            owner.Bus.SwapBuffers();

            var evt = Assert.Single(owner.Bus.Read<DoorCommandEvent>().ToArray());
            Assert.Equal(ownersDoor, evt.Door);
            Assert.Equal(ownersActor, evt.Actor);
            Assert.Equal(DoorVerb.Lock, evt.Verb);
            Assert.True(evt.IsRemote);
        }

        [Fact]
        public void R221_TheLoopGuard_ARemoteEvent_OrOneWhoseTargetIsOwnedHere_IsNeverSent()
        {
            using var w = World();
            var map = new NetworkEntityMap();
            var mine = w.CreateEntity();                                       // no authority component: owned here
            w.AddComponent(mine, new DoorState { State = TerrainDoorState.Closed });
            var theirs = w.CreateEntity();
            w.AddComponent(theirs, new DoorState { State = TerrainDoorState.Closed });
            w.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            map.Register(1L, mine); map.Register(2L, theirs);
            var writer = new CapturingWriter<EntityInteractionRequest>();
            var egress = new InteractionEgressTranslator(writer, map);

            w.Bus.Publish(new DoorCommandEvent { Door = mine, Verb = DoorVerb.Open });                      // owned ⇒ applied here
            w.Bus.Publish(new DoorCommandEvent { Door = theirs, Verb = DoorVerb.Open, IsRemote = true });   // came off the wire
            w.Bus.SwapBuffers();
            egress.ScanAndPublish(w);

            Assert.Empty(writer.Written);
        }

        /// <summary>
        /// ⭐ CE-3172 S4e (docs/DESIGN_Entity_Control.md §8c) — possession crosses as three union cases: a station that does not run the
        /// unit's brain sends its request, frame and release; the brain owner receives the same typed events, marked remote, with the
        /// station's node id and every control axis intact.
        /// </summary>
        [Fact]
        public void CE3172_PossessControlRelease_CrossAsTheirUnionCases_AndArriveAsTheSameEvents_MarkedRemote()
        {
            using var station = new EntityRepository();
            station.RegisterEvent<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>();
            station.RegisterEvent<Fdp.Toolkit.Behavior.Possession.ReleasePossessionEvent>();
            station.RegisterEvent<Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent>();
            var map = new NetworkEntityMap();
            var unit = station.CreateEntity();   // no BehaviorState here: the brain runs elsewhere
            map.Register(900L, unit);
            var writer = new CapturingWriter<EntityInteractionRequest>();
            var egress = new InteractionEgressTranslator(writer, map);

            station.Bus.Publish(new Fdp.Toolkit.Behavior.Possession.PossessRequestEvent
                { Unit = unit, StationNodeId = 300, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps });
            station.Bus.Publish(new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent
            {
                Unit = unit, StationNodeId = 300, Seq = 7, Forward = 1f, Lateral = -0.5f, Steer = 0.25f, Yaw = 1.5f, Gait = 1,
                Trigger = 1, Mount = 2, AimPoint = new System.Numerics.Vector3(10f, 20f, 3f),
            });
            station.Bus.Publish(new Fdp.Toolkit.Behavior.Possession.ReleasePossessionEvent { Unit = unit, StationNodeId = 300 });
            station.Bus.SwapBuffers();
            egress.ScanAndPublish(station);

            Assert.Equal(3, writer.Written.Count);
            Assert.All(writer.Written, w => Assert.Equal(900L, w.TargetId));
            Assert.Contains(writer.Written, w => w.Payload.Kind == EInteractionKind.Possess && w.Payload.Possess.StationNodeId == 300);
            Assert.Contains(writer.Written, w => w.Payload.Kind == EInteractionKind.ControlFrame && w.Payload.ControlFrame.Seq == 7);
            Assert.Contains(writer.Written, w => w.Payload.Kind == EInteractionKind.ReleasePossession);

            using var brain = new EntityRepository();
            brain.RegisterEvent<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>();
            brain.RegisterEvent<Fdp.Toolkit.Behavior.Possession.ReleasePossessionEvent>();
            brain.RegisterEvent<Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent>();
            var brainMap = new NetworkEntityMap();
            var brainsUnit = brain.CreateEntity();
            brainMap.Register(900L, brainsUnit);
            var ingress = new InteractionIngressTranslator(participant: null, brainMap);
            using var cmd = new EntityCommandBuffer();
            foreach (var sample in writer.Written) ingress.ProcessSample(in sample, cmd);
            cmd.Playback(brain);
            brain.Bus.SwapBuffers();

            var possess = Assert.Single(brain.Bus.Read<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>().ToArray());
            Assert.Equal(brainsUnit, possess.Unit);
            Assert.Equal(300L, possess.StationNodeId);
            Assert.Equal(Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, possess.Mode);
            Assert.True(possess.IsRemote);
            var frame = Assert.Single(brain.Bus.Read<Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent>().ToArray());
            Assert.Equal((7u, 1f, -0.5f, 0.25f, 1.5f, (byte)1, (byte)1, (byte)2), (frame.Seq, frame.Forward, frame.Lateral, frame.Steer,
                frame.Yaw, frame.Gait, frame.Trigger, frame.Mount));
            Assert.Equal(new System.Numerics.Vector3(10f, 20f, 3f), frame.AimPoint);
            Assert.True(frame.IsRemote);
            var release = Assert.Single(brain.Bus.Read<Fdp.Toolkit.Behavior.Possession.ReleasePossessionEvent>().ToArray());
            Assert.Equal(300L, release.StationNodeId);
            Assert.True(release.IsRemote);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3110</c> I-2a (R-224) — embarkation crosses as three union cases: a node that does not own the vehicle sends the
        /// soldier's embark and disembark and the vehicle's eject (target = the vehicle, actor = the passenger); the vehicle's owner
        /// receives the same typed commands marked remote — and its <c>EmbarkationSystem</c> applies them (the loop guard keeps a
        /// remote one from being sent again).
        /// </summary>
        [Fact]
        public void CE3110_EmbarkDisembarkEject_CrossAsTheirUnionCases_AndTheVehiclesOwnerAppliesThem()
        {
            static EntityRepository Node()
            {
                var w = new EntityRepository();
                w.RegisterComponent<NetworkAuthority>();
                w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
                w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
                w.RegisterEvent<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>();
                w.RegisterEvent<Fdp.Toolkit.Behavior.Events.DisembarkEntityCommand>();
                w.RegisterEvent<Fdp.Toolkit.Behavior.Events.EjectPassengersCommand>();
                return w;
            }

            using var soldiersNode = Node();
            var map = new NetworkEntityMap();
            var apc = soldiersNode.CreateEntity();
            soldiersNode.AddComponent(apc, new Fdp.Toolkit.Behavior.Components.PassengerBuffer());
            soldiersNode.AddComponent(apc, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));   // node 2's vehicle
            var soldier = soldiersNode.CreateEntity();
            map.Register(800L, apc);
            map.Register(43L, soldier);
            var writer = new CapturingWriter<EntityInteractionRequest>();
            var egress = new InteractionEgressTranslator(writer, map);

            soldiersNode.Bus.Publish(new Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand { Passenger = soldier, Vehicle = apc });
            soldiersNode.Bus.SwapBuffers();
            egress.ScanAndPublish(soldiersNode);
            var embark = Assert.Single(writer.Written);
            Assert.Equal((800L, 43L, EInteractionKind.Embark), (embark.TargetId, embark.ActorId, embark.Payload.Kind));

            // the vehicle's owner: the command arrives remote and its applier boards the soldier
            using var owner = Node();
            var ownerMap = new NetworkEntityMap();
            var ownersApc = owner.CreateEntity();
            owner.AddComponent(ownersApc, new Fdp.Toolkit.Behavior.Components.PassengerBuffer());
            var ownersSoldier = owner.CreateEntity();
            ownerMap.Register(800L, ownersApc);
            ownerMap.Register(43L, ownersSoldier);
            var ingress = new InteractionIngressTranslator(participant: null, ownerMap);
            var applier = new Fdp.Toolkit.Behavior.Systems.EmbarkationSystem();
            void Deliver(EntityInteractionRequest sample)
            {
                using var cmd = new EntityCommandBuffer();
                ingress.ProcessSample(in sample, cmd);
                cmd.Playback(owner);
                owner.Bus.SwapBuffers();
                applier.Execute(owner, 0f);
            }
            Deliver(embark);
            Assert.True(Assert.Single(owner.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().ToArray()).IsRemote);
            Assert.Equal(ownersSoldier, owner.GetComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(ownersApc).Passengers[0]);
            var ownersEgress = new InteractionEgressTranslator(new CapturingWriter<EntityInteractionRequest>(), ownerMap);
            ownersEgress.ScanAndPublish(owner);
            Assert.Equal(0, ownersEgress.SentSampleCount);                                             // remote ⇒ never sent back

            // disembark (names its vehicle) and eject cross the same way
            writer.Written.Clear();
            soldiersNode.Bus.Publish(new Fdp.Toolkit.Behavior.Events.DisembarkEntityCommand { Passenger = soldier, Vehicle = apc });
            soldiersNode.Bus.Publish(new Fdp.Toolkit.Behavior.Events.EjectPassengersCommand { Vehicle = apc });
            soldiersNode.Bus.SwapBuffers();
            egress.ScanAndPublish(soldiersNode);
            Assert.Equal(2, writer.Written.Count);
            var disembark = Assert.Single(writer.Written, w => w.Payload.Kind == EInteractionKind.Disembark);
            Assert.Equal((800L, 43L), (disembark.TargetId, disembark.ActorId));
            Assert.Contains(writer.Written, w => w.Payload.Kind == EInteractionKind.EjectPassengers && w.TargetId == 800L);
            Deliver(disembark);
            Assert.Equal(0, owner.GetComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(ownersApc).Count);
            Assert.False(owner.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(ownersSoldier));

            // ⭐ CE-2151 B-b (R-2009 B4) — the seat ROLE crosses too (the payload's Role byte): a crew request arrives as that role.
            //   ✅ Red-proof: toPayload writing `Embark = default` ⇒ Role 0 on the wire ⇒ red.
            writer.Written.Clear();
            soldiersNode.Bus.Publish(new Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand
                { Passenger = soldier, Vehicle = apc, Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner });
            soldiersNode.Bus.SwapBuffers();
            egress.ScanAndPublish(soldiersNode);
            var asGunner = Assert.Single(writer.Written);
            Assert.Equal((byte)Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, asGunner.Payload.Embark.Role);
            Deliver(asGunner);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Gunner,
                Assert.Single(owner.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().ToArray()).Role);

            // ⭐ CE-2152 C-a (R-2011 C5) — and the CARRY MODE (the payload's Mode byte): a drag request arrives as a drag.
            //   ✅ Red-proof: toPayload without Mode ⇒ 0 (seated) on the wire ⇒ red.
            owner.Bus.SwapBuffers();
            writer.Written.Clear();
            soldiersNode.Bus.Publish(new Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand
                { Passenger = soldier, Vehicle = apc, Mode = Fdp.Toolkit.Behavior.Components.CarryMode.Dragged });
            soldiersNode.Bus.SwapBuffers();
            egress.ScanAndPublish(soldiersNode);
            var asDrag = Assert.Single(writer.Written);
            Assert.Equal((byte)Fdp.Toolkit.Behavior.Components.CarryMode.Dragged, asDrag.Payload.Embark.Mode);
            Deliver(asDrag);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.CarryMode.Dragged,
                Assert.Single(owner.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().ToArray()).Mode);
        }

        [Fact]
        public void R221_AKindThisBuildDoesNotKnow_IsDroppedAndCounted()
        {
            using var w = World();
            var map = new NetworkEntityMap();
            map.Register(700L, w.CreateEntity());
            var ingress = new InteractionIngressTranslator(participant: null, map);
            using var cmd = new EntityCommandBuffer();

            var unknown = new EntityInteractionRequest { TargetId = 700L, Payload = new InteractionPayload { Kind = (EInteractionKind)999 } };   // ⭐ CE-3110 — Embark (2) is known now; 999 is not
            ingress.ProcessSample(in unknown, cmd);
            cmd.Playback(w);
            w.Bus.SwapBuffers();

            Assert.Equal(1, ingress.UnknownKindCount);
            Assert.Equal(0, w.Bus.Read<DoorCommandEvent>().Length);
        }
    }
}
