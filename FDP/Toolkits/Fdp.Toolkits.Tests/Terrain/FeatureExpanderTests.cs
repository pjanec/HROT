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

        /// <summary>LevelPads writes <c>pad</c> (the base ground at the centre), the bit off writes none; with no road net (T-5) the AlignRoad bit changes nothing.</summary>
        [Fact]
        public void LevelPads_WritesAPad_AndAlignRoadWithoutARoadNetChangesNothing()
        {
            using var f = new OverlayFixture();
            Assert.All(Feats(Run(f, 3)).Where(x => ((JsonObject)x["properties"]!)["kind"]!.GetValue<string>() == "building"), x => Assert.NotNull(((JsonObject)x["properties"]!)["pad"]));
            Assert.All(Feats(Run(f, 3, TerrainBlock.AvoidWater | TerrainBlock.AvoidBuildings)).Where(x => ((JsonObject)x["properties"]!)["kind"]!.GetValue<string>() == "building"), x => Assert.Null(((JsonObject)x["properties"]!)["pad"]));
            Assert.Equal(Json(Run(f, 3, 15)), Json(Run(f, 3, 14)));   // bit 1 (AlignRoad) changes nothing: no road net was passed
        }

        // ── ⭐ T-5 (CE-3431, §18h H-5) — AlignRoad ───────────────────────────────────────────────────────────────────

        private const float TrackDeg = 30f;   // the track's direction: not a multiple of 90, so an aligned house is told from an unaligned one

        /// <summary>A track 8.7 m from the block's centre (300, 300), running at <see cref="TrackDeg"/>.</summary>
        private static TerrainRoads Track(float deg = TrackDeg, float offsetFromCentre = 10f)
        {
            float r = deg * MathF.PI / 180f; var d = new Vector2(MathF.Cos(r), MathF.Sin(r));
            var through = new Vector2(300f, 300f + offsetFromCentre);
            return TerrainRoads.FromEdges(new[] { (through - d * 120f, through + d * 120f) });
        }

        private static FeatureExpansion Farmstead(OverlayFixture f, TerrainRoads? roads, byte fit = TerrainBlock.FitAll, float heading = 0f)
        {
            File.WriteAllText(Path.Combine(f.Recipes, "farmstead.recipe.json"), """{"id":"farmstead","houses":{"templates":["hut"],"count":[3,3],"radius":45,"minSpacing":12},"woods":{"count":[1,1],"size":[10,14]}}""");
            return FeatureExpander.Expand(f.Recipe("farmstead"), 5, new Vector2(300, 300), heading, fit, f.BaseWorld(), "g1", TerrainAssets.ForFolder(f.TerrainFolder), roads);
        }

        private static float[] Rotations(FeatureExpansion x)
            => Feats(x).Where(b => ((JsonObject)b["properties"]!)["kind"]!.GetValue<string>() == "building").Select(b => (float)((JsonObject)b["properties"]!)["rotation"]!.GetValue<double>()).ToArray();

        private static float OffAxis(float rotation, float axis) { float d = (((rotation - axis) % 90f) + 90f) % 90f; return MathF.Min(d, 90f - d); }

        /// <summary>
        /// ⭐ a farmstead placed near a track turns to face it: every house's rotation is the track's direction (or its opposite — and the quarter turns of the recipe) to within 1°. Not vacuous: with no road the same block's
        /// houses stand at the quarter turns of the PLACED heading. 🔴 Red-proof: drop the <c>AlignRoad</c> branch in <c>FeatureExpander.Expand</c> ⇒ the houses keep the placed heading ⇒ red.
        /// </summary>
        [Fact]
        public void T5_AFarmsteadNearATrack_TurnsToFaceIt()
        {
            using var f = new OverlayFixture();
            var turned = Rotations(Farmstead(f, Track()));
            Assert.Equal(3, turned.Length);
            Assert.All(turned, r => Assert.True(OffAxis(r, TrackDeg) < 1f, $"rotation {r} is not the track's {TrackDeg}° (or its opposite / a quarter turn)"));
            Assert.All(Rotations(Farmstead(f, null)), r => Assert.True(OffAxis(r, 0f) < 1f && OffAxis(r, TrackDeg) > 20f));
        }

        /// <summary>
        /// ⭐ the direction picked: of the edge's two directions (it and its opposite) the one nearer the PLACED heading, so a block turns at most 90°. A track at 150° with the block placed at 0° turns it to −30° (330°), not 150°; placed at 180°
        /// it turns to 150°. 🔴 Red-proof: drop the <c>Abs(diff) &gt; 90</c> flip in <c>TerrainRoads.TryAlign</c> ⇒ the first case is 150° ⇒ red.
        /// </summary>
        [Fact]
        public void T5_TheBlockTurnsToTheNearerOfTheTracksTwoDirections_NeverMoreThanAQuarter()
        {
            var roads = Track(150f);
            Assert.True(roads.TryAlign(new Vector2(300, 300), 45f, 0f, out float a0));
            Assert.Equal(330f, ((a0 % 360f) + 360f) % 360f, 1);
            Assert.True(roads.TryAlign(new Vector2(300, 300), 45f, 180f, out float a180));
            Assert.Equal(150f, ((a180 % 360f) + 360f) % 360f, 1);
            foreach (float placed in new[] { 0f, 40f, 100f, 179f, 250f, 359f })
            {
                Assert.True(roads.TryAlign(new Vector2(300, 300), 45f, placed, out float a));
                float turn = MathF.Abs((((a - placed) % 360f) + 540f) % 360f - 180f);
                Assert.True(turn <= 90.01f, $"placed {placed}° turned {turn}°");
            }
        }

        /// <summary>
        /// ⭐ no road in reach ⇒ the heading as placed: a track farther than the recipe's radius (45 m), the AlignRoad bit off, and no road net at all each give the BYTES of the plain block.
        /// 🔴 Red-proof: ignore the radius in <c>TryAlign</c> (<c>best = float.MaxValue</c>) ⇒ the far track turns the block ⇒ red; ignore the bit ⇒ the second case reds.
        /// </summary>
        [Fact]
        public void T5_WithNoRoadInReach_TheHeadingIsAsPlaced()
        {
            using var f = new OverlayFixture();
            string plain = Json(Farmstead(f, null));
            Assert.Equal(plain, Json(Farmstead(f, Track(offsetFromCentre: 80f))));                                    // 70 m away: out of reach
            Assert.Equal(Json(Farmstead(f, null, TerrainBlock.AvoidWater | TerrainBlock.AvoidBuildings | TerrainBlock.LevelPads)),
                         Json(Farmstead(f, Track(), TerrainBlock.AvoidWater | TerrainBlock.AvoidBuildings | TerrainBlock.LevelPads)));   // the bit off
            Assert.Equal(plain, Json(Farmstead(f, TerrainRoads.FromEdges(Array.Empty<(Vector2, Vector2)>()))));        // a net with no road
            Assert.NotEqual(plain, Json(Farmstead(f, Track())));                                                        // …and the track in reach does turn it
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
