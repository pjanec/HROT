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

        [Fact]
        public void R221_AKindThisBuildDoesNotKnow_IsDroppedAndCounted()
        {
            using var w = World();
            var map = new NetworkEntityMap();
            map.Register(700L, w.CreateEntity());
            var ingress = new InteractionIngressTranslator(participant: null, map);
            using var cmd = new EntityCommandBuffer();

            var unknown = new EntityInteractionRequest { TargetId = 700L, Payload = new InteractionPayload { Kind = EInteractionKind.Embark } };
            ingress.ProcessSample(in unknown, cmd);
            cmd.Playback(w);
            w.Bus.SwapBuffers();

            Assert.Equal(1, ingress.UnknownKindCount);
            Assert.Equal(0, w.Bus.Read<DoorCommandEvent>().Length);
        }
    }
}
