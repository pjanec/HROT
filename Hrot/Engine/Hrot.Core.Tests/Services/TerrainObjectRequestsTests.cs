using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Core.Network;
using Fdp.Core;
using Xunit;

namespace Hrot.Core.Tests.Services
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-4 (UTM §8.2 B-7) — the load's half: <see cref="TerrainObjectRequests.ForBuildingDamage"/> turns a scenario's saved building damage into creation requests.
    /// </summary>
    public sealed class TerrainObjectRequestsTests
    {
        private sealed class Ids : INetworkIdAllocator
        {
            private int _next = 1000;
            public int AllocateId() => _next++;
            public void Reset(int startId = 0) => _next = startId;
            public void Dispose() { }
        }

        /// <summary>Two houses, so the second one's panels have a GLOBAL index (4..7) that differs from the index within the building (0..3).</summary>
        private const string House = """
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } },
               { "type": "Feature",
                "properties": { "kind": "building", "label": "G",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [40, 20] } } ] }
            """;

        private static IReadOnlyDictionary<string, SavedBuildingDamage> Saved(string json)
            => TerrainObjectsSection.ReadBuildingDamage((JsonObject)JsonNode.Parse("{ \"TerrainObjects\": " + json + " }")!);

        /// <summary>
        /// A saved building becomes ONE transient request for a <c>BuildingDamage</c> entity (TKB 7203, an id from the cluster's allocator) carrying the breaches (the panel back to its GLOBAL index) and the collapsed byte,
        /// at the building's position, keyed by the building's terrain-object key. ⚠ R-4196 — untargeted (<c>OwnerAppInstanceId</c> 0): the default processor, i.e. the LOADING node, owns it, exactly like a door.
        /// 🔴 Red-proof: drop the <c>ToGlobal</c> mapping ⇒ the panel comes back as the local index; drop <c>IsTransient</c> ⇒ the entity would be saved as an entity.
        /// </summary>
        [Fact]
        public void ASavedBuilding_BecomesOneTransientRequest_WithTheBreachesAndTheCollapsedByte()
        {
            var w = TerrainWorldParser.Parse(House, "range");
            var saved = Saved("""{ "range/G": { "breaches": [[0, 4, 6, 0, 2.2, "passage"]], "collapsed": true } }""");
            var requests = TerrainObjectRequests.ForBuildingDamage(w, saved, new HashSet<string>(), new Ids());

            var r = Assert.Single(requests);
            Assert.Equal(BuildingDamages.TkbType, (long)r.TkbType);
            Assert.Equal(1000, r.PreAllocatedNetworkId);
            Assert.True(r.IsTransient);
            Assert.Equal(EntityCreationRouting.DefaultEntityCreationRequestProcessor, r.OwnerAppInstanceId);   // the loader's (R-4196: not a muscle in a cluster)
            Assert.Equal("range/G", r.InitialComponents!.OfType<TerrainObjectKey>().Single().Key);
            var d = r.InitialComponents!.OfType<BuildingDamage>().Single();
            Assert.Equal(1, d.Building);
            Assert.Equal(4, BuildingPanelIndex.ToGlobal(w, 1, 0));   // the second house: global 4 ≠ local 0
            Assert.Equal(1, d.Collapsed);
            Assert.Equal(1, d.Count);
            Assert.Equal(new WallBreach(BuildingPanelIndex.ToGlobal(w, 1, 0), 4f, 6f, 0f, 2.2f, BreachKind.Passage), d.Get(0));
            var at = r.InitialComponents!.OfType<SimTransform>().Single().Position;
            Assert.Equal(new Vector3(w.Buildings[1].Position, w.Buildings[1].BaseZ), at.AsVector3);
        }

        /// <summary>A building with damage already present (a re-commit) is not doubled; one with no damage makes no request. 🔴 Red-proof: drop the <c>existingKeys</c> test ⇒ a second request.</summary>
        [Fact]
        public void AReCommit_NeverDoublesABuildingsDamage_AndAnEmptyEntryMakesNoRequest()
        {
            var w = TerrainWorldParser.Parse(House, "range");
            var saved = Saved("""{ "range/H": { "breaches": [[0, 4, 6, 0, 2.2, "passage"]] } }""");
            Assert.Empty(TerrainObjectRequests.ForBuildingDamage(w, saved, new HashSet<string> { "range/H" }, new Ids()));
            Assert.Empty(TerrainObjectRequests.ForBuildingDamage(w, Saved("""{ "range/H": { "breaches": [] } }"""), new HashSet<string>(), new Ids()));
            Assert.Empty(TerrainObjectRequests.ForBuildingDamage(w, null, new HashSet<string>(), new Ids()));
        }

        /// <summary>A building the terrain lacks is warned (through the log, here a captured callback) and ignored; the others are still made. 🔴 Red-proof: drop the <c>byKey</c> test ⇒ a throw.</summary>
        [Fact]
        public void ABuildingTheTerrainLacks_IsWarnedAndIgnored()
        {
            var w = TerrainWorldParser.Parse(House, "range");
            var saved = Saved("""{ "range/Gone": { "collapsed": true }, "range/H": { "collapsed": true } }""");
            var warnings = new List<string>();
            var requests = TerrainObjectRequests.ForBuildingDamage(w, saved, new HashSet<string>(), new Ids(), warnings.Add);
            Assert.Equal("range/H", Assert.Single(requests).InitialComponents!.OfType<TerrainObjectKey>().Single().Key);
            Assert.Contains("range/Gone", Assert.Single(warnings));
        }
    
        /// <summary>
        /// ⭐ <c>CE-4591</c> (P-4 part 3) — the saved craters become one ground-damage entity per CELL (17 in one cell ⇒ two entities), untargeted (the loader's), transient,
        /// at the cell's anchor; a world that already holds a ground-damage entity (a re-commit) gets none. 🔴 Red-proof: drop the re-commit guard ⇒ a second batch.
        /// </summary>
        [Fact]
        public void CE4591_SavedCraters_BecomeOneEntityPerCell_AndAReCommitDoublesNothing()
        {
            var craters = new List<Crater>();
            for (int i = 0; i < 17; i++) craters.Add(new Crater(new Vector2(1f + i, 1f), 1f, 0.4f, 0.1f, (SimInstant)i, CraterKind.Small));
            craters.Add(new Crater(new Vector2(100f, 10f), 3.6f, 1.6f, 0.3f, (SimInstant)20, CraterKind.Large));   // cell (1, 0)
            using var world = new EntityRepository();
            world.RegisterComponent<GroundDamage>();
            var requests = TerrainObjectRequests.ForCraters(world, craters, new Ids());
            Assert.Equal(3, requests.Count);
            var cells = requests.Select(r => r.InitialComponents!.OfType<GroundDamage>().Single()).ToList();
            Assert.Equal(new[] { 16, 1, 1 }, cells.Select(d => (int)d.Count).ToArray());
            Assert.Equal((1, 0), (cells[2].CellX, cells[2].CellY));
            Assert.All(requests, r => { Assert.True(r.IsTransient); Assert.Equal(GroundDamages.TkbType, (long)r.TkbType); Assert.Equal(EntityCreationRouting.DefaultEntityCreationRequestProcessor, r.OwnerAppInstanceId); });

            var e = world.CreateEntity(); world.AddComponent(e, cells[0]);
            Assert.Empty(TerrainObjectRequests.ForCraters(world, craters, new Ids()));
        }
    }
}
