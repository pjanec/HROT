using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ T-2 (CE-4429, docs/DESIGN_Terrain_Authoring.md §18g) — the kinds of a <see cref="TerrainFeature"/> beyond the house, compiled by the ONE expander and merged into the base: a hedge blocks movement, a wood conceals,
    /// water is impassable, a unit crosses a bridge and stops crossing it once it collapses, a scatter compiles byte-identically, clutter round-trips through the overlay file. (The kinds' own suite: <c>FeatureExpanderTests</c> is the
    /// block's.) The base is <see cref="OverlayFixture"/>'s 400 × 400 m terrain.
    /// </summary>
    public sealed class FeatureKindsTests : IDisposable
    {
        private readonly OverlayFixture _f = new();
        public void Dispose() => _f.Dispose();

        private const string G1 = "c1000000-0000-4000-8000-000000000001", G2 = "c2000000-0000-4000-8000-000000000002", G3 = "c3000000-0000-4000-8000-000000000003";

        private TerrainWorld Merged(params (string Guid, JsonObject Entity)[] entities)
        {
            _f.Compile(_f.Scenario(entities));
            var overlay = TerrainFolderWorld.ReadOverlay(_f.Overlay, "base-t", TerrainAssets.RecipesSibling(_f.TerrainFolder));
            return TerrainWorldMerge.Merge(_f.BaseWorld(), overlay.World);
        }

        private static INavmeshProvider Nav(TerrainWorld w) => new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = new NavTileCache() }.Build(w) ?? throw new InvalidOperationException("no navmesh");

        // ── a hedge blocks movement ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A HEDGE (a wall of the <c>hedge</c> material, 0.8 m thick) across a field: the path from one side to the other goes round its end, and no waypoint is inside it. 🔴 Red-proof: have <c>OfKind</c> expand a hedge to nothing ⇒
        /// the path runs straight through (two waypoints).
        /// </summary>
        [Fact]
        public void AHedge_BlocksMovement_ThePathGoesRound()
        {
            var world = Merged((G1, OverlayFixture.Feature(TerrainFeature.Hedge, 300, 250, "", 0, 0, 0, (0, -40), (0, 40))));
            var part = Assert.Single(world.Panels.Where(p => p.Material?.Name == "hedge").Take(1));
            Assert.Equal("hedge", part.Material!.Name);
            var nav = Nav(world);
            Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
            int n = nav.PlanPath(new Vector3(280, 250, 0), new Vector3(320, 250, 0), wps, (uint)NavLayerMask.Infantry);
            Assert.True(n >= 3, $"expected a detour round the hedge, got {n} waypoint(s)");
            for (int i = 0; i < n; i++)
                Assert.False(wps[i].Position.X > 299.5f && wps[i].Position.X < 300.5f && MathF.Abs(wps[i].Position.Y - 250f) < 40f, $"waypoint {i} {wps[i].Position} is inside the hedge");
        }

        // ── a wood conceals ───────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A WOOD feature (a forest surface from its polygon) conceals as the base's woods do: 100 m apart inside it is hidden, 20 m is seen. 🔴 Red-proof: expand a wood to an <c>open</c> surface ⇒ 100 m is seen.</summary>
        [Fact]
        public void AWood_Conceals()
        {
            var world = Merged((G1, OverlayFixture.Feature(TerrainFeature.Wood, 250, 300, "", 0, 0, 0, (-150, -80), (150, -80), (150, 80), (-150, 80))));
            Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Forest);
            Vector3 Man(float x, float y) => new(x, y, 1.7f);
            Assert.False(world.SegmentBlocked(Man(250, 300), Man(270, 300)), "20 m apart in the wood: seen");
            Assert.True(world.SegmentBlocked(Man(200, 300), Man(300, 300)), "100 m apart in the wood: hidden");
            Assert.False(world.SegmentBlocked(Man(20, 20), Man(120, 20)), "100 m apart outside it: seen");
        }

        // ── water is impassable ───────────────────────────────────────────────────────────────────────────────────

        private static JsonObject River() => OverlayFixture.Feature(TerrainFeature.Water, 215, 200, "", 0, 0, 0, (-15, -200), (15, -200), (15, 200), (-15, 200));

        /// <summary>A WATER feature across the whole terrain cuts it in two: no path from one bank to the other. 🔴 Red-proof: expand water to an <c>open</c> surface ⇒ a path exists.</summary>
        [Fact]
        public void Water_IsImpassable()
        {
            var world = Merged((G1, River()));
            Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Water);
            var nav = Nav(world);
            Assert.True(nav.IsWalkable(new Vector3(150, 200, 0)));
            Assert.True(nav.IsWalkable(new Vector3(280, 200, 0)));
            Assert.False(nav.PathExists(new Vector3(150, 200, 0), new Vector3(280, 200, 0), (uint)NavLayerMask.Infantry), "a path across the river");
        }

        // ── a bridge carries a unit, until it collapses ───────────────────────────────────────────────────────────

        private static JsonObject Bridge() => OverlayFixture.Feature(TerrainFeature.Bridge, 198, 200, "", 0, 4f, 0, (0, 0), (34, 0));

        /// <summary>
        /// A BRIDGE (a deck slab + two ramps under one <c>"structure"</c>) over the river carries a unit across — and once its building collapses (the damage table over a derived world, as the live bake lays it) the deck is gone and
        /// nothing carries a unit at deck height. 🔴 Red-proof: expand a bridge to its deck only (no ramps) ⇒ no path crosses; expand it without the <c>structure</c> ⇒ there is no building to collapse.
        /// </summary>
        [Fact]
        public void ABridge_CarriesAUnitAcross_AndStopsCarryingOnceItCollapses()
        {
            var world = Merged((G1, River()), (G2, Bridge()));
            int bridge = world.Buildings.ToList().FindIndex(b => b.Label == G2);
            Assert.True(bridge >= 0, "the bridge is ONE structure building");
            Assert.Empty(world.Panels.Where(p => p.Building == bridge));   // a deck and ramps, no walls
            var from = new Vector3(150, 200, 0); var to = new Vector3(280, 200, 0);
            Assert.True(Nav(world).PathExists(from, to, (uint)NavLayerMask.Infantry), "the unit crosses the bridge");

            using var repo = BreachFixtures.World(world);
            BreachFixtures.Collapsed(repo, world, bridge);
            var derived = TerrainObstacles.With(world, Array.Empty<TerrainPrism>(), DoorStates.Of(repo, world).Damage);
            Assert.Equal(new[] { bridge }, derived.BakedCollapsed.ToArray());
            // the deck and both ramps are gone (the base's own roof slab is the one walkable left) and a unit at deck height has nothing under it (O-2's own criterion)
            Assert.Equal(_f.BaseWorld().Walkables.Count, derived.Walkables.Count);
            Assert.True(derived.SurfaceZ(215f, 200f, 1.5f) < 1.0f, "the collapsed bridge no longer carries a unit at deck height");
            // ⭐ CE-3438 (T-7b) — what a collapse leaves is the rubble mound CLIPPED TO LAND: one mound on each bank, none in the water. 📌 (T-2 measured the unclipped 0.6 m mound over the whole footprint as a walkable causeway:
            //   a path from bank to bank still existed.) 🔴 Red-proof: skip the clip (return the whole prism) ⇒ the path exists again.
            var mounds = derived.Prisms.Where(p => p.Label != null && p.Label.EndsWith("(rubble)", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, mounds.Count);
            Assert.All(mounds, m => Assert.False(m.Min.X < 230f && m.Max.X > 200f, "no mound reaches into the river (x 200..230)"));
            Assert.False(Nav(derived).PathExists(from, to, (uint)NavLayerMask.Infantry), "after the collapse nothing carries a unit across the river");
        }

        /// <summary>
        /// ⭐ <c>CE-6002</c> (showcase world, T-2 measured) — the bridge over the river carries BOTH layers, and the way over it is the only way: a man AND a ground vehicle go bank to bank, their path is
        /// at least the straight distance and keeps to the bridge's strip (y 198..202, never in the water at x 200..230), the river alone has no crossing, and once the bridge collapses neither layer has one.
        /// 🔴 Red-proof: expand a bridge to its deck only (no ramps) ⇒ the vehicle layer has no way on to the deck ⇒ no crossing.
        /// </summary>
        [Fact]
        public void ABridge_IsTheOnlyCrossing_ForMenAndGroundVehicles_AndAFallenBridgeLeavesNone()
        {
            var layers = NavLayerMask.Infantry | NavLayerMask.Vehicle;
            INavmeshProvider Both(TerrainWorld w) => new RecastNavmeshFactory { Layers = layers, TileCache = new NavTileCache() }.Build(w) ?? throw new InvalidOperationException("no navmesh");
            var from = new Vector3(150, 200, 0); var to = new Vector3(280, 200, 0);

            var riverOnly = Both(Merged((G1, River())));
            var world = Merged((G1, River()), (G2, Bridge()));
            var spanned = Both(world);
            Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
            foreach (var layer in new[] { NavLayerMask.Infantry, NavLayerMask.Vehicle })
            {
                Assert.False(riverOnly.PathExists(from, to, (uint)layer), $"{layer}: the river alone has a crossing");
                Assert.True(spanned.PathExists(from, to, (uint)layer), $"{layer}: no way over the bridge");
                int n = spanned.PlanPath(from, to, wps, (uint)layer);
                Assert.True(n >= 2);
                float length = 0f;
                for (int i = 1; i < n; i++) length += Vector3.Distance(wps[i - 1].Position, wps[i].Position);
                Assert.True(length >= Vector3.Distance(from, to) - 0.5f, $"{layer}: path {length:F1} m is shorter than the straight line");
                for (int i = 0; i < n; i++)
                    if (wps[i].Position.X > 200f && wps[i].Position.X < 230f)
                        Assert.InRange(wps[i].Position.Y, 197.5f, 202.5f);   // over the river only inside the bridge's 4 m strip
            }

            int bridge = world.Buildings.ToList().FindIndex(b => b.Label == G2);
            using var repo = BreachFixtures.World(world);
            BreachFixtures.Collapsed(repo, world, bridge);
            var fallen = Both(TerrainObstacles.With(world, Array.Empty<TerrainPrism>(), DoorStates.Of(repo, world).Damage));
            foreach (var layer in new[] { NavLayerMask.Infantry, NavLayerMask.Vehicle })
                Assert.False(fallen.PathExists(from, to, (uint)layer), $"{layer}: a fallen bridge still carries");
        }

        /// <summary>
        /// ⭐ <c>CE-6001</c> (§18k T-7a for a SAVED terrain) — <c>TerrainBake.SaveAs</c> folds the bridge's ROAD into the saved terrain: its segment is appended to the saved road network (the base's indices stay).
        /// 🔴 Red-proof: skip the fold in <c>SaveAs</c> ⇒ the saved roads have the base's two segments only.
        /// </summary>
        [Fact]
        public void SaveAs_FoldsTheBridgesRoad_IntoTheSavedRoadNetwork()
        {
            _f.WithRoads();
            File.WriteAllText(Path.Combine(_f.ScenarioDir, "scenario.json"), _f.Scenario((G1, River()), (G2, Bridge())).ToJsonString());
            string target = Path.Combine(_f.Assets, "terrain", "saved-t");
            TerrainBake.SaveAs(_f.ScenarioDir, target, "saved-t", _f.Resolve, new[] { _f.Recipes }, bakeNavtiles: false);
            var roads = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "roads.json")))!;
            var segs = roads["segments"]!.AsArray();
            Assert.Equal(3, segs.Count);                                 // the base's WEST and EAST roads + the bridge, appended
            Assert.Equal(2, (int)segs[2]!["id"]!);
            Assert.Equal(1, (int)segs[2]!["startNodeId"]!);              // snapped to the west road's end node and the east road's start node
            Assert.Equal(2, (int)segs[2]!["endNodeId"]!);
        }

        // ── a scatter is deterministic ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A SCATTER of 140 items (a recipe's cars, sandbag walls, trees and bushes inside a polygon, one entity) compiles BYTE-IDENTICALLY twice; a different seed is a different scatter; every item is a part labelled
        /// <c>&lt;guid&gt;/&lt;i&gt;</c> inside the polygon. 🔴 Red-proof: seed the scatter's generator from the clock ⇒ the two compiles differ.
        /// </summary>
        [Fact]
        public void AScatterOf140Items_CompilesByteIdenticallyTwice()
        {
            JsonObject Scatter(uint seed) => OverlayFixture.Feature(TerrainFeature.Scatter, 300, 300, "litter", 0, 3.9f, seed, (-30, -30), (30, -30), (30, 30), (-30, 30));
            byte[] CompileBytes(uint seed)
            {
                _f.Compile(_f.Scenario((G1, Scatter(seed))));
                return File.ReadAllBytes(Path.Combine(_f.Overlay, "overlay.world.geojson"));
            }
            var a = CompileBytes(7); var b = CompileBytes(7); var c = CompileBytes(8);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            var features = JsonNode.Parse(a)!["features"]!.AsArray();
            Assert.Equal(140, features.Count);
            Assert.All(features, ft => Assert.StartsWith(G1 + "/", (string)ft!["properties"]!["label"]!));
            foreach (var ft in features.Where(ft => (string)ft!["geometry"]!["type"]! == "Point"))
            {
                var xy = ft!["geometry"]!["coordinates"]!.AsArray();
                Assert.InRange((float)xy[0]!, 270f, 330f); Assert.InRange((float)xy[1]!, 270f, 330f);
            }
            Assert.Contains(features, ft => (string)ft!["properties"]!["kind"]! == "prism");
            Assert.Contains(features, ft => (string)ft!["properties"]!["kind"]! == "tree");
            Assert.Contains(features, ft => (string)ft!["properties"]!["surface"]?.ToString()! == "scrub");
        }

        // ── clutter round-trips through the overlay file ──────────────────────────────────────────────────────────

        /// <summary>
        /// A CLUTTER item (a car of the obstacle table) is written into the overlay as a <c>prism</c>, read back by the ONE world parser as a solid of the car's box and material (<c>car-body</c>, 4.5 × 1.8 × 1.5 m), and merged with the
        /// base: sight is blocked below its roof. 🔴 Red-proof: have the parser ignore the prism's material ⇒ it is not <c>car-body</c>.
        /// </summary>
        [Fact]
        public void AClutterPrism_RoundTripsThroughTheOverlayFile()
        {
            var world = Merged((G1, OverlayFixture.Feature(TerrainFeature.Clutter, 300, 300, "car", 90f)));
            var car = Assert.Single(world.Prisms.Where(p => p.Label == G1 + "/0"));
            Assert.Equal("car-body", car.Material!.Name);
            Assert.Equal(1.5f, car.TopZ - car.BaseZ, 3);
            Assert.Equal(1.8f, car.Max.X - car.Min.X, 2);   // heading 90°: the 4.5 m length runs north
            Assert.Equal(4.5f, car.Max.Y - car.Min.Y, 2);
            Assert.True(world.SegmentBlocked(new Vector3(300, 290, 1.0f), new Vector3(300, 310, 1.0f)), "the car blocks sight below its roof");
            Assert.False(world.SegmentBlocked(new Vector3(300, 290, 1.7f), new Vector3(300, 310, 1.7f)), "…and not above it");
            Assert.Equal(ClutterTypes.Car.Length, 4.5f);   // the table the obstacle TKB type reads too
        }

        // ── the rest: line / area kinds parse, scrub is a surface, the component is small ─────────────────────────

        [Fact]
        public void EveryKind_ExpandsToPartsTheParserReads()
        {
            var world = Merged(
                (G1, OverlayFixture.Feature(TerrainFeature.Wall, 100, 300, "brick", 0, 2.5f, 0, (0, 0), (20, 0))),
                (G2, OverlayFixture.Feature(TerrainFeature.Fence, 100, 320, "", 0, 0, 0, (0, 0), (20, 0))),
                (G3, OverlayFixture.Feature(TerrainFeature.Scrub, 350, 350, "", 0, 0, 0, (0, 0), (20, 0), (20, 20), (0, 20))),
                ("c4000000-0000-4000-8000-000000000004", OverlayFixture.Feature(TerrainFeature.Tree, 360, 100)));
            Assert.Contains(world.Panels, p => p.Material?.Name == "brick" && p.TopZ - p.BaseZ == 2.5f);
            Assert.Contains(world.Panels, p => p.Material?.Name == "fence-wood");
            Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Scrub);
            Assert.Contains(world.Prisms, p => p.Kind == TerrainPrismKind.Tree);
            Assert.True(System.Runtime.CompilerServices.Unsafe.SizeOf<TerrainFeature>() <= 1024);
            Assert.Throws<ArgumentException>(() => ClutterTypes.Get("no-such-thing", "test"));
        }
    }
}
