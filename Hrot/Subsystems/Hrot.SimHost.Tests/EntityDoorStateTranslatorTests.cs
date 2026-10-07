using System.Threading;
using CycloneDDS.Runtime;
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
    /// ⭐ Buildings Stage 5b — a terrain door's state crosses the wire and reaches the OTHER node's terrain (📄
    /// docs/DESIGN_Building_Interiors.md §3j): the owner's <see cref="EntityDoorStateEgressTranslator"/> publishes on change, a
    /// receiver's <see cref="EntityDoorStateIngressTranslator"/> builds a ghost carrying the state and the key, and that node's
    /// <see cref="DoorStateMirrorSystem"/> writes it into its own <see cref="TerrainWorld"/> — so its sight is blocked by the door the
    /// owner locked.
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
            var mirror = new DoorStateMirrorSystem();
            var from = new System.Numerics.Vector3(25f, 10f, 1.5f); var to = new System.Numerics.Vector3(25f, 24f, 1.5f);

            Assert.True(replicaTerrain.SegmentBlocked(from, to));            // the terrain authored the door closed

            void Tick()
            {
                egress.ScanAndPublish(owner);
                Thread.Sleep(300);
                using var cmd = new EntityCommandBuffer();
                ingress.PollIngress(cmd, replica);
                cmd.Playback(replica);
                mirror.Execute(replica, 0f);
            }

            Thread.Sleep(200);
            Tick();
            Assert.True(map.TryGetEntity(5000L, out var ghost));
            Assert.Equal("range/H/front", replica.GetComponent<TerrainObjectKey>(ghost).Key);
            Assert.Equal(TerrainDoorState.Open, replica.GetComponentRO<DoorState>(ghost).State);
            Assert.False(replicaTerrain.SegmentBlocked(from, to));           // the owner's OPEN reached this node's terrain

            owner.SetComponent(door, new DoorState { State = TerrainDoorState.Locked });
            Tick();
            Assert.True(replicaTerrain.SegmentBlocked(from, to));            // …and so did its LOCK
            Assert.Equal(2, egress.SentSampleCount);

            Tick();
            Assert.Equal(2, egress.SentSampleCount);                         // send on change only
        }
    }
}
