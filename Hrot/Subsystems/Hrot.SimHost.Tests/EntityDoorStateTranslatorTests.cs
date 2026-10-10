using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Replication.Interactions;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ Buildings Stage 5b — a terrain door's state crosses the wire and reaches the OTHER node's terrain (📄
    /// docs/DESIGN_Building_Interiors.md §3j): the owner's <see cref="EntityDoorStateEgressTranslator"/> publishes on change, a
    /// receiver's <see cref="EntityDoorStateIngressTranslator"/> builds a ghost carrying the state and the key, and that node's
    /// own queries read it from that node's view (<see cref="DoorStates.Of(Fdp.ModuleHost.Abstractions.ISimulationView)"/>, R-219) — so its
    /// sight is blocked by the door the owner locked.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("SimHostDds")]
    public sealed class EntityDoorStateTranslatorTests
    {
        private const string OneDoorHouse = """
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H", "doors": { "front": "closed" },
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [ { "from": [0,0], "to": [10,0], "thickness": 0.3,
                      "openings": [ { "kind": "door", "at": 4.5, "width": 1, "doorId": "front" } ] } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        private static EntityRepository World(out TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<DoorState>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.RegisterEvent<DoorCommandEvent>();
            terrain = TerrainWorldParser.Parse(OneDoorHouse, "range");
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        [Fact]
        public void Stage5b_TheOwnersDoorState_ReachesTheOtherNodesTerrain_AndFollowsAChange()
        {
            const uint domainId = 226u;
            using var participant = new DdsParticipant(domainId);

            using var owner = World(out _);
            var door = owner.CreateEntity();
            owner.AddComponent(door, new NetworkIdentity(5000L));
            owner.AddComponent(door, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            owner.AddComponent(door, new DoorState { State = TerrainDoorState.Open });
            owner.SetManagedComponent(door, new TerrainObjectKey { Key = "range/H/front" });
            var egress = new EntityDoorStateEgressTranslator(participant);

            using var replica = World(out var replicaTerrain);
            var map = new NetworkEntityMap();
            var ingress = new EntityDoorStateIngressTranslator(participant, map, new GhostCreationSystem(map), localNodeId: 2);
            var from = new System.Numerics.Vector3(25f, 10f, 1.5f); var to = new System.Numerics.Vector3(25f, 24f, 1.5f);

            Assert.True(replicaTerrain.SegmentBlocked(from, to, DoorStates.Of(replica)));   // the terrain authored the door closed

            void Tick()
            {
                egress.ScanAndPublish(owner);
                Thread.Sleep(300);
                using var cmd = new EntityCommandBuffer();
                ingress.PollIngress(cmd, replica);
                cmd.Playback(replica);
            }

            Thread.Sleep(200);
            Tick();
            Assert.True(map.TryGetEntity(5000L, out var ghost));
            Assert.Equal("range/H/front", replica.GetComponent<TerrainObjectKey>(ghost).Key);
            Assert.Equal(TerrainDoorState.Open, replica.GetComponentRO<DoorState>(ghost).State);
            Assert.False(replicaTerrain.SegmentBlocked(from, to, DoorStates.Of(replica)));   // the owner's OPEN reached this node's view

            owner.SetComponent(door, new DoorState { State = TerrainDoorState.Locked });
            Tick();
            Assert.True(replicaTerrain.SegmentBlocked(from, to, DoorStates.Of(replica)));    // …and so did its LOCK
            Assert.Equal(2, egress.SentSampleCount);

            Tick();
            Assert.Equal(2, egress.SentSampleCount);                         // send on change only
        }

        /// <summary>
        /// ⭐⭐ Buildings Stage 5d (📄 docs/DESIGN_Building_Interiors.md §3j "5d") — a door command raised on a node that does NOT own the
        /// door (here: it only holds the replica) travels to the owner over the ONE interaction topic (R-221), the owner applies it, and the result comes back as the door's
        /// state. Nothing applies it on the replica (a ghost is never the owner), and the owner never sends its own command anywhere.
        /// </summary>
        [Fact]
        public void Stage5d_ACommandRaisedOnAReplica_IsAppliedByTheOwner_AndTheNewStateComesBack()
        {
            const uint domainId = 227u;
            using var participant = new DdsParticipant(domainId);
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtDoorState, DoorCommandSystem.DoorStateDescriptorOrdinal);   // the toolkit's copy of the ordinal

            using var owner = World(out _);
            var door = owner.CreateEntity();
            owner.AddComponent(door, new NetworkIdentity(5100L));
            owner.AddComponent(door, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            owner.AddComponent(door, new DoorState { State = TerrainDoorState.Closed });
            owner.SetManagedComponent(door, new TerrainObjectKey { Key = "range/H/front" });
            var ownerMap = new NetworkEntityMap();
            ownerMap.Register(5100L, door);
            var stateOut   = new EntityDoorStateEgressTranslator(participant);
            var commandIn  = new InteractionIngressTranslator(participant, ownerMap);
            var commandOut = new InteractionEgressTranslator(participant, ownerMap);
            var applier    = new DoorCommandSystem();

            using var replica = World(out _);
            var replicaMap = new NetworkEntityMap();
            var stateIn    = new EntityDoorStateIngressTranslator(participant, replicaMap, new GhostCreationSystem(replicaMap), localNodeId: 2);
            var replicaOut = new InteractionEgressTranslator(participant, replicaMap);
            var replicaApplier = new DoorCommandSystem();

            void Pump()
            {
                stateOut.ScanAndPublish(owner);
                Thread.Sleep(300);
                using var c1 = new EntityCommandBuffer(); stateIn.PollIngress(c1, replica); c1.Playback(replica);
                using var c2 = new EntityCommandBuffer(); commandIn.PollIngress(c2, owner); c2.Playback(owner);
                owner.Bus.SwapBuffers();
                applier.Execute(owner, 0.1f);
                commandOut.ScanAndPublish(owner);   // the owner's own event (and the remote one) never goes back out
            }

            Thread.Sleep(200);
            Pump();
            Assert.True(replicaMap.TryGetEntity(5100L, out var ghost));
            Assert.Equal(TerrainDoorState.Closed, replica.GetComponentRO<DoorState>(ghost).State);

            replica.Bus.Publish(new DoorCommandEvent { Door = ghost, Verb = DoorVerb.Lock });
            replica.Bus.SwapBuffers();
            replicaApplier.Execute(replica, 0.1f);
            Assert.Equal(TerrainDoorState.Closed, replica.GetComponentRO<DoorState>(ghost).State);   // a replica never applies
            replicaOut.ScanAndPublish(replica);
            Assert.Equal(1, replicaOut.SentSampleCount);

            Pump();                                      // the command reaches the owner, which locks the door
            Assert.Equal(TerrainDoorState.Locked, owner.GetComponentRO<DoorState>(door).State);
            Assert.Equal(1, applier.Applied);
            Pump();                                      // …and the new state reaches the replica
            Assert.Equal(TerrainDoorState.Locked, replica.GetComponentRO<DoorState>(ghost).State);
            Assert.Equal(0, commandOut.SentSampleCount);
        }
    }
}
