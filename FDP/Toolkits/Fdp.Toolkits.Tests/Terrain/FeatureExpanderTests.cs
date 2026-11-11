using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18c) — <see cref="FeatureExpander"/>: pure, seeded, fitted to the site. Rails run on a throw-away base (<see cref="OverlayFixture"/>).</summary>
    public sealed class FeatureExpanderTests
    {
        private static string Json(FeatureExpansion x) => new JsonArray(x.Features.Select(f => (JsonNode)f.DeepClone()).ToArray()).ToJsonString();

        /// <summary>The features as a reader of the file sees them (parsed back from the bytes).</summary>
        private static System.Collections.Generic.IEnumerable<JsonObject> Feats(FeatureExpansion x) => x.Features.Select(f => (JsonObject)JsonNode.Parse(f.ToJsonString())!);

        private static FeatureExpansion Run(OverlayFixture f, uint seed, byte fit = TerrainBlock.FitAll, string recipe = "village")
            => FeatureExpander.Expand(f.Recipe(recipe), seed, new Vector2(100, 100), 0f, fit, f.BaseWorld(), "g1", TerrainAssets.ForFolder(f.TerrainFolder));

        /// <summary>The same input is the same bytes; another seed is another layout. 🔴 Red-proof: draw from <c>System.Random</c> / an unseeded value ⇒ the first assertion fails; ignore the seed ⇒ the second.</summary>
        [Fact]
        public void TheSameInput_IsByteIdentical_AndAnotherSeedIsNot()
        {
            using var f = new OverlayFixture();
            string a = Json(Run(f, 7)), b = Json(Run(f, 7)), c = Json(Run(f, 8));
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.Equal(10, Run(f, 7).Houses); Assert.InRange(Run(f, 7).Woods, 1, 2);   // a wood centred in the lake is dropped
            Assert.StartsWith("g1/", ((JsonObject)Run(f, 7).Features[0]["properties"]!)["label"]!.GetValue<string>());
        }

        /// <summary>
        /// Fit: with a house and a lake inside the recipe's radius, no placed house's footprint box meets the base house's or another placed house's, and no corner or centre is in the lake.
        /// 🔴 Red-proof: skip the water test or the building-overlap test ⇒ a house lands in the lake / on the base house (the layouts are dense: radius 60, ten 10 × 8 m houses around them).
        /// </summary>
        [Fact]
        public void NoHouseOverlapsABaseBuilding_OrAnotherHouse_OrSitsInWater()
        {
            using var f = new OverlayFixture();
            var world = f.BaseWorld();
            foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var boxes = new System.Collections.Generic.List<(Vector2 Min, Vector2 Max)> { (new Vector2(100, 100), new Vector2(110, 108)) };   // the base house "B"
                foreach (var feat in Feats(Run(f, seed)))
                {
                    var p = (JsonObject)feat["properties"]!;
                    if (p["kind"]!.GetValue<string>() != "building") continue;
                    var c = (JsonArray)((JsonObject)feat["geometry"]!)["coordinates"]!;
                    var at = new Vector2((float)c[0]!.GetValue<double>(), (float)c[1]!.GetValue<double>());
                    float rot = (float)p["rotation"]!.GetValue<double>() * MathF.PI / 180f, co = MathF.Cos(rot), si = MathF.Sin(rot);
                    var corners = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 8), new Vector2(0, 8) }.Select(q => at + new Vector2(q.X * co - q.Y * si, q.X * si + q.Y * co)).ToArray();
                    var min = corners.Aggregate(Vector2.Min); var max = corners.Aggregate(Vector2.Max);
                    foreach (var o in boxes) Assert.False(min.X < o.Max.X && max.X > o.Min.X && min.Y < o.Max.Y && max.Y > o.Min.Y, $"seed {seed}: a house at {at} overlaps another building");
                    boxes.Add((min, max));
                    foreach (var q in corners.Append(at)) Assert.False(q.X is >= 125 and <= 175 && q.Y is >= 70 and <= 130, $"seed {seed}: a house point {q} is in the lake");
                }
            }
        }

        /// <summary>The unfitted block ignores the sites: with every bit off, some seed puts a house in the lake or on the base house (so the fit rail above is not vacuous).</summary>
        [Fact]
        public void WithoutFit_TheSameLayoutsDoLandOnTheSite()
        {
            using var f = new OverlayFixture();
            bool bad = false;
            foreach (uint seed in Enumerable.Range(1, 8).Select(i => (uint)i))
                foreach (var feat in Feats(Run(f, seed, 0)))
                {
                    if (((JsonObject)feat["properties"]!)["kind"]!.GetValue<string>() != "building") continue;
                    var c = (JsonArray)((JsonObject)feat["geometry"]!)["coordinates"]!;
                    float x = (float)c[0]!.GetValue<double>(), y = (float)c[1]!.GetValue<double>();
                    bad |= x is >= 125 and <= 175 && y is >= 70 and <= 130;
                }
            Assert.True(bad);
        }

        /// <summary>LevelPads writes <c>pad</c> (the base ground at the centre), the bit off writes none; AlignRoad is NOT implemented: that bit changes nothing.</summary>
        [Fact]
        public void LevelPads_WritesAPad_AndAlignRoadIsIgnored()
        {
            using var f = new OverlayFixture();
            Assert.All(Feats(Run(f, 3)).Where(x => ((JsonObject)x["properties"]!)["kind"]!.GetValue<string>() == "building"), x => Assert.NotNull(((JsonObject)x["properties"]!)["pad"]));
            Assert.All(Feats(Run(f, 3, TerrainBlock.AvoidWater | TerrainBlock.AvoidBuildings)).Where(x => ((JsonObject)x["properties"]!)["kind"]!.GetValue<string>() == "building"), x => Assert.Null(((JsonObject)x["properties"]!)["pad"]));
            Assert.Equal(Json(Run(f, 3, 15)), Json(Run(f, 3, 14)));   // bit 1 (AlignRoad) changes nothing
        }

        /// <summary>The three shipped recipes parse and name existing templates.</summary>
        [Fact]
        public void TheShippedRecipes_Parse_AndTheirTemplatesExist()
        {
            string root = ContentLayout.TryFindSourceTree()!;
            foreach (var id in new[] { "hamlet", "farmstead", "copse" })
            {
                var r = TerrainRecipe.Find(id, new[] { ContentLayout.TerrainRecipeAssets(root) });
                Assert.Equal(id, r.Id);
                foreach (var t in r.Templates) Assert.True(File.Exists(Path.Combine(ContentLayout.BuildingAssets(root), t + ".building.json")), t);
            }
        }
    }
}
