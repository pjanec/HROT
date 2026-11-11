using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>⭐ O-5 (CE-4422, DESIGN_Terrain_Authoring §18d) — <see cref="TerrainBake.SaveAs"/>: base + the source's overlay → a new terrain folder that the one parser reads to the same world as <see cref="TerrainWorldMerge"/>.</summary>
    public sealed class TerrainBakeTests
    {
        private const string BlockGuid = "b1000000-0000-4000-8000-000000000001", HouseGuid = "a1000000-0000-4000-8000-000000000002";

        private sealed class Case : IDisposable
        {
            public readonly OverlayFixture F = new();
            public string Target => Path.Combine(F.Assets, "terrain", "saved-t");

            public Case(bool prefillBase = false)
            {
                if (prefillBase) new RecastNavmeshFactory { TileCache = NavTileCache.ForPrefill(Path.Combine(F.TerrainFolder, NavTileCache.TerrainFolderName)) }.Build(F.BaseWorld());
                var dom = F.Scenario((BlockGuid, OverlayFixture.Block("village", 11, 100, 100)), (HouseGuid, OverlayFixture.House("hut", 300, 300, 90)));
                var entities = (JsonObject)dom["entities"]!;
                entities["u1000000-0000-4000-8000-000000000003"] = new JsonObject { ["EntityInfo"] = new JsonObject { ["Name"] = "Rifleman" }, ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(5, 5, 0) } };
                entities["u1000000-0000-4000-8000-000000000004"] = new JsonObject { ["SpawnArea"] = new JsonObject { ["Side"] = 1 }, ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(9, 9, 0) } };
                dom["TerrainObjects"] = new JsonObject
                {
                    ["base-t/B/front"] = new JsonObject { ["door"] = "Locked" },
                    [$"base-t/{HouseGuid}/0/front"] = new JsonObject { ["door"] = "Open" },
                    ["base-t/B"] = new JsonObject { ["collapsed"] = true },   // damage from a test run: stays out (E-6)
                };
                File.WriteAllText(Path.Combine(F.ScenarioDir, "scenario.json"), dom.ToJsonString());
            }

            public TerrainSaveResult Save(bool bake = true, Action<string>? hook = null, Func<string, string?>? resolve = null)
                => TerrainBake.SaveAs(F.ScenarioDir, Target, "saved-t", resolve ?? F.Resolve, new[] { F.Recipes }, bake, hook);

            public TerrainWorld Saved() => TerrainFolderWorld.ReadWorld(Path.Combine(Target, "terrain.json"), TerrainFolderWorld.ReadDefinition(Path.Combine(Target, "terrain.json")), "saved-t")!;
            public void Dispose() => F.Dispose();
        }

        /// <summary>
        /// (a) Parsing the saved folder gives a world element-wise equal to <c>TerrainWorldMerge.Merge(base, overlay)</c> — buildings (rotation, template, storeys), panels, doors, walkables, surfaces, prisms, bounds;
        /// only the terrain-name prefix of door keys differs. 🔴 Red-proof: append the overlay's features BEFORE the base's ⇒ every index differs.
        /// </summary>
        [Fact]
        public void TheSavedFolder_ParsesToTheSameWorldAsTheMerge()
        {
            using var c = new Case();
            var r = c.Save();
            var overlay = TerrainFolderWorld.ReadOverlay(c.F.Overlay, "base-t", TerrainAssets.RecipesSibling(c.F.TerrainFolder)).World;
            var expected = TerrainWorldMerge.Merge(c.F.BaseWorld(), overlay);
            var saved = c.Saved();

            Assert.Equal(expected.BoundsMin, saved.BoundsMin); Assert.Equal(expected.BoundsMax, saved.BoundsMax);
            Assert.Equal(expected.Buildings.Count, saved.Buildings.Count);
            for (int i = 0; i < expected.Buildings.Count; i++)
            {
                var a = expected.Buildings[i]; var b = saved.Buildings[i];
                Assert.Equal((a.Label, a.Name, a.Template, a.Position, a.RotationDegrees, a.BaseZ, a.Solid), (b.Label, b.Name, b.Template, b.Position, b.RotationDegrees, b.BaseZ, b.Solid));
                Assert.Equal(a.StoreyZ, b.StoreyZ); Assert.Equal(a.Footprint, b.Footprint);
            }
            Assert.Equal(expected.Panels.Select(p => (p.A, p.B, p.Thickness, p.BaseZ, p.TopZ, p.Building, p.Storey, p.Label, p.Openings.Count)), saved.Panels.Select(p => (p.A, p.B, p.Thickness, p.BaseZ, p.TopZ, p.Building, p.Storey, p.Label, p.Openings.Count)));
            Assert.Equal(expected.Doors.Select(d => (d.Key.Substring("base-t/".Length), d.Panel, d.Opening, d.Initial, d.Center, d.SillZ)), saved.Doors.Select(d => (d.Key.Substring("saved-t/".Length), d.Panel, d.Opening, d.Initial, d.Center, d.SillZ)));
            Assert.Equal(expected.Walkables.Select(w => (w.Kind, w.Building, w.MinZ, w.MaxZ)), saved.Walkables.Select(w => (w.Kind, w.Building, w.MinZ, w.MaxZ)));
            Assert.Equal(expected.Walkables.Select(w => w.Vertices), saved.Walkables.Select(w => w.Vertices));
            Assert.Equal(expected.Surfaces.Select(s => (s.Type, s.Density, s.Canopy)), saved.Surfaces.Select(s => (s.Type, s.Density, s.Canopy)));
            Assert.Equal(expected.Surfaces.Select(s => s.Polygon), saved.Surfaces.Select(s => s.Polygon));
            Assert.Equal(expected.Prisms.Select(p => (p.Kind, p.BaseZ, p.TopZ, p.Label, p.Panel, p.Building, p.Material?.Name)), saved.Prisms.Select(p => (p.Kind, p.BaseZ, p.TopZ, p.Label, p.Panel, p.Building, p.Material?.Name)));
            Assert.Equal(expected.Prisms.Select(p => p.Footprint), saved.Prisms.Select(p => p.Footprint));
            Assert.True(File.Exists(Path.Combine(r.Folder, "buildings", "hut.building.json")), "the saved terrain carries the templates it places");
        }

        /// <summary>(b) Loading the saved terrain's navmesh bakes 0 tiles — every one is a hit from the saved navtiles (the base's + the overlay's). 🔴 Red-proof: leave the overlay's navtiles out of the copy ⇒ tiles are baked.</summary>
        [Fact]
        public void TheSavedTerrainsNavmesh_BakesNothing_AtLoad()
        {
            using var c = new Case(prefillBase: true);
            c.Save();
            var factory = new RecastNavmeshFactory { TileCache = new NavTileCache() };   // a node's fresh cache: memory only
            factory.Build(c.Saved());
            Assert.True(factory.LastStats.Tiles > 0);
            Assert.Equal(0, factory.LastStats.Baked);
            Assert.Equal(factory.LastStats.Tiles, factory.LastStats.FromCache);
        }

        /// <summary>(c) The saved terrain's content hash differs from the base's, and the <c>source/</c> folder does not move it. 🔴 Red-proof: hash <c>source/</c> too ⇒ the second assertion fails.</summary>
        [Fact]
        public void TheSavedTerrainsHash_DiffersFromTheBases_AndIgnoresTheSourceFolder()
        {
            using var c = new Case();
            var r = c.Save();
            string def = Path.Combine(r.Folder, "terrain.json");
            string baseHash = TerrainContentHash.OfTerrain(c.F.Definition);
            Assert.NotEqual(baseHash, r.ContentHash);
            Assert.Equal(r.ContentHash, TerrainContentHash.OfTerrain(def));
            File.WriteAllText(Path.Combine(r.Folder, "source", "scenario", "extra.txt"), "an edit to the source");
            File.AppendAllText(Path.Combine(r.Folder, "source", "base", "w.geojson"), " ");
            File.Delete(Path.Combine(r.Folder, TerrainContentHash.CacheFileName));
            Assert.Equal(r.ContentHash, TerrainContentHash.OfTerrain(def));
            var j = JsonNode.Parse(File.ReadAllText(def))!;
            Assert.Equal("saved-t", (string)j["name"]!); Assert.Equal("base-t", (string)j["source"]!["base"]!["name"]!); Assert.Equal(baseHash, (string)j["source"]!["base"]!["contentHash"]!);
            Assert.Equal("source/scenario", (string)j["source"]!["scenario"]!);
            Assert.True(File.Exists(Path.Combine(r.Folder, "source", "scenario", "scenario.json"))); Assert.False(Directory.Exists(Path.Combine(r.Folder, "source", "scenario", "terrain-overlay")));
            Assert.True(File.Exists(Path.Combine(r.Folder, "source", "base", "terrain.json"))); Assert.False(Directory.Exists(Path.Combine(r.Folder, "source", "base", "source")));
        }

        /// <summary>(d) A door state authored in the source resolves on the saved terrain through the new scenario (keys re-prefixed); the new scenario carries no entities and no damage. 🔴 Red-proof: skip the re-prefix ⇒ the keys resolve to no door.</summary>
        [Fact]
        public void ADoorStateAuthoredInTheSource_ResolvesOnTheSavedTerrain()
        {
            using var c = new Case();
            var r = c.Save();
            var saved = c.Saved();
            var doors = TerrainObjectsSection.ReadDoors(r.Scenario);
            Assert.Equal(2, doors.Count);
            Assert.All(doors, kv => Assert.True(saved.DoorIndexOf(kv.Key) >= 0, $"'{kv.Key}' names no door of the saved terrain"));
            Assert.Equal(TerrainDoorState.Locked, doors["saved-t/B/front"]);
            Assert.Equal(TerrainDoorState.Open, doors[$"saved-t/{HouseGuid}/0/front"]);
            Assert.Equal("saved-t", (string)r.Scenario["header"]!["terrainName"]!);
            Assert.Empty(r.Scenario["entities"]!.AsObject());
            Assert.Empty(TerrainObjectsSection.ReadBuildingDamage(r.Scenario));
        }

        /// <summary>(e) A re-save over the same target works and keeps <c>source/</c> — also from the saved terrain's own <c>source/scenario</c> when the original base is gone (W-4: the base is its own <c>source/base</c>). 🔴 Red-proof: skip the <c>source/base</c> fallback ⇒ the second save throws.</summary>
        [Fact]
        public void AReSave_OverTheSameTarget_Works_AndKeepsTheSource_EvenWhenTheOriginalBaseIsGone()
        {
            using var c = new Case();
            var first = c.Save();
            var again = c.Save();
            Assert.Equal(first.ContentHash, again.ContentHash);   // the same inputs, the same terrain
            Assert.True(File.Exists(Path.Combine(c.Target, "source", "scenario", "scenario.json")));
            Assert.False(Directory.Exists(c.Target + ".part")); Assert.False(Directory.Exists(c.Target + ".old"));

            // W-4: the scenario is the terrain's own source; the original base is deleted — its kept copy stands in
            Directory.Delete(c.F.TerrainFolder, true);
            string sourceScenario = Path.Combine(c.Target, "source", "scenario");
            var resave = TerrainBake.SaveAs(sourceScenario, c.Target, "saved-t", _ => null, new[] { c.F.Recipes });
            Assert.Equal(first.ContentHash, resave.ContentHash);
            Assert.True(File.Exists(Path.Combine(c.Target, "source", "base", "terrain.json")));
            Assert.NotEmpty(c.Saved().Buildings.Where(b => b.Label.StartsWith(BlockGuid)));
        }

        /// <summary>(f) A failed save leaves the target exactly as it was and no <c>.part</c>. 🔴 Red-proof: delete the target before the part is complete ⇒ the target is gone.</summary>
        [Fact]
        public void AFailedSave_LeavesTheTargetUntouched_AndNoPart()
        {
            using var c = new Case();
            Directory.CreateDirectory(c.Target);
            File.WriteAllText(Path.Combine(c.Target, "keep.txt"), "the old terrain");
            Assert.Throws<InvalidOperationException>(() => c.Save(hook: _ => throw new InvalidOperationException("disk full")));
            Assert.Equal("the old terrain", File.ReadAllText(Path.Combine(c.Target, "keep.txt")));
            Assert.False(Directory.Exists(c.Target + ".part")); Assert.False(Directory.Exists(c.Target + ".old"));
            Assert.Empty(Directory.GetFiles(c.Target, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith("keep.txt")));
            // and a save that fails before the part exists (a recipe that is not there)
            File.WriteAllText(Path.Combine(c.F.Recipes, "village.recipe.json"), "{ not json");
            Assert.ThrowsAny<Exception>(() => c.Save());
            Assert.Equal("the old terrain", File.ReadAllText(Path.Combine(c.Target, "keep.txt")));
            Assert.False(Directory.Exists(c.Target + ".part"));
        }

        /// <summary>(g) The summary: houses / woods / trees / wall km / bridges from the overlay, what is NOT baked (units, areas, damaged houses), the tiles; and <c>TerrainUsers.Of</c> finds the scenarios naming a terrain.</summary>
        [Fact]
        public void TheSummary_CountsTheOverlaysParts_AndWhatIsNotBaked_AndTerrainUsersFindsScenarios()
        {
            using var c = new Case(prefillBase: true);
            var r = c.Save();
            var s = r.Summary;
            Assert.Equal(11, s.Houses);                       // ten from the village block + the placed house
            Assert.InRange(s.Woods, 1, 2);
            Assert.Equal(0, s.Trees); Assert.Equal(0.0, s.WallKm); Assert.Equal(0, s.Bridges);
            var block = Assert.Single(s.Blocks);
            Assert.Equal((BlockGuid, "village", 10), (block.Key, block.Recipe, block.Houses));
            Assert.Equal(2, s.UnitsNotBaked); Assert.Equal(1, s.AreasNotBaked); Assert.Equal(1, s.DamagedHousesNotBaked);
            Assert.True(s.TilesTotal > 0); Assert.Equal(0, s.TilesToBake); Assert.Equal(0.0, s.EstimateSeconds);

            var unbaked = c.Save(bake: false).Summary;        // the navtiles are not baked at save: unknown to bake, a bounds estimate of the total
            Assert.Null(unbaked.TilesToBake); Assert.Null(unbaked.EstimateSeconds); Assert.True(unbaked.TilesTotal > 0);

            // two more scenarios: one naming base-t, one naming another terrain; the saved terrain's own source/ is not a scenario
            string roots = Path.Combine(c.F.Root, "scenarios");
            foreach (var (name, terrain) in new[] { ("s2", "base-t"), ("s3", "elsewhere") })
            {
                Directory.CreateDirectory(Path.Combine(roots, name));
                File.WriteAllText(Path.Combine(roots, name, "scenario.json"), $"{{\"Header\":{{\"TerrainName\":\"{terrain}\"}},\"Entities\":{{}}}}");
            }
            var users = TerrainUsers.Of("base-t", new[] { roots, Path.Combine(c.Target) });
            Assert.Equal(new[] { Path.Combine(roots, "s1"), Path.Combine(roots, "s2") }, users.ToArray());
            Assert.Equal(new[] { Path.Combine(roots, "s3") }, TerrainUsers.Of("elsewhere", new[] { roots }).ToArray());
        }
    }
}
