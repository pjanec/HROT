using System.Numerics;
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
    /// ⭐⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-11) — a building's wall breaches cross the wire: the deciding node's
    /// <see cref="EntityBuildingDamageEgressTranslator"/> publishes the <see cref="BuildingDamage"/> entity on change, a receiver's
    /// <see cref="EntityBuildingDamageIngressTranslator"/> builds a ghost carrying the breaches and the building key, and that node's own queries read them from
    /// its own view (<see cref="DoorStates.Damage"/>, R-219) — its sight passes the breach the owner made. The <c>EntityDoorState</c> pair's shape.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("SimHostDds")]
    public sealed class EntityBuildingDamageTranslatorTests
    {
        private const string BrickHouse = """
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3 }, { "from": [10,0], "to": [10,8], "thickness": 0.3 },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3 }, { "from": [0,8], "to": [0,0], "thickness": 0.3 } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        private static EntityRepository World(out TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<BuildingDamage>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            terrain = TerrainWorldParser.Parse(BrickHouse, "range");
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        private static int SouthWall(TerrainWorld t) => System.Array.FindIndex(System.Linq.Enumerable.ToArray(t.Panels),
            p => System.MathF.Abs(p.A.Y - 20f) < 1e-3f && System.MathF.Abs(p.B.Y - 20f) < 1e-3f && p.B.X > p.A.X);

        private static BuildingDamage Damage(int south)
        {
            var d = new BuildingDamage { Building = 0 };
            BuildingDamages.Add(ref d, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage), null);
            BuildingDamages.Add(ref d, new WallBreach(south, 8f, 9f, 1f, 1.6f, BreachKind.Hole), null);
            return d;
        }

        /// <summary>The toolkit states the wire ordinal (it cannot reference the NED assembly); this pins the two equal.</summary>
        [Fact]
        public void TheToolkitsOrdinal_IsTheDescriptorsOrdinal()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtBuildingDamage, BuildingDamageSystem.DescriptorOrdinal);

        /// <summary>
        /// The wire form round-trips with no network: every breach and the key reach a ghost on the other node, whose view then lets a line through; the recorded OWNER
        /// never takes its own sample back. 🔴 Red-proof: drop the <c>Kind</c> copy in <c>FromSample</c> ⇒ the passage count differs.
        /// </summary>
        [Fact]
        public void TheSample_RoundTripsEveryBreach_AndTheOwnerNeverTakesItsOwnBack()
        {
            using var replica = World(out var terrain);
            int south = SouthWall(terrain);
            var original = Damage(south);
            var sample = EntityBuildingDamageEgressTranslator.ToSample(5200, original, "range/H");
            var map = new NetworkEntityMap();
            var ingress = new EntityBuildingDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);

            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(5200, out var ghost));
            Assert.Equal("range/H", replica.GetComponent<TerrainObjectKey>(ghost).Key);
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<BuildingDamage>(in original))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<BuildingDamage>(replica.GetComponentRO<BuildingDamage>(ghost)))));
            var damage = DoorStates.Of(replica, terrain).Damage!;
            Assert.Equal(2, damage.Count);
            Assert.Equal(1, damage.PassageCount);
            Assert.False(terrain.SegmentBlocked(new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f), DoorStates.Of(replica, terrain)));   // the owner's passage reached this node's view
            Assert.True(terrain.SegmentBlocked(new Vector3(22f, 15f, 1.5f), new Vector3(22f, 25f, 1.5f), DoorStates.Of(replica, terrain)));

            // the node that OWNS the entity (recorded ownership) ignores a sample of it: its own loopback must not overwrite a newer local value
            replica.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));
            var newer = original; BuildingDamages.Add(ref newer, new WallBreach(south, 1f, 1.5f, 1f, 1.5f, BreachKind.Hole), null);
            replica.SetComponent(ghost, newer);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(3, replica.GetComponentRO<BuildingDamage>(ghost).Count);
        }

        /// <summary>
        /// ⭐ <c>CE-4417</c> (Terrain Authoring §18 C-4, O-3) — the deciding node breaches house "H", which is building 1 in ITS world (another house
        /// stands before it); on the receiver "H" is building 0. The sample names the house by KEY and the wall by its index WITHIN the house, so the
        /// receiver breaches ITS "H"'s south wall — the same house, the same wall. 🔴 Mutation: <c>FromSample</c> keeps the sender's building index
        /// (ignores the key) ⇒ the receiver's damage names building 1, which it does not have ⇒ red.
        /// </summary>
        [Fact]
        public void CE4417_ABreach_LandsOnTheSameHouseAndWall_WhenTheTwoWorldsIndexItDifferently()
        {
            const string Garage = """
                { "type": "Feature", "properties": { "kind": "building", "label": "G",
                    "building": { "footprint": [[0,0],[6,0],[6,6],[0,6]],
                      "storeys": [ { "height": 3, "walls": [
                        { "from": [0,0], "to": [6,0], "thickness": 0.3 }, { "from": [6,0], "to": [6,6], "thickness": 0.3 },
                        { "from": [6,6], "to": [0,6], "thickness": 0.3 }, { "from": [0,6], "to": [0,0], "thickness": 0.3 } ] } ] } },
                  "geometry": { "type": "Point", "coordinates": [-40, -40] } }
                """;
            var sender = TerrainWorldParser.Parse(BrickHouse.Replace("\"features\": [ ", "\"features\": [ " + Garage + ", "), "range");
            int senderH = BuildingDamages.IndexOfKey(sender, "range/H");
            Assert.Equal(1, senderH);   // the sender's house H is building 1
            int senderSouth = SouthWall(sender);
            var decided = new BuildingDamage { Building = senderH };
            BuildingDamages.Add(ref decided, new WallBreach(senderSouth, 4f, 6f, 0f, 2.2f, BreachKind.Passage), null);
            var sample = EntityBuildingDamageEgressTranslator.ToSample(5203, decided, "range/H", sender);

            using var replica = World(out var terrain);   // H is building 0 here
            var map = new NetworkEntityMap();
            var ingress = new EntityBuildingDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(5203, out var ghost));
            var got = replica.GetComponentRO<BuildingDamage>(ghost);
            Assert.Equal(0, got.Building);
            Assert.Equal(1, got.Count);
            Assert.Equal(SouthWall(terrain), got.Get(0).Panel);
            Assert.False(terrain.SegmentBlocked(new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f), DoorStates.Of(replica, terrain)));   // the passage is in THIS node's south wall
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4599</c> (B-2, D-1, V-11) — a GARDEN WALL's breach crosses the wire on the same record: the sender's world has one extra house (so the line is group 2
        /// there, group 1 on the receiver); the sample names the line by its key <c>range/wall:Garden</c> and the segment by its rank, and the receiver opens ITS garden
        /// wall's second segment. 🔴 Red-proof: key a line by its group index ⇒ the receiver resolves nothing (or the wrong wall).
        /// </summary>
        [Fact]
        public void CE4599_AGardenWallBreach_LandsOnTheSameWall_WhenTheTwoWorldsNumberItDifferently()
        {
            const string Wall = """
                { "type": "Feature", "properties": { "kind": "wall", "label": "Garden", "height": 1.0, "thickness": 0.3, "material": "brick" },
                  "geometry": { "type": "LineString", "coordinates": [[0,10],[20,10],[40,10]] } }
                """;
            const string Garage = """
                { "type": "Feature", "properties": { "kind": "building", "label": "G",
                    "building": { "footprint": [[0,0],[6,0],[6,6],[0,6]],
                      "storeys": [ { "height": 3, "walls": [
                        { "from": [0,0], "to": [6,0], "thickness": 0.3 }, { "from": [6,0], "to": [6,6], "thickness": 0.3 },
                        { "from": [6,6], "to": [0,6], "thickness": 0.3 }, { "from": [0,6], "to": [0,0], "thickness": 0.3 } ] } ] } },
                  "geometry": { "type": "Point", "coordinates": [-40, -40] } }
                """;
            var sender = TerrainWorldParser.Parse(BrickHouse.Replace("\"features\": [ ", "\"features\": [ " + Garage + ", " + Wall + ", "), "range");
            int senderLine = BuildingDamages.IndexOfKey(sender, "range/wall:Garden");
            Assert.Equal(2, senderLine);
            int senderPanel = BuildingPanelIndex.ToGlobal(sender, senderLine, 1);
            var decided = new BuildingDamage { Building = senderLine };
            BuildingDamages.Add(ref decided, new WallBreach(senderPanel, 4f, 6f, 0f, 1f, BreachKind.Passage), null);
            var sample = EntityBuildingDamageEgressTranslator.ToSample(5204, decided, "range/wall:Garden", sender);

            var repo = World(out _);
            var terrain = TerrainWorldParser.Parse(BrickHouse.Replace("\"features\": [ ", "\"features\": [ " + Wall + ", "), "range");
            repo.SetSingletonManaged(terrain);
            using (repo)
            {
                var map = new NetworkEntityMap();
                var ingress = new EntityBuildingDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);
                using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, repo); cmd.Playback(repo); }
                Assert.True(map.TryGetEntity(5204, out var ghost));
                var got = repo.GetComponentRO<BuildingDamage>(ghost);
                Assert.Equal(1, got.Building);
                Assert.Equal(BuildingPanelIndex.ToGlobal(terrain, 1, 1), got.Get(0).Panel);
                Assert.False(terrain.SegmentBlocked(new Vector3(25f, 5f, 0.5f), new Vector3(25f, 15f, 0.5f), DoorStates.Of(repo, terrain)), "the gap in THIS node's garden wall");
                Assert.True(terrain.SegmentBlocked(new Vector3(10f, 5f, 0.5f), new Vector3(10f, 15f, 0.5f), DoorStates.Of(repo, terrain)), "its first segment stands");
            }
        }

        /// <summary>
        /// ⭐ CE-4427 R-2 — a breach whose KEY the receiver cannot resolve NEVER breaches another house: the sample names panel 2 of a building this node does not have; the old fallback stored 2 as a GLOBAL index (a wall of
        /// house "H"). Dropped, once the world is resident. 🔴 Red-proof: in <c>TryFromSample</c> fall back to the sender's building and raw panel when the key resolves nothing ⇒ H's wall 2 is breached.
        /// </summary>
        [Fact]
        public void CE4427_R2_ABreachWhoseKeyTheReceiverCannotResolve_NeverBreachesAnotherHouse()
        {
            using var replica = World(out var terrain);
            var from = new BuildingDamage { Building = 0 };
            BuildingDamages.Add(ref from, new WallBreach(2, 4f, 6f, 0f, 2.2f, BreachKind.Passage), null);
            var sample = EntityBuildingDamageEgressTranslator.ToSample(5210, from, "range/NoSuchHouse");   // no terrain: the panel travels as 2
            var map = new NetworkEntityMap();
            var ingress = new EntityBuildingDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }

            Assert.Equal(0, ingress.DeferredCount);   // the world is resident: dropped, not waiting
            if (map.TryGetEntity(5210, out var ghost) && replica.HasComponent<BuildingDamage>(ghost))
                Assert.Equal(0, replica.GetComponentRO<BuildingDamage>(ghost).Count);
            Assert.Equal(0, DoorStates.Of(replica, terrain).Damage?.Passages().Count ?? 0);
            Assert.True(terrain.SegmentBlocked(new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f), DoorStates.Of(replica, terrain)));   // H's south wall still stands
        }

        /// <summary>
        /// ⭐ CE-4427 R-2 — a sample that arrives BEFORE the receiver's terrain waits (deferred, not applied raw) and lands on the right house and wall when the world arrives. 🔴 Red-proof: make
        /// <c>RetryDeferred</c> return at once ⇒ the breach never lands.
        /// </summary>
        [Fact]
        public void CE4427_R2_ASampleThatArrivesBeforeTheTerrain_IsDeferred_AndLandsWhenTheWorldIsResident()
        {
            using var replica = new EntityRepository();
            replica.RegisterComponent<NetworkIdentity>(); replica.RegisterComponent<NetworkAuthority>(); replica.RegisterComponent<GhostStateTracker>();
            replica.RegisterComponent<BuildingDamage>(); replica.RegisterManagedComponent<TerrainObjectKey>(); replica.RegisterManagedComponent<TerrainWorld>();
            var terrain = TerrainWorldParser.Parse(BrickHouse, "range");
            int south = SouthWall(terrain);
            var from = new BuildingDamage { Building = 0 };
            BuildingDamages.Add(ref from, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage), null);
            var sample = EntityBuildingDamageEgressTranslator.ToSample(5211, from, "range/H", terrain);
            var map = new NetworkEntityMap();
            var ingress = new EntityBuildingDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);

            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(1, ingress.DeferredCount);
            if (map.TryGetEntity(5211, out var early) && replica.HasComponent<BuildingDamage>(early))
                Assert.Equal(0, replica.GetComponentRO<BuildingDamage>(early).Count);   // nothing applied raw

            replica.SetSingletonManaged(terrain);   // the world arrives
            using (var cmd = new EntityCommandBuffer()) { ingress.PollIngress(cmd, replica); cmd.Playback(replica); }
            Assert.Equal(0, ingress.DeferredCount);
            Assert.True(map.TryGetEntity(5211, out var ghost));
            var got = replica.GetComponentRO<BuildingDamage>(ghost);
            Assert.Equal(0, got.Building);
            Assert.Equal(1, got.Count);
            Assert.Equal(south, got.Get(0).Panel);
        }

        /// <summary>
        /// ⭐ CE-4427 R-3 (HOT PATH) — a STEADY egress tick (an owned damaged building, nothing changed) allocates nothing and rescans no panels: the query is built once, the unchanged component is compared with the last sample
        /// BEFORE any sample is built, and the within-building panel comes from the world's cached table. 🔴 Red-proof: replace <c>Matches</c> by <c>ToSample</c> + <c>Same</c> in <c>ScanAndPublish</c> ⇒ bytes > 0.
        /// </summary>
        [Fact]
        public void CE4427_R3_ASteadyEgressTick_AllocatesNothing_AndRescansNoPanels()
        {
            using var participant = new DdsParticipant(228u);
            using var owner = World(out var terrain);
            var entity = owner.CreateEntity();
            owner.AddComponent(entity, new NetworkIdentity(5320));
            owner.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            owner.AddComponent(entity, Damage(SouthWall(terrain)));
            owner.SetManagedComponent(entity, new TerrainObjectKey { Key = "range/H" });
            var egress = new EntityBuildingDamageEgressTranslator(participant);

            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);
            for (int i = 0; i < 30; i++) egress.ScanAndPublish(owner);   // warm
            int tables = BuildingPanelIndex.TablesBuilt;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) egress.ScanAndPublish(owner);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(1, egress.SentSampleCount);              // nothing re-sent
            Assert.Equal(tables, BuildingPanelIndex.TablesBuilt); // no table (re)built
            Assert.Equal(0L, bytes);
        }

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-2 — the COLLAPSE crosses the wire: <c>Collapsed</c> is copied by the egress and the ingress, and the other node's view then shows the mound.
        /// 🔴 Red-proof: drop <c>c.Collapsed = d.Collapsed</c> in <c>FromSample</c> ⇒ the replica's building stands.
        /// </summary>
        [Fact]
        public void TheCollapse_ReachesTheOtherNodesView_AsAMound()
        {
            using var replica = World(out var terrain);
            var original = new BuildingDamage { Building = 0, Collapsed = 1 };
            var sample = EntityBuildingDamageEgressTranslator.ToSample(5201, original, "range/H");
            Assert.Equal(1, sample.Collapsed);
            var map = new NetworkEntityMap();
            var ingress = new EntityBuildingDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            var damage = DoorStates.Of(replica, terrain).Damage!;
            Assert.True(damage.Collapsed(0));
            Assert.Single(damage.Rubble);
            Assert.False(terrain.SegmentBlocked(new Vector3(15f, 24f, 1.6f), new Vector3(35f, 24f, 1.6f), DoorStates.Of(replica, terrain)));   // across the former interior, above the mound
            Assert.True(terrain.SegmentBlocked(new Vector3(15f, 24f, 0.5f), new Vector3(35f, 24f, 0.5f), DoorStates.Of(replica, terrain)));    // …and the mound stops the low one
        }

        [Fact]
        public void TheOwnersBuildingDamage_ReachesTheOtherNodesView_AndFollowsAChange_OnTheRealWire()
        {
            const uint domainId = 226u;
            using var participant = new DdsParticipant(domainId);

            using var owner = World(out var ownerTerrain);
            int south = SouthWall(ownerTerrain);
            var entity = owner.CreateEntity();
            owner.AddComponent(entity, new NetworkIdentity(5300));
            owner.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            owner.AddComponent(entity, Damage(south));
            owner.SetManagedComponent(entity, new TerrainObjectKey { Key = "range/H" });
            var egress = new EntityBuildingDamageEgressTranslator(participant);

            using var replica = World(out var replicaTerrain);
            var map = new NetworkEntityMap();
            var ingress = new EntityBuildingDamageIngressTranslator(participant, map, new GhostCreationSystem(map), localNodeId: 2);
            var through = (From: new Vector3(25f, 15f, 1.5f), To: new Vector3(25f, 25f, 1.5f));
            Assert.True(replicaTerrain.SegmentBlocked(through.From, through.To, DoorStates.Of(replica)));   // whole before the sample arrives

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
            Assert.True(map.TryGetEntity(5300, out var ghost));
            Assert.Equal("range/H", replica.GetComponent<TerrainObjectKey>(ghost).Key);
            Assert.Equal(2, replica.GetComponentRO<BuildingDamage>(ghost).Count);
            Assert.False(replicaTerrain.SegmentBlocked(through.From, through.To, DoorStates.Of(replica)));   // the owner's breach is in this node's view
            Assert.Equal(1, egress.SentSampleCount);

            Tick();
            Assert.Equal(1, egress.SentSampleCount);                                                         // send on change only

            // a replica holds only a GHOST: it never publishes the entity as if it owned it
            var replicaEgress = new EntityBuildingDamageEgressTranslator(participant);
            replicaEgress.ScanAndPublish(replica);
            Assert.Equal(0, replicaEgress.SentSampleCount);

            var changed = owner.GetComponentRO<BuildingDamage>(entity);
            BuildingDamages.Add(ref changed, new WallBreach(south, 1f, 1.5f, 1f, 1.5f, BreachKind.Hole), null);
            owner.SetComponent(entity, changed);
            Tick();
            Assert.Equal(2, egress.SentSampleCount);
            Assert.Equal(3, replica.GetComponentRO<BuildingDamage>(ghost).Count);
        }
    }
}
