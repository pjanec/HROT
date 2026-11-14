using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-4 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-7, Q-D3) — building damage in the scenario's <c>TerrainObjects</c> section: the save (the owned entities' UNION per building, the
    /// panel as its index within the building), the strict reader, and the load's components (<see cref="BuildingDamageLoad"/>; the requests are <c>TerrainObjectRequests.ForBuildingDamage</c> in Hrot.Core.Tests).
    /// </summary>
    public sealed class BuildingDamageSaveTests
    {
        private static EntityRepository World(TerrainWorld w)
        {
            var repo = BreachFixtures.World(w);
            repo.RegisterComponent<NetworkAuthority>();
            return repo;
        }

        private static Entity OwnedBy(EntityRepository repo, Entity e, int owner)
        {
            repo.AddComponent(e, new NetworkAuthority(primaryOwnerId: owner, localNodeId: 1));
            return e;
        }

        /// <summary>The loaded world: the saved section read back and turned into entities the way the load does, then viewed through the table.</summary>
        private static BuildingDamageTable Reload(TerrainWorld w, JsonObject section, List<string>? warnings = null)
        {
            var dom = new JsonObject { [TerrainObjectsSection.Name] = JsonNode.Parse(section.ToJsonString()) };   // through TEXT: the file's own number formatting
            var saved = TerrainObjectsSection.ReadBuildingDamage(dom);
            var repo = BreachFixtures.World(w);
            foreach (var (key, building, entities) in BuildingDamageLoad.Resolve(w, saved, m => warnings?.Add(m)))
                foreach (var d in entities)
                {
                    var e = repo.CreateEntity();
                    repo.AddComponent(e, d);
                    repo.SetManagedComponent(e, new TerrainObjectKey { Key = key });
                }
            return DoorStates.Of(repo, w).Damage!;
        }

        /// <summary>
        /// ⭐ A save after a breach (a passage and a hole) and a collapse, then a reload: the same <see cref="BuildingDamageTable"/> — breaches equal one by one, collapsed equal, the same NavHash.
        /// 🔴 Red-proof: write <c>Kind</c> as hole always ⇒ the passage turns into a hole; drop the <c>collapsed</c> write ⇒ the mound is gone.
        /// </summary>
        [Fact]
        public void ASaveAfterABreachAndACollapse_ReloadsToTheSameTable()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = World(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4.1f, 6.3f, 0f, 2.2f, BreachKind.Passage), new WallBreach(south, 8f, 8.6f, 1.1f, 1.6f, BreachKind.Hole));
            BreachFixtures.Collapsed(repo, w, 0);
            var before = DoorStates.Of(repo, w).Damage!;

            var section = TerrainObjectsSection.Write(repo)!;
            Assert.NotNull(section);
            var key = Assert.Single(section).Key;
            Assert.Equal("range/H", key);
            var entry = (JsonObject)section[key]!;
            Assert.True((bool)entry["collapsed"]!);
            Assert.Equal(2, entry["breaches"]!.AsArray().Count);
            var panelWithinBuilding = (int)entry["breaches"]![0]![0]!;
            Assert.Equal(BuildingPanelIndex.ToLocal(w, south), panelWithinBuilding);   // the panel is saved as its index WITHIN the building

            var after = Reload(w, section);
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++) Assert.Equal(before[i], after[i]);
            Assert.True(after.Collapsed(0));
            Assert.Equal(before.NavHash, after.NavHash);
        }

        /// <summary>An untouched building writes nothing: no entity, an empty entity, a foreign-owned one — no section at all. 🔴 Red-proof: drop the empty-entry removal ⇒ an entry for an empty entity.</summary>
        [Fact]
        public void AnUntouchedBuilding_WritesNothing()
        {
            var w = BreachFixtures.House();
            using var repo = World(w);
            Assert.Null(TerrainObjectsSection.Write(repo));
            OwnedBy(repo, BreachFixtures.Damage(repo, w, 0), owner: 1);   // an entity holding no breach and not collapsed
            Assert.Null(TerrainObjectsSection.Write(repo));
        }

        /// <summary>The save is the owner's: a damage entity another node owns is that node's slice, not this one's (one entity, one slice). 🔴 Red-proof: drop the <c>OwnsDamage</c> gate ⇒ the foreign entity is written too.</summary>
        [Fact]
        public void OnlyTheOwnedEntitiesAreWritten()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = World(w);
            OwnedBy(repo, BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 1f, 2f, 0f, 2.2f, BreachKind.Passage)), owner: 2);   // node 2's
            Assert.Null(TerrainObjectsSection.Write(repo));
            OwnedBy(repo, BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 8f, 9f, 1f, 1.5f, BreachKind.Hole)), owner: 1);      // ours
            var entry = (JsonObject)TerrainObjectsSection.Write(repo)!["range/H"]!;
            Assert.Single(entry["breaches"]!.AsArray());
        }

        /// <summary>
        /// ⭐ Two owned entities for one building (a full first one, then a second — or two entities after a merge) save as ONE entry: the breaches distinct (a breach in both is written once), in file order; collapsed on either ⇒ true.
        /// 🔴 Red-proof: key the entry by entity (overwrite per entity) ⇒ only the last entity's breaches; replace <c>AddDistinct</c> by <c>Add</c> ⇒ the shared breach twice.
        /// </summary>
        [Fact]
        public void TwoOwnedEntitiesForOneBuilding_SaveAsOneUnionEntry()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = World(w);
            var shared = new WallBreach(south, 8f, 9f, 1f, 1.5f, BreachKind.Hole);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage), shared);
            BreachFixtures.Collapsed(repo, w, 0);
            BreachFixtures.Damage(repo, w, 0, shared, new WallBreach(south, 1f, 1.5f, 1f, 1.5f, BreachKind.Hole));

            var section = TerrainObjectsSection.Write(repo)!;
            var entry = (JsonObject)Assert.Single(section).Value!;
            var rows = entry["breaches"]!.AsArray();
            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { 1f, 4f, 8f }, rows.Select(r => (float)r![1]!).ToArray());   // sorted by panel, from …
            Assert.True((bool)entry["collapsed"]!);
        }

        /// <summary>A building with more than 16 breaches (two entities) reloads in full: nothing is lost to the per-entity capacity. 🔴 Red-proof: drop the chunking ⇒ the 17th breach is lost.</summary>
        [Fact]
        public void MoreThanOneEntitysCapacity_ReloadsInFull()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = World(w);
            BreachFixtures.Damage(repo, w, 0, Enumerable.Range(0, 16).Select(i => new WallBreach(south, i * 0.6f, i * 0.6f + 0.3f, 1f, 1.5f, BreachKind.Hole)).ToArray());
            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 9.7f, 9.9f, 1f, 1.5f, BreachKind.Hole));
            Assert.Equal(17, DoorStates.Of(repo, w).Damage!.Count);
            Assert.Equal(17, Reload(w, TerrainObjectsSection.Write(repo)!).Count);
        }

        /// <summary>
        /// ⭐ A malformed entry FAILS LOUDLY, naming the key; an entry that is not a damage entry (a door, a future kind) is ignored. 🔴 Red-proof: make the reader skip a bad row ⇒ the throws below go green→red.
        /// </summary>
        [Theory]
        [InlineData("""{ "breaches": "x" }""", "not an array")]
        [InlineData("""{ "breaches": [ [0, 1, 2, 0, 2] ] }""", "[panel, from, to, bottom, top, kind]")]
        [InlineData("""{ "breaches": [ [0, 1, 2, 0, 2, "crater"] ] }""", "crater")]
        [InlineData("""{ "breaches": [ [-1, 1, 2, 0, 2, "hole"] ] }""", "panel")]
        [InlineData("""{ "breaches": [ [0, "a", 2, 0, 2, "hole"] ] }""", "numbers")]
        [InlineData("""{ "breaches": [ [0, 3, 2, 0, 2, "hole"] ] }""", "inverted")]
        [InlineData("""{ "collapsed": "yes" }""", "collapsed")]
        public void AMalformedEntry_Throws(string entry, string mentions)
        {
            var dom = (JsonObject)JsonNode.Parse("{ \"TerrainObjects\": { \"range/H\": " + entry + " } }")!;
            var ex = Assert.Throws<InvalidOperationException>(() => TerrainObjectsSection.ReadBuildingDamage(dom));
            Assert.Contains("range/H", ex.Message);
            Assert.Contains(mentions, ex.Message);
        }

        [Fact]
        public void AFutureObjectKind_AndADoor_AreIgnoredByTheDamageReader()
        {
            var dom = (JsonObject)JsonNode.Parse("""{ "TerrainObjects": { "range/H/front": { "door": "Open" }, "range/lamp": { "lit": true } } }""")!;
            Assert.Empty(TerrainObjectsSection.ReadBuildingDamage(dom));
            Assert.Single(TerrainObjectsSection.ReadDoors(dom));   // and the door reader ignores a damage entry
            var both = (JsonObject)JsonNode.Parse("""{ "TerrainObjects": { "range/H": { "breaches": [[0,1,2,0,2,"hole"]], "collapsed": true } } }""")!;
            Assert.Empty(TerrainObjectsSection.ReadDoors(both));
        }

        /// <summary>A building the terrain does not have, and a panel the building does not have, are warned and ignored — the rest of the building still loads. 🔴 Red-proof: drop the <c>ToGlobal &lt; 0</c> test ⇒ a breach on global panel −1.</summary>
        [Fact]
        public void ABuildingOrPanelTheTerrainDoesNotHave_IsWarnedAndIgnored()
        {
            var w = BreachFixtures.House();
            var section = (JsonObject)JsonNode.Parse("""
                { "range/Gone": { "breaches": [[0, 1, 2, 0, 2, "hole"]] },
                  "range/H": { "breaches": [[0, 1, 2, 0, 2, "passage"], [99, 1, 2, 0, 2, "hole"]] } }
                """)!;
            var warnings = new List<string>();
            var table = Reload(w, section, warnings);
            Assert.Equal(2, warnings.Count);
            Assert.Contains(warnings, m => m.Contains("range/Gone"));
            Assert.Contains(warnings, m => m.Contains("panel 99"));
            Assert.Equal(1, table.Count);
            Assert.Equal(BreachKind.Passage, table[0].Kind);
        }

        [Fact]
        public void PanelIndex_MapsBothWays()
        {
            var w = BreachFixtures.House();
            for (int p = 0; p < w.Panels.Count; p++)
            {
                int local = BuildingPanelIndex.ToLocal(w, p);
                Assert.True(local >= 0);
                Assert.Equal(p, BuildingPanelIndex.ToGlobal(w, w.Panels[p].Building, local));
            }
            Assert.Equal(-1, BuildingPanelIndex.ToLocal(w, w.Panels.Count));
            Assert.Equal(-1, BuildingPanelIndex.ToGlobal(w, 0, 999));
        }

        /// <summary>
        /// ⭐ CE-4427 R-3 — the panel index is built ONCE per world: 1 000 conversions both ways build the rank table once (no scan of the panels per breach), and a second world builds its own.
        /// 🔴 Red-proof: make <c>TableOf</c> build a fresh table on every call (no cache) ⇒ <c>TablesBuilt</c> grows with the calls.
        /// </summary>
        [Fact]
        public void CE4427_R3_PanelIndex_IsBuiltOncePerWorld()
        {
            var w = BreachFixtures.House();
            int before = BuildingPanelIndex.TablesBuilt;
            for (int i = 0; i < 1000; i++)
            {
                int p = i % w.Panels.Count;
                int local = BuildingPanelIndex.ToLocal(w, p);
                Assert.Equal(p, BuildingPanelIndex.ToGlobal(w, w.Panels[p].Building, local));
            }
            Assert.Equal(before + 1, BuildingPanelIndex.TablesBuilt);
            BuildingPanelIndex.ToLocal(BreachFixtures.House(), 0);
            Assert.Equal(before + 2, BuildingPanelIndex.TablesBuilt);
        }

        private const string TwoHouses = """
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "building", "label": "FIRST", "building": HOUSE }, "geometry": { "type": "Point", "coordinates": [20, 20] } },
              { "type": "Feature", "properties": { "kind": "building", "label": "SECOND", "building": HOUSE }, "geometry": { "type": "Point", "coordinates": [60, 20] } } ] }
            """;
        private const string HouseBody = """{ "footprint": [[0,0],[10,0],[10,8],[0,8]], "storeys": [ { "height": 3, "walls": [ { "from": [0,0], "to": [10,0], "thickness": 0.3 }, { "from": [10,0], "to": [10,8], "thickness": 0.3 }, { "from": [10,8], "to": [0,8], "thickness": 0.3 }, { "from": [0,8], "to": [0,0], "thickness": 0.3 } ] } ] }""";

        /// <summary>The two houses in either order: "FIRST" then "SECOND", or the reverse (a re-sorted overlay).</summary>
        private static TerrainWorld Two(bool swapped)
        {
            string json = TwoHouses.Replace("HOUSE", HouseBody);
            if (swapped) json = json.Replace("FIRST", "TMP").Replace("SECOND", "FIRST").Replace("TMP", "SECOND");   // labels swap places: the SAME key now names the other index
            return TerrainWorldParser.Parse(json, "range");
        }

        /// <summary>
        /// ⭐ CE-4427 R-4 — a damage component re-keys to the world that replaces its own: the building by stable KEY and each breach's panel by its within-building rank, so damage on "range/SECOND" stays on SECOND when
        /// the world is re-sorted. 🔴 Red-proof: make <c>Remap</c> return false at once (or keep the old numbers) ⇒ the damage sits on the other house.
        /// </summary>
        [Fact]
        public void CE4427_R4_ADamageComponent_FollowsItsHouseByKey_WhenTheWorldIsReSorted()
        {
            var a = Two(swapped: false);                                    // FIRST = 0, SECOND = 1
            var b = Two(swapped: true);                                     // the labels trade places: ONE key, two numberings
            int sa = Array.FindIndex(a.Buildings.ToArray(), x => x.Label == "SECOND");
            int sb = Array.FindIndex(b.Buildings.ToArray(), x => x.Label == "SECOND");
            Assert.NotEqual(sa, sb);
            int panelA = BuildingPanelIndex.ToGlobal(a, sa, 2);
            var d = new BuildingDamage { Building = sa };
            BuildingDamages.Add(ref d, new WallBreach(panelA, 4f, 6f, 0f, 2.2f, BreachKind.Passage), null);

            Assert.True(BuildingDamages.Remap(ref d, a, b));
            Assert.Equal(sb, d.Building);
            Assert.Equal(1, d.Count);
            Assert.Equal(sb, b.Panels[d.Get(0).Panel].Building);                       // the breach is on SECOND's panel in the NEW world
            Assert.Equal(2, BuildingPanelIndex.ToLocal(b, d.Get(0).Panel));            // the same wall of it
            Assert.False(BuildingDamages.Remap(ref d, b, b));                          // same world: nothing to do

            var gone = TerrainWorldParser.Parse(TwoHouses.Replace("HOUSE", HouseBody).Replace("SECOND", "OTHER"), "range");
            var d2 = new BuildingDamage { Building = sa };
            BuildingDamages.Add(ref d2, new WallBreach(panelA, 4f, 6f, 0f, 2.2f, BreachKind.Passage), null);
            Assert.True(BuildingDamages.Remap(ref d2, a, gone));                       // the house is gone from the new world
            Assert.Equal(-1, d2.Building);
            Assert.Equal(0, d2.Count);
        }

        /// <summary>
        /// ⭐ CE-4427 R-4 — the damage TABLE of one world laid over a re-sorted one breaches the same house: <c>RemapTo</c> keys by house and wall. 🔴 Red-proof: return <c>this</c> from <c>RemapTo</c> ⇒ the breach
        /// stays on the old index (the other house's panel).
        /// </summary>
        [Fact]
        public void CE4427_R4_ADamageTable_RemapsToAReSortedWorld_ByKey()
        {
            var a = Two(false); var b = Two(true);
            int sa = Array.FindIndex(a.Buildings.ToArray(), x => x.Label == "SECOND");
            int sb = Array.FindIndex(b.Buildings.ToArray(), x => x.Label == "SECOND");
            int panelA = BuildingPanelIndex.ToGlobal(a, sa, 1);
            using var repo = BreachFixtures.World(a);
            BreachFixtures.Damage(repo, a, sa, new WallBreach(panelA, 4f, 6f, 0f, 2.2f, BreachKind.Passage));
            var table = BuildingDamageTable.Of(repo, a)!;
            Assert.True(table.HasBreaches(panelA));

            var re = table.RemapTo(b);
            int panelB = BuildingPanelIndex.ToGlobal(b, sb, 1);
            Assert.True(re.HasBreaches(panelB));
            Assert.Equal(1, re.PassageCount);
            Assert.False(re.HasBreaches(BuildingPanelIndex.ToGlobal(b, 1 - sb, 1)));   // the other house is untouched
            Assert.Same(table, table.RemapTo(a));                                      // built for that world: itself
        }
    }
}
