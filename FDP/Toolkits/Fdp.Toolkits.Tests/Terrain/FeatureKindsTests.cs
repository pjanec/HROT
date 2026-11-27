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

        // ── a wall's thickness is its material's (CE-11069) ─────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-11069</c> (DESIGN_Breachable_Obstacle_Walls.md §8 V-4) — a SANDBAG wall feature is built 0.6 m thick, a CONCRETE one 0.4 m, a brick one at the parser's 0.3 m as before; and the 1 m
        /// sandbag wall's contact strength (resistance × thickness × height, <c>ContactBreach.Identify</c>) is above a ramming car's limit (a car is held) and within an IFV's (it breaks it), from
        /// <c>contact-breach.json</c>. 🔴 Red-proof: <c>WallThicknessOf</c> returns 0 ⇒ the sandbag panel is 0.3 m and its strength 30 — below the car's 40.
        /// </summary>
        [Fact]
        public void CE11069_AWallFeature_IsItsMaterialsThickness_AndASandbagWallHoldsARammingCar()
        {
            const string S = "c4000000-0000-4000-8000-000000000004", C = "c5000000-0000-4000-8000-000000000005", B = "c6000000-0000-4000-8000-000000000006";
            var world = Merged((S, OverlayFixture.Feature(TerrainFeature.Wall, 300, 250, "sandbags", 0, 1.0f, 0, (0, -6), (0, 6))),
                               (C, OverlayFixture.Feature(TerrainFeature.Wall, 320, 250, "concrete", 0, 0.8f, 0, (0, -6), (0, 6))),
                               (B, OverlayFixture.Feature(TerrainFeature.Wall, 340, 250, "brick", 0, 2.0f, 0, (0, -6), (0, 6))));
            TerrainWallPanel Of(string guid) => world.Panels.First(p => p.Label == guid + "/0");
            var sand = Of(S);
            Assert.Equal(0.6f, sand.Thickness, 3);
            Assert.Equal(0.4f, Of(C).Thickness, 3);
            Assert.Equal(0.3f, Of(B).Thickness, 3);

            float strength = sand.Material.ResistanceMmRhaPerMetre * sand.Thickness * (sand.TopZ - sand.BaseZ);
            var table = ContactBreachTable.Shared;
            var car = table.Rows.First(r => r.Name == "ramming-car");
            var ifv = table.Rows.First(r => r.Name == "ifv");
            Assert.True(strength > car.SmallMaxStrength, $"a 1 m sandbag wall's strength {strength} must hold a ramming car (limit {car.SmallMaxStrength})");
            Assert.True(strength <= ifv.SmallMaxStrength, $"a 1 m sandbag wall's strength {strength} must break under an IFV (limit {ifv.SmallMaxStrength})");
        }

        // ── a feature wall that stands in some variants only (CE-11071) ──────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-11071</c> (DESIGN_Breachable_Obstacle_Walls.md §9c) — a wall feature with <c>Presence 0.6</c> and <c>PresenceWith</c> a roadblock macro instance's guid: the compiled overlay stamps its
        /// part, and reading the overlay for a run drops the wall EXACTLY in the variants where the scenario load drops the roadblock (<c>ScenarioVariants.Apply</c> on the same DOM — the one
        /// decision); variant 0 keeps it; a wall without Presence is never dropped. 🔴 Red-proof: <c>OverlayPresence.Keep</c> returns true ⇒ the wall stands in a variant whose roadblock is absent.
        /// 🔴 Red-proof: <c>Apply</c> rolls under the entity's own key (ignores PresenceWith) ⇒ the wall and the roadblock disagree in some variant.
        /// </summary>
        [Fact]
        public void CE11071_AWallWithPresenceWith_StandsExactlyWhenItsRoadblockDoes()
        {
            const string Wall = "c7000000-0000-4000-8000-000000000007", Plain = "c8000000-0000-4000-8000-000000000008", Roadblock = "cb000000-0000-4000-8000-00000000000b";
            var wall = OverlayFixture.Feature(TerrainFeature.Wall, 300, 250, "concrete", 0, 1.0f, 0, (0, -3), (0, 3));
            wall["Presence"] = 0.6; wall["PresenceWith"] = Roadblock;
            _f.Compile(_f.Scenario((Wall, wall), (Plain, OverlayFixture.Feature(TerrainFeature.Wall, 320, 250, "brick", 0, 2.0f, 0, (0, -3), (0, 3)))));
            bool Stands(int variant, string guid)
            {
                var o = TerrainFolderWorld.ReadOverlay(_f.Overlay, "base-t", TerrainAssets.RecipesSibling(_f.TerrainFolder), null, variant);
                return o.World.Panels.Any(p => p.Label == guid + "/0");
            }
            bool RoadblockStands(int variant)
            {
                var dom = new JsonObject { ["entities"] = new JsonObject
                {
                    [Roadblock] = new JsonObject { ["Presence"] = 0.6, ["EntityInfo"] = new JsonObject { ["Name"] = "Roadblock N" } },
                    [Wall] = new JsonObject { ["Presence"] = 0.6, ["PresenceWith"] = Roadblock, ["EntityInfo"] = new JsonObject { ["Name"] = "wall" } },
                } };
                Fdp.Toolkit.Scenario.ScenarioVariants.Apply(dom, variant, Guid.Empty, "t");
                var ents = (JsonObject)dom["entities"]!;
                Assert.Equal(ents.ContainsKey(Roadblock), ents.ContainsKey(Wall));   // the load drops them together
                return ents.ContainsKey(Roadblock);
            }
            Assert.True(Stands(0, Wall));
            int absent = 0, present = 0;
            for (int v = 1; v <= 40; v++)
            {
                bool rb = RoadblockStands(v);
                Assert.Equal(rb, Stands(v, Wall));
                Assert.True(Stands(v, Plain), $"a wall without Presence stands in variant {v}");
                if (rb) present++; else absent++;
            }
            Assert.True(absent > 0 && present > 0, $"40 variants should drop the roadblock in some and keep it in others (absent {absent}, present {present})");
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
        /// ⭐⭐ <c>CE-4692</c> BL-4 — ONE deck geometry: the authored bridge's compiled parts are, corner for corner, the pre-lift quads (deck, ramp at A, ramp at B — the formula as it stood in
        /// <c>FeatureKinds.Bridge</c> before the lift, restated here as the reference), and a laid bridge of the same span, width and height (a WalkableDeck obstacle, <c>TerrainObstacles.DeckOf</c>)
        /// is the SAME three walkables as the parsed authored ones. 🔴 Red-proof: flip the side normal in <c>DeckGeometry.Build</c> (or reorder its corners) ⇒ the compiled corners differ.
        /// </summary>
        [Fact]
        public void CE4692_TheAuthoredBridge_AndALaidDeck_AreTheOneDeckGeometry()
        {
            var world = Merged((G1, River()), (G2, Bridge()));
            var features = JsonNode.Parse(File.ReadAllText(Path.Combine(_f.Overlay, "overlay.world.geojson")))!["features"]!.AsArray()
                .Where(ft => ((string?)ft!["properties"]!["label"])?.StartsWith(G2 + "/", StringComparison.Ordinal) == true).ToList();
            Assert.Equal(3, features.Count);
            // the pre-lift formula (FeatureKinds.Bridge before CE-4692), as the reference
            var a = new Vector2(198, 200); var b = new Vector2(232, 200);
            var u = Vector2.Normalize(b - a); var n = new Vector2(-u.Y, u.X); float hw = 2f;
            (Vector2, float)[] Quad(Vector2 p0, float z0, Vector2 p1, float z1) => new[] { (p0 - n * hw, z0), (p1 - n * hw, z1), (p1 + n * hw, z1), (p0 + n * hw, z0) };
            var expected = new[] { Quad(a, 1.5f, b, 1.5f), Quad(a - u * 6f, 0f, a, 1.5f), Quad(b, 1.5f, b + u * 6f, 0f) };
            var kinds = new[] { "slab", "ramp", "ramp" };
            for (int f = 0; f < 3; f++)
            {
                var ft = features.Single(x => (string)x!["properties"]!["label"]! == $"{G2}/{f}")!;
                Assert.Equal(kinds[f], (string)ft["properties"]!["kind"]!);
                var ring = ft["geometry"]!["coordinates"]![0]!.AsArray();
                Assert.Equal(5, ring.Count);
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(expected[f][i].Item1.X, (float)ring[i]![0]!, 3);
                    Assert.Equal(expected[f][i].Item1.Y, (float)ring[i]![1]!, 3);
                    Assert.Equal(expected[f][i].Item2, (float)ring[i]![2]!, 3);
                }
            }
            // the laid deck: the same three walkables
            var authored = world.Walkables.Where(w => w.Building >= 0 && world.Buildings[w.Building].Label == G2).ToList();
            Assert.Equal(3, authored.Count);
            var laid = new System.Collections.Generic.List<TerrainWalkable>();
            TerrainObstacles.DeckOf(new Vector3(215, 200, 0), 0f, new ObstacleShape { Length = 34f, Width = 4f, Height = 1.5f }, (x, y) => world.GroundHeightAt(x, y), 7, false, laid);
            Assert.Equal(3, laid.Count);
            Assert.All(laid, w => Assert.Equal(7, w.Obstacle));
            foreach (var w in laid)
            {
                var twin = authored.SingleOrDefault(o => o.Kind == w.Kind && Vector2.Distance(o.Min, w.Min) < 0.02f && Vector2.Distance(o.Max, w.Max) < 0.02f);
                Assert.True(twin != null, $"the laid {w.Kind} {w.Min}..{w.Max} has no authored twin");
                Assert.Equal(twin!.Vertices.Length, w.Vertices.Length);
                for (int i = 0; i < w.Vertices.Length; i++)
                    Assert.True(w.Vertices.Any(v => Vector3.Distance(v, twin.Vertices[i]) < 0.02f), $"vertex {twin.Vertices[i]} of the authored {w.Kind} is not on the laid one");
            }
        }

        /// <summary>
        /// ⭐ DD-035 D-2 (gateway) — <c>TerrainObstacles.DeckParts</c> IS what <c>DeckOf</c> feeds the walkables, for an assault span (8825-like) and a floating span (8826-like, low deck, rotated and off-origin,
        /// and a degenerate shape the clamps lift): the same three parts in the same order, each part's footprint the walkable's own, a slab's corners at the deck height. 🔴 Red-proof: give <c>DeckOf</c> its own clamp again (e.g. <c>Max(shape.Length, 2f)</c> there) ⇒ the degenerate case's footprints differ.
        /// </summary>
        [Fact]
        public void DeckParts_IsWhatDeckOfFeedsTheWalkables_ForAnAssaultAndAFloatingSpan()
        {
            var cases = new (Vector3 At, float Yaw, ObstacleShape Shape)[]
            {
                (new Vector3(215, 200, 0), 0f, new ObstacleShape { Length = 34f, Width = 4f, Height = 1.5f }),
                (new Vector3(-40, 12, 0), 0.7f, new ObstacleShape { Length = 22f, Width = 3.2f, Height = 0.6f }),
                (new Vector3(5, 5, 0), 2.1f, new ObstacleShape { Length = 0f, Width = 0f, Height = 0f }),
            };
            foreach (var (at, yaw, shape) in cases)
            {
                var parts = TerrainObstacles.DeckParts(at, yaw, shape);
                Assert.Equal(3, parts.Length);
                Assert.Equal(new[] { TerrainWalkableKind.Slab, TerrainWalkableKind.Ramp, TerrainWalkableKind.Ramp }, parts.Select(p => p.Kind));
                var laid = new System.Collections.Generic.List<TerrainWalkable>();
                TerrainObstacles.DeckOf(at, yaw, shape, (_, _) => 0f, 3, false, laid);
                Assert.Equal(parts.Length, laid.Count);
                for (int k = 0; k < parts.Length; k++)
                {
                    Assert.Equal(parts[k].Kind, laid[k].Kind);
                    var (lo, hi) = DeckGeometry.Bounds(new[] { parts[k] });
                    Assert.True(Vector2.Distance(lo, laid[k].Min) < 0.02f && Vector2.Distance(hi, laid[k].Max) < 0.02f, $"part {k} footprint");
                    for (int i = 0; i < 4; i++)
                        Assert.True(laid[k].Vertices.Any(v => Vector2.Distance(new Vector2(v.X, v.Y), parts[k][i].At) < 0.02f), $"part {k} corner {i}");
                    if (parts[k].Kind == TerrainWalkableKind.Slab)
                        for (int i = 0; i < 4; i++) Assert.Equal(MathF.Max(shape.Height, 0.05f), parts[k][i].Above, 3);
                }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-4694</c> ② — a vehicle climbs an AUTHORED bridge's ramp: a hull whose nose is 4 m up the west ramp (0.9 m high — above the hull's step reach, 0.6 m, from the
        /// ground; the foot is sunk 0.3 m) is NOT held, and its corner there rests ON the ramp. 📐 Found live (stechovice-battle): a tank was held at the authored bridge's ramp foot (FailedBlocked) —
        /// CE-4692's hull-point rule held for LAID decks only. 🔴 Red-proof: make <c>TerrainWorld.IsDeck</c> test <c>Obstacle</c> only ⇒ the step is blocked.
        /// </summary>
        [Fact]
        public void CE4694_AHullClimbsAnAuthoredBridgesRamp()
        {
            var world = Merged((G1, River()), (G2, Bridge()));
            var q = new TerrainWorldQuery(world);
            // the bridge runs x 198 → 232 at y 200, its west ramp x 192 → 198; a 7.9 m hull heading east, nose at x 196.0 (4 m up)
            Assert.True(q.HullPointZ(196.0f, 200f, 0f) > 0.8f, "the hull's nose on the ramp rests on it");
            Assert.False(global::CarKinem.Controllers.HullClearance.StepBlocked(q, new Vector2(192.05f, 200f), Vector2.UnitX, 0f, new Vector2(192.15f, 200f), Vector2.UnitX, 0f, 7.9f, 3.66f),
                "a hull climbing the authored bridge's ramp is held");
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

        // ── ⭐ CE-5007 (S-2) — row / group kinds on the ONE shared placement ─────────────────────────────────────────

        private static string Sha(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));

        /// <summary>
        /// ⭐ The refactor guard — every shipped block recipe (three seeds, two headings) and a fixed-seed scatter expand to the SAME bytes as before the shared placement was extracted (hash captured from the code BEFORE the refactor).
        /// 🔴 Red-proof: nudge the group element's line placement (<c>+0.5</c> → <c>+0.4</c>) or the scatter's draw order ⇒ red.
        /// </summary>
        [Fact]
        public void TheSharedPlacement_LeavesEveryShippedRecipeAndScatterByteIdentical()
        {
            string root = Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!;
            string folder = Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(root), "test-town");
            string def = Path.Combine(folder, "terrain.json");
            var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "test-town")!;
            var assets = TerrainAssets.ForFolder(folder);
            var all = new System.Text.StringBuilder();
            foreach (var id in new[] { "farmstead", "hamlet", "village-street", "town-block", "checkpoint", "roadblock", "defensive-position", "copse", "orchard", "hedgerow-fields" })
            {
                var recipe = TerrainRecipe.Find(id, new[] { Fdp.Toolkit.Orchestration.ContentLayout.TerrainRecipeAssets(root) });
                foreach (var (seed, heading) in new[] { (11u, 0f), (12u, 0f), (13u, 37f) })
                {
                    var x = FeatureExpander.Expand(recipe, seed, new Vector2(345f, 180f), heading, TerrainBlock.FitAll, world, "g1", assets);
                    all.Append(id).Append(seed).Append(new JsonArray(x.Features.Select(f => (JsonNode)f.DeepClone()).ToArray()).ToJsonString());
                }
            }
            var poly = new[] { new Vector2(270, 270), new Vector2(330, 270), new Vector2(330, 330), new Vector2(270, 330) };
            foreach (uint seed in new[] { 3u, 7u })
            {
                var sc = FeatureExpander.OfKind(TerrainFeature.Scatter, "k", "litter", 0, 3.9f, seed, default, poly, _f.BaseWorld(), TerrainAssets.ForFolder(_f.TerrainFolder), new[] { _f.Recipes });
                all.Append(new JsonArray(sc.Select(f => (JsonNode)f.DeepClone()).ToArray()).ToJsonString());
            }
            // ⭐ re-pinned `2026-11-19`: CE-3473 made the generated barrel an EMPTY drum (material "steel-plate" → "drum-empty") — proven the only change (the old material restores the old hash)
            string sha = Sha(all.ToString());
            Assert.True(sha == "F175C660B36357C188200EA2F0D074BCC0B1DB3BD7614E248CC7DB94C0EE70C1", $"the expansions changed: {sha}");
        }

        private System.Collections.Generic.IReadOnlyList<JsonObject> Of(byte kind, float pad, uint seed, params Vector2[] pts)
            => FeatureExpander.OfKind(kind, "k", "", 0f, pad, seed, default, pts, _f.BaseWorld(), TerrainAssets.ForFolder(_f.TerrainFolder), new[] { _f.Recipes });

        private static string Json(System.Collections.Generic.IEnumerable<JsonObject> parts) => new JsonArray(parts.Select(f => (JsonNode)f.DeepClone()).ToArray()).ToJsonString();

        /// <summary>
        /// ⭐ A ROW over a 3-point line of 124.5 m at 8 m spacing is 16 lone trees, the first 4 m in, then every 8 m round the corner. 🔴 Red-proof: spacing <c>* (i + 0.5f)</c> → <c>* (i + 1f)</c> in <c>LinePlacesSpaced</c> ⇒ the first tree is at 8 m ⇒ red.
        /// </summary>
        [Fact]
        public void ARow_PlacesATreeEvery8m_AlongTheLine_FirstHalfASpacingIn()
        {
            var parts = Of(TerrainFeature.Row, 8f, 0, new Vector2(100, 250), new Vector2(160, 250), new Vector2(160, 314.5f));
            Assert.Equal(16, parts.Count);
            Assert.All(parts, p => Assert.Equal("tree", (string)((JsonObject)p["properties"]!)["kind"]!));
            Assert.Equal(new[] { "k/0", "k/15" }, new[] { (string)((JsonObject)parts[0]["properties"]!)["label"]!, (string)((JsonObject)parts[15]["properties"]!)["label"]! });
            var (x0, y0) = Centre(parts[0]); var (x7, y7) = Centre(parts[7]); var (x8, y8) = Centre(parts[8]); var (x15, y15) = Centre(parts[15]);
            Assert.Equal((104f, 250f), (x0, y0)); Assert.Equal((160f, 250f), (x7, y7)); Assert.Equal((160f, 258f), (x8, y8)); Assert.Equal((160f, 314f), (x15, y15));
            Assert.Equal(Json(parts), Json(Of(TerrainFeature.Row, 8f, 0, new Vector2(100, 250), new Vector2(160, 250), new Vector2(160, 314.5f))));
            Assert.Equal(Of(TerrainFeature.Row, 0f, 0, new Vector2(100, 250), new Vector2(260, 250)).Count, 20);   // spacing 0 = 8 m: 160 m ⇒ 20 trees
        }

        private static (float X, float Y) Centre(JsonObject feature)
        {
            var c = (JsonArray)((JsonObject)feature["geometry"]!)["coordinates"]!;
            return (c[0]!.GetValue<float>(), c[1]!.GetValue<float>());
        }

        /// <summary>
        /// ⭐ A GROUP of count 11 is 11 lone trees inside the polygon — the same bytes for the same seed, other bytes for another. 🔴 Red-proof: ignore the seed (<c>new Rng(0)</c>) in <c>Group</c> ⇒ the seeds agree ⇒ red; drop the polygon test in <c>AreaSampler.Next</c> ⇒ a tree outside ⇒ red.
        /// </summary>
        [Fact]
        public void AGroup_IsCountTreesInsideThePolygon_ByteIdenticalPerSeed()
        {
            var square = new[] { new Vector2(250, 250), new Vector2(290, 250), new Vector2(290, 290), new Vector2(250, 290) };
            var a = Of(TerrainFeature.Group, 11f, 5, square);
            Assert.Equal(11, a.Count);
            foreach (var t in a) { var (x, y) = Centre(t); Assert.InRange(x, 249f, 291f); Assert.InRange(y, 249f, 291f); }
            Assert.Equal(Json(a), Json(Of(TerrainFeature.Group, 11f, 5, square)));
            Assert.NotEqual(Json(a), Json(Of(TerrainFeature.Group, 11f, 6, square)));
            Assert.Equal(10, Of(TerrainFeature.Group, 0f, 5, square).Count);   // count 0 = 10
        }

        /// <summary>⭐ A row needs two points and a group three; the new kinds are KNOWN (14, 15) and 16 is not — an older build ignores them, a newer one throws nothing for 16. 🔴 Red-proof: <c>IsKnown</c> back to <c>&lt;= Ground</c> ⇒ red.</summary>
        [Fact]
        public void RowAndGroup_NeedTheirShape_AndAreKnownKinds()
        {
            Assert.Throws<ArgumentException>(() => Of(TerrainFeature.Row, 8f, 0, new Vector2(1, 1)));
            Assert.Throws<ArgumentException>(() => Of(TerrainFeature.Group, 5f, 0, new Vector2(1, 1), new Vector2(5, 5)));
            Assert.True(FeatureExpander.IsKnown(14) && FeatureExpander.IsKnown(15));
            Assert.True(FeatureExpander.IsKnown(TerrainFeature.Edit));   // ⭐ CE-5025 BE-1 — 16, the base edit
            Assert.False(FeatureExpander.IsKnown(17));
            Assert.True(FeatureExpander.NeedsShape(TerrainFeature.Row) && FeatureExpander.NeedsShape(TerrainFeature.Group));
        }

        /// <summary>⭐ A ROW drawn across the base's pond (x 125..175, y 70..130) plants no tree in the water and still plants those outside (6 of 12). 🔴 Red-proof: remove the <c>InWater</c> skip in <c>Row</c> ⇒ 12 trees ⇒ red.</summary>
        [Fact]
        public void ARow_SkipsTheBasesWater_AndPlantsTheRest()
        {
            var parts = Of(TerrainFeature.Row, 8f, 0, new Vector2(100, 100), new Vector2(200, 100));
            Assert.Equal(6, parts.Count);
            foreach (var t in parts) { var (x, _) = Centre(t); Assert.False(x >= 125f && x <= 175f, $"a tree at x {x} stands in the water"); }
            Assert.Equal("k/5", (string)((JsonObject)parts[5]["properties"]!)["label"]!);
        }

        /// <summary>⭐ CE-5008 — the draw panel's tree count (<c>CountAlong</c>) is the expander's own: the Row of the 124.5 m line makes exactly that many trees, and an obstacle row's spacing places the same points. 🔴 Red-proof: <c>CountAlong</c> with <c>spacing + 1</c> ⇒ red.</summary>
        [Fact]
        public void TheDrawPanelsCount_IsTheExpandersOwn()
        {
            var line = new[] { new Vector2(100, 250), new Vector2(160, 250), new Vector2(160, 314.5f) };
            Assert.Equal(16, FeatureExpander.CountAlong(line, 8f));
            Assert.Equal(Of(TerrainFeature.Row, 8f, 0, line).Count, FeatureExpander.CountAlong(line, 8f));
            Assert.Equal(FeatureExpander.CountAlong(line, 5f), Of(TerrainFeature.Row, 5f, 0, line).Count);
            Assert.Equal(0, FeatureExpander.CountAlong(new[] { new Vector2(1, 1) }, 8f));
        }

        /// <summary>⭐ CE-5008 (R-5041) — a placement group id is non-zero and never repeats; the component is the small unmanaged saved one.</summary>
        [Fact]
        public void APlacementGroupId_IsNonZero_AndFreshEachTime()
        {
            uint a = PlacementGroup.NewId(), b = PlacementGroup.NewId(), c = PlacementGroup.NewId();
            Assert.True(a != 0 && b > a && c > b);
            Assert.True(System.Runtime.CompilerServices.Unsafe.SizeOf<PlacementGroup>() == 4);
        }
    }
}
