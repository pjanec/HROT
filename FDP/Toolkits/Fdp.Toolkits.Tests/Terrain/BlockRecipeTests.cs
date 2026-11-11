using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ T-3 (CE-4430, docs/DESIGN_Terrain_Authoring.md §18i) — the TEN shipped block recipes (the mockup's tiles) over the shipped test-town: each expands byte-identically twice, another seed is another layout, each makes the kinds
    /// its tile shows (counts in the tile's range), every part parses into a world, and each template a recipe names exists. Generation: proof is determinism + the kinds + real frames (<c>TerrainOverlayFrameRail</c>), not a mutation matrix.
    /// </summary>
    public sealed class BlockRecipeTests
    {
        private static readonly string[] Ten = { "farmstead", "hamlet", "village-street", "town-block", "checkpoint", "roadblock", "defensive-position", "copse", "orchard", "hedgerow-fields" };
        private static readonly Vector2 At = new(345f, 180f);

        private static (string Root, TerrainWorld Base, TerrainAssets Assets, string Folder) Site()
        {
            string root = ContentLayout.TryFindSourceTree()!;
            string folder = Path.Combine(ContentLayout.TerrainAssets(root), "test-town");
            string def = Path.Combine(folder, "terrain.json");
            return (root, TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "test-town")!, TerrainAssets.ForFolder(folder), folder);
        }

        private static FeatureExpansion Run(string id, uint seed, byte fit = TerrainBlock.FitAll, float heading = 0f)
        {
            var (root, world, assets, _) = Site();
            var recipe = TerrainRecipe.Find(id, new[] { ContentLayout.TerrainRecipeAssets(root) });
            return FeatureExpander.Expand(recipe, seed, At, heading, fit, world, "g1", assets);
        }

        private static string Json(FeatureExpansion x) => new JsonArray(x.Features.Select(f => (JsonNode)f.DeepClone()).ToArray()).ToJsonString();

        private static IEnumerable<JsonObject> Props(FeatureExpansion x) => x.Features.Select(f => (JsonObject)f["properties"]!);

        private static int Count(FeatureExpansion x, Func<JsonObject, bool> pick) => Props(x).Count(pick);
        private static string K(JsonObject p) => (string?)p["kind"] ?? "";
        private static bool Hedge(JsonObject p) => K(p) == "wall" && (string?)p["material"] == "hedge";
        private static bool Open(JsonObject p) => K(p) == "surface" && (string?)p["surface"] == "open";

        /// <summary>🔴 Red-proof: draw from an unseeded source in an element ⇒ the first assertion fails; ignore the seed ⇒ the second.</summary>
        [Fact]
        public void EachShippedRecipe_ExpandsByteIdenticallyTwice_AndAnotherSeedIsAnotherLayout()
        {
            foreach (var id in Ten)
            {
                Assert.Equal(Json(Run(id, 11)), Json(Run(id, 11)));
                if (id is "checkpoint" or "roadblock" or "defensive-position") continue;   // fixed placements: the seed moves only the jitter of the clutter's facing
                Assert.NotEqual(Json(Run(id, 11)), Json(Run(id, 12)));
            }
        }

        /// <summary>Each recipe makes what its mockup tile shows (house counts in range, hedges, lanes, prisms, trees).</summary>
        [Fact]
        public void EachRecipe_MakesTheKindsItsTileShows()
        {
            var farm = Run("farmstead", 11);
            Assert.InRange(farm.Houses, 3, 5); Assert.True(Count(farm, Hedge) >= 1, "the yard's hedge"); Assert.True(Count(farm, Open) >= 1, "the track in"); Assert.True(Count(farm, p => K(p) == "prism") >= 3, "crates and barrels");

            var ham = Run("hamlet", 11);
            Assert.InRange(ham.Houses, 4, 8); Assert.True(Count(ham, Open) == 1, "the lane");

            var vil = Run("village-street", 11);
            Assert.InRange(vil.Houses, 9, 12); Assert.True(Count(vil, Hedge) >= 6, "gardens"); Assert.Equal(1, Count(vil, Open)); Assert.True(Count(vil, p => K(p) == "prism") >= 1, "cars");

            var town = Run("town-block", 11);
            Assert.True(town.Houses >= 14, $"a street grid of houses ({town.Houses})"); Assert.InRange(Count(town, Open), 4, 5); Assert.True(Count(town, p => K(p) == "prism") >= 3, "cars");
            Assert.True(Props(town).Where(p => K(p) == "building").Select(p => (string)p["template"]!).Distinct().Count() >= 2, "3–6-storey templates");

            Assert.Equal(9, Count(Run("checkpoint", 11), p => K(p) == "prism"));   // 4 barriers, a booth, 3 sandbag walls, a car
            Assert.InRange(Count(Run("roadblock", 11), p => K(p) == "prism"), 7, 9);
            var def = Run("defensive-position", 11);
            Assert.InRange(Count(def, p => K(p) == "prism"), 11, 14); Assert.Equal(1, Count(def, Open));

            Assert.InRange(Count(Run("copse", 11), p => K(p) == "surface" && (string?)p["surface"] == "forest"), 2, 5);
            Assert.InRange(Count(Run("orchard", 11), p => K(p) == "tree"), 30, 48);
            var fields = Run("hedgerow-fields", 11);
            Assert.True(Count(fields, Hedge) >= 10, "the hedges edging the fields"); Assert.Equal(0, fields.Houses);
        }

        /// <summary>A block placed turned (AlignRoad's heading) lays out as the same layout turned: the same parts, each of them moved — never a different count.</summary>
        [Fact]
        public void ATurnedBlock_HasTheSamePartCounts()
        {
            foreach (var id in new[] { "village-street", "town-block", "checkpoint", "hedgerow-fields" })
            {
                var a = Run(id, 11, 0, 0f); var b = Run(id, 11, 0, 37f);   // fit off: the water and the base's houses would drop different parts
                Assert.Equal(a.Features.Count, b.Features.Count);
            }
        }

        /// <summary>Every recipe's parts parse into a world (a typo in a part would throw here) and the recipes name templates that exist.</summary>
        [Fact]
        public void EachRecipe_ParsesIntoAWorld_AndItsTemplatesExist()
        {
            var (root, world, assets, _) = Site();
            foreach (var id in Ten)
            {
                var recipe = TerrainRecipe.Find(id, new[] { ContentLayout.TerrainRecipeAssets(root) });
                Assert.Equal(id, recipe.Id);
                var names = recipe.Templates.Concat(recipe.Elements.Where(e => e["templates"] is JsonArray).SelectMany(e => ((JsonArray)e["templates"]!).Select(t => (string)t!))).Distinct();
                foreach (var t in names) Assert.True(File.Exists(Path.Combine(ContentLayout.BuildingAssets(root), t + ".building.json")), $"{id}: template {t}");
                var x = FeatureExpander.Expand(recipe, 11, At, 0f, TerrainBlock.FitAll, world, "g1", assets);
                var parsed = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(world, x.Features).ToJsonString(), "test-town", assets);
                Assert.NotNull(parsed);
            }
        }

        /// <summary>A recipe with an unknown element type, or one missing what it needs, is a parse error (a typo is not an empty block).</summary>
        [Fact]
        public void ABadElement_IsAParseError()
        {
            Assert.Throws<ArgumentException>(() => TerrainRecipe.Parse("""{"id":"x","elements":[{"type":"wibble"}]}"""));
            Assert.Throws<ArgumentException>(() => TerrainRecipe.Parse("""{"id":"x","elements":[{"type":"row","from":[0,0]}]}"""));
            Assert.Throws<ArgumentException>(() => TerrainRecipe.Parse("""{"id":"x","elements":[{"type":"group","items":[{"type":"unicorn","at":[[0,0]]}]}]}"""));
            Assert.Equal("x", TerrainRecipe.Parse("""{"id":"x","elements":[{"type":"lane","from":[0,0],"to":[10,0]}]}""").Id);
        }
    }
}
