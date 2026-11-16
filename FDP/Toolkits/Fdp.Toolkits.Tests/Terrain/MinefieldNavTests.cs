using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ CE-4624 MS-T3 (docs/designs/minefields/DESIGN_Minefields.md MR-3, MR-4, MR-20, KR-10, KR-14) — a minefield in the NAVMESH: stamped as composite areas (a field over a wood stays a wood, over a crater a crater), a lane a
    /// passage in the mesh, the polygon → (field, cells, lane) map, and the price a mover's SIDE puts on it by what that side knows — known-mined ×200, suspected ×20, unknown ×1, never closed — with the lane open to the side that
    /// cleared it. Real Recast meshes built from GeoJSON (the <c>TerrainWoodsTests</c> precedent).
    /// </summary>
    public sealed class MinefieldNavTests
    {
        private const int Blue = 1, Red = 2;
        private static readonly Guid FieldGuid = Guid.Parse("a7000000-0000-4000-8000-0000000000f1");
        private const uint Infantry = (uint)NavLayerMask.Infantry, Wheeled = (uint)NavLayerMask.Vehicle;

        /// <summary>Open ground 300 × 600 m: the field's long thin shape leaves a detour round its north end (y &gt; 560).</summary>
        private const string Open = """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,300,600],"groundZ":0},"features":[]}""";

        /// <summary>The woods barrier of <c>TerrainWoodsTests</c>: a wood across the whole 80 m corridor — a wheeled vehicle may not cross it.</summary>
        private const string WoodBarrier = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,300,80],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"surface","surface":"forest"},
              "geometry":{"type":"Polygon","coordinates":[[[100,-10],[200,-10],[200,90],[100,90],[100,-10]]]}}]}
            """;

        private static readonly Vector2[] LongField = { new(130, 0), new(160, 0), new(160, 560), new(130, 560) };

        private sealed class Rig
        {
            public readonly EntityRepository R = new();
            public readonly Entity Field;
            public TerrainWorld Terrain = null!;

            public delegate void Setter(ref Minefield f);

            public Rig(Vector2[] outline, ushort knownAtStart, Setter? lanes = null)
            {
                R.RegisterComponent<SimTransform>(); R.RegisterComponent<EntityGuid>(); R.RegisterComponent<Minefield>(); R.RegisterComponent<MinefieldKnowledge>(); R.RegisterComponent<MinefieldLanes>();
                R.RegisterManagedComponent<TestAreaOutline>();
                Field = R.CreateEntity();
                R.AddComponent(Field, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                var f = new Minefield { Type = MineType.AntiPersonnel, SpacingM = 5f, KnownAtStartSides = knownAtStart };
                lanes?.Invoke(ref f);
                R.AddComponent(Field, f);
                R.AddComponent(Field, new EntityGuid(FieldGuid));
                R.SetManagedComponent(Field, new TestAreaOutline { Points = outline });
            }

            /// <summary>The terrain of <paramref name="json"/> with this repo's minefields laid in (what the static-obstacle bake does).</summary>
            public TerrainWorld Lay(string json)
            {
                R.Tick();
                Terrain = TerrainObstacles.With(TerrainWorldParser.Parse(json, "t"), Array.Empty<TerrainPrism>(), null, null, null, MinefieldSet.Of(R));
                return Terrain;
            }

            public DotRecastNavmeshProvider Mesh() => (DotRecastNavmeshProvider)new RecastNavmeshFactory { TileCache = null }.Build(Terrain)!;
            public DoorStates Doors() { R.Tick(); return DoorStates.Of(R, Terrain); }

            public void Know(int side, MineCellKnowledge level, bool marked = false)
            {
                var o = new List<Vector2>(); MinefieldKnowledgeOps.TryOutline(R, Field, o, out var anchor);
                var g = MinefieldKnowledgeOps.Grid(R.GetComponentRO<Minefield>(Field), anchor, o);
                var k = new MinefieldKnowledge();
                k.Header.SubjectKind = SubjectKind.Entity; k.Header.Subject = FieldGuid; k.Header.ObjectIndex = -1; k.Header.SidePlus1 = (byte)(side + 1); k.Header.Kind = PlaceKnowledgeKind.Minefield;
                k.Header.Marked = (byte)(marked ? 1 : 0);
                for (int r = 0; r < g.Rows; r++) for (int c = 0; c < g.Cols; c++) k.Set(c, r, level);
                var e = R.CreateEntity(); R.AddComponent(e, k);
            }
        }

        private static uint Mask(uint layer, int side) => layer | NavLayerSelection.SideBits(side);

        private static List<(long Ref, int Area)> Polys(DotRecastNavmeshProvider nav, NavLayerMask layer)
        {
            Assert.True(nav.TryGetNavMesh(layer, out var mesh));
            var list = new List<(long, int)>();
            for (int t = 0; t < mesh!.GetMaxTiles(); t++)
            {
                var tile = mesh.GetTile(t);
                if (tile?.data?.header == null) continue;
                long baseRef = mesh.GetPolyRefBase(tile);
                for (int p = 0; p < tile.data.header.polyCount; p++)
                {
                    var poly = tile.data.polys[p];
                    if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;
                    list.Add((baseRef | (long)p, poly.GetArea()));
                }
            }
            return list;
        }

        /// <summary>
        /// ⭐ MR-3 — a field is STAMPED: its spans become minefield-area polygons (5) the filter can recognise, the polygon → (field, cells) map holds them, and each carries the knowledge cells it covers; a LANE
        /// restores plain ground (63) polygons of its own, in the map with their lane index. 🔴 Red-proof: skip the field's own volumes ⇒ no minefield polygon; skip the lane's restoring volume ⇒ the lane polygons keep area 5.
        /// </summary>
        [Fact]
        public void CE4624_AField_IsStampedAsMinefieldPolygons_AndALaneRestoresGroundPolygonsOfItsOwn()
        {
            var rig = new Rig(LongField, knownAtStart: 0, (ref Minefield f) => { f.LaneCount = 1; f.SetLane(0, new MinefieldLane(new Vector2(120, 250), new Vector2(170, 250), 6f, (byte)(Blue + 1), 0)); });
            rig.Lay(Open);
            Assert.Single(rig.Terrain.Minefields);
            var nav = rig.Mesh();
            var polys = Polys(nav, NavLayerMask.Vehicle);
            Assert.Contains(polys, p => p.Area == NavDoorways.MinefieldArea);
            Assert.DoesNotContain(polys, p => p.Area is NavDoorways.MinefieldWoodsArea or NavDoorways.MinefieldCraterArea);
            var map = nav.MinefieldPolys(NavLayerMask.Vehicle)!;
            Assert.True(map.Count > 0 && nav.MinefieldPolyCount(NavLayerMask.Vehicle) == map.Count);
            Assert.True(map.LanePolyCount > 0, "the lane's strip is polygons of its own");
            foreach (var (r, area) in polys)
            {
                if (!map.TryGet(r, out var mp)) { Assert.False(NavDoorways.IsMinefield(area), "a minefield-area polygon is in the map"); continue; }
                Assert.NotEmpty(mp.Cells);
                if (mp.Lane >= 0) Assert.Equal(NavDoorways.GroundArea, area);   // the lane restored what lies underneath
                else Assert.Equal(NavDoorways.MinefieldArea, area);
            }
            Assert.Contains(polys, p => p.Area == NavDoorways.GroundArea && !map.TryGet(p.Ref, out _));   // ground outside the field is unmapped
        }

        /// <summary>
        /// ⭐⭐ MR-3 — a field over a WOOD keeps the wood's rules: stamped as the composite (6), the wood still keeps wheels out (a tracked vehicle and infantry cross it); the same field stamped as plain 5 would let wheels in.
        /// 🔴 Red-proof: skip the composite stamp ⇒ the wheeled vehicle crosses; take <c>== WoodsAreaId</c> in <c>PassFilter</c> back ⇒ the same.
        /// </summary>
        [Fact]
        public void CE4624_AFieldOverAWood_KeepsTheWoodsRules_WheelsStayOut()
        {
            var rig = new Rig(new[] { new Vector2(90, -5), new Vector2(210, -5), new Vector2(210, 85), new Vector2(90, 85) }, knownAtStart: 0);
            rig.Lay(WoodBarrier);
            var nav = rig.Mesh();
            Assert.Contains(Polys(nav, NavLayerMask.Vehicle), p => p.Area == NavDoorways.MinefieldWoodsArea);
            Vector3 west = new(40, 40, 0), east = new(260, 40, 0);
            Assert.False(nav.PathExists(west, east, Wheeled), "a wheeled vehicle may not cross a wood, mined or not");
            Assert.False(nav.PathExists(west, east, Mask(Wheeled, Blue)));
            Assert.True(nav.PathExists(west, east, Wheeled | NavLayerSelection.TrackedModifier), "tracked drives through it");
            Assert.True(nav.PathExists(west, east, Infantry));
        }

        /// <summary>
        /// ⭐ MR-3 — a field over a CRATER or ditch is the crater composite (7), stamped after the field's plain area and winning over a wood (as the plain stamps do); a lane restores the underlying area afterwards. Volume level
        /// (the crater table is the residency's): the order is wood → crater → field (5) → composites (6, 7) → lane restores (63, 3, 4). 🔴 Red-proof: drop the crater composite loop ⇒ no area-7 volume.
        /// </summary>
        [Fact]
        public void CE4624_AFieldOverACrater_IsTheCraterComposite_AndALaneRestoresTheUnderlyingAreaAfterwards()
        {
            var rig = new Rig(new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100) }, 0, (ref Minefield f) => { f.LaneCount = 1; f.SetLane(0, new MinefieldLane(new Vector2(0, 50), new Vector2(100, 50), 10f, 0, 0)); });
            var world = rig.Lay(Open);
            static NavDoorways.Volume Square(float x0, float y0, float x1, float y1, int area)
                => new(-1, new[] { x0, -3f, y0, x1, -3f, y0, x1, -3f, y1, x0, -3f, y1 }, -3f, 3f) { Area = area };
            var woods = new[] { Square(-10, -10, 40, 110, NavDoorways.WoodsArea) };
            var craters = new[] { Square(30, 20, 60, 80, NavDoorways.CraterArea) };
            var v = NavDoorways.Minefields(world, woods, craters);
            var areas = v.Select(x => x.Area).ToList();
            Assert.Contains(NavDoorways.MinefieldArea, areas);
            Assert.Contains(NavDoorways.MinefieldWoodsArea, areas);
            Assert.Contains(NavDoorways.MinefieldCraterArea, areas);
            Assert.Contains(NavDoorways.GroundArea, areas);
            // the order: plain field stamps, then the composites, then the lane's restores
            int lastPlain = areas.FindLastIndex(a => a == NavDoorways.MinefieldArea), firstWoodsC = areas.IndexOf(NavDoorways.MinefieldWoodsArea);
            int lastCraterC = areas.FindLastIndex(a => a == NavDoorways.MinefieldCraterArea), firstRestore = areas.IndexOf(NavDoorways.GroundArea);
            Assert.True(lastPlain < firstWoodsC && areas.IndexOf(NavDoorways.MinefieldCraterArea) > areas.FindLastIndex(a => a == NavDoorways.MinefieldWoodsArea) && lastCraterC < firstRestore, string.Join(",", areas));
            Assert.Contains(NavDoorways.WoodsArea, areas.Skip(firstRestore));    // the wood under the lane comes back
            Assert.Contains(NavDoorways.CraterArea, areas.Skip(firstRestore));   // and the crater
            // the lane's pieces lie inside the lane strip, the composites inside the field
            foreach (var vol in v.Where(x => x.Area is NavDoorways.GroundArea or NavDoorways.WoodsArea or NavDoorways.CraterArea))
                for (int i = 0; i < vol.Verts.Length; i += 3) Assert.InRange(vol.Verts[i + 2], 44.9f, 55.1f);
        }

        /// <summary>
        /// ⭐ MR-4, KR-10 — a path for a side that KNOWS the field goes round it (the field is priced ×200, never closed); one for a side that does not — or for no side at all — walks straight through. A field known at start
        /// (<c>KnownAtStartSides</c>) is known in full. 🔴 Red-proof: drop the side test in <c>GetCost</c> ⇒ the no-side query goes round too; price nothing ⇒ the knowing side walks through.
        /// </summary>
        [Fact]
        public void CE4624_ASideThatKnowsTheField_GoesRound_OneThatDoesNot_WalksThrough()
        {
            var rig = new Rig(LongField, knownAtStart: (ushort)((1 << Blue) | 1));   // Blue — and side 0 (Neutral): a query carrying NO side must still apply nothing
            rig.Lay(Open);
            var nav = rig.Mesh();
            var doors = rig.Doors();
            Vector3 from = new(100, 40, 0), to = new(190, 40, 0);
            float blue = nav.PathCost(from, to, Mask(Infantry, Blue), doors), red = nav.PathCost(from, to, Mask(Infantry, Red), doors), none = nav.PathCost(from, to, Infantry, doors);
            Assert.True(red < 110f, $"a side that knows nothing walks through ({red:F0} m)");
            Assert.True(none < 110f, $"no side: no knowledge applied ({none:F0} m)");
            Assert.True(blue > 900f, $"a side that knows the field goes round its north end ({blue:F0} m)");
            Assert.True(nav.PathExists(from, to, Mask(Infantry, Blue), doors), "never closed: a route exists");
            // and without the view's doors (the shared filter) nothing is priced
            Assert.True(nav.PathCost(from, to, Mask(Infantry, Blue)) < 110f);
        }

        /// <summary>
        /// ⭐ MR-20, KR-10 — KNOWN-MINED is dearer than SUSPECTED: a 30 m crossing costs ×20 (+570 m) when suspected — cheaper than the ~1 100 m detour, so the route crosses — and ×200 (+5 970 m) when known-mined — so it goes round.
        /// 🔴 Red-proof: price suspected like known-mined ⇒ the suspecting side goes round too.
        /// </summary>
        [Fact]
        public void CE4624_KnownMined_IsDearerThanSuspected_AndSuspectedDearerThanUnknown()
        {
            var rig = new Rig(LongField, knownAtStart: 0);
            rig.Lay(Open);
            rig.Know(Blue, MineCellKnowledge.Suspected);
            rig.Know(Red, MineCellKnowledge.KnownMined);
            rig.Know(3, MineCellKnowledge.KnownClear);   // known-clear: priced as plain ground
            var nav = rig.Mesh();
            var doors = rig.Doors();
            Vector3 from = new(100, 40, 0), to = new(190, 40, 0);
            float suspected = nav.PathCost(from, to, Mask(Infantry, Blue), doors), mined = nav.PathCost(from, to, Mask(Infantry, Red), doors), clear = nav.PathCost(from, to, Mask(Infantry, 3), doors);
            Assert.True(suspected < 110f, $"×20 over 30 m is cheaper than the detour: it crosses ({suspected:F0} m)");
            Assert.True(mined > 900f, $"×200 over 30 m is dearer: it goes round ({mined:F0} m)");
            Assert.True(clear < 110f, $"known-clear is plain ground ({clear:F0} m)");
        }

        /// <summary>
        /// ⭐ MR-18, MR-20 — a LANE a side cleared at runtime (a <see cref="MinefieldLanes"/> record) opens the field to THAT side only: both sides know the field, the clearer's route crosses at the lane, the other's goes round.
        /// 🔴 Red-proof: drop the "cleared by" test in the filter ⇒ the clearing side goes round too.
        /// </summary>
        [Fact]
        public void CE4624_ALane_OpensTheFieldToTheSideThatClearedIt_Only()
        {
            var rig = new Rig(LongField, knownAtStart: (ushort)((1 << Blue) | (1 << Red)));
            var lanes = new MinefieldLanes { Field = FieldGuid };
            lanes.TryAdd(new LaneItem(FieldGuid, new Vector2(120, 250), new Vector2(170, 250), 6f, (byte)(Blue + 1)));
            var e = rig.R.CreateEntity(); rig.R.AddComponent(e, lanes);
            rig.Lay(Open);
            Assert.Single(rig.Terrain.Minefields[0].Lanes);
            var nav = rig.Mesh();
            Assert.True(nav.MinefieldPolys(NavLayerMask.Infantry)!.LanePolyCount > 0);
            var doors = rig.Doors();
            Vector3 from = new(100, 250, 0), to = new(190, 250, 0);
            float blue = nav.PathCost(from, to, Mask(Infantry, Blue), doors), red = nav.PathCost(from, to, Mask(Infantry, Red), doors);
            Assert.True(blue < 110f, $"the side that cleared the lane crosses at it ({blue:F0} m)");
            Assert.True(red > 300f, $"the other side goes round ({red:F0} m)");
        }

        /// <summary>
        /// ⭐ KR-8, R-2084 — pricing a query allocates NOTHING once warm: the side's per-cell state is read through array indexes and dictionary lookups on the view's immutable table.
        /// 🔴 Red-proof: allocate in <c>FactorFor</c> ⇒ bytes per query.
        /// </summary>
        [Fact]
        public void CE4624_PricingAQuery_AllocatesNothing()
        {
            var rig = new Rig(LongField, knownAtStart: (ushort)(1 << Blue));
            rig.Lay(Open);
            var nav = rig.Mesh();
            var doors = rig.Doors();
            Assert.True(nav.ProjectToNavmesh(new Vector3(125, 100, 0), out var from, Infantry));
            var to = new Vector3[16];
            for (int i = 0; i < to.Length; i++)
            {
                float a = i * MathF.Tau / to.Length;
                nav.ProjectToNavmesh(new Vector3(from.X + MathF.Cos(a) * 20f, from.Y + MathF.Sin(a) * 20f, 0f), out to[i], Infantry);
            }
            var costs = new float[to.Length];
            uint mask = Mask(Infantry, Blue);
            nav.PathCosts(from, to, costs, 60f, mask, doors); nav.PathCosts(from, to, costs, 60f, mask, doors);   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++) nav.PathCosts(from, to, costs, 60f, mask, doors);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(nav.MinefieldPolyCount(NavLayerMask.Infantry) > 0 && costs.Any(c => c > 0f), "the field's polygons were on the queried routes");
        }

        /// <summary>
        /// ⭐ MR-3, R-2084 — the bake's change detection: <c>MinefieldSet.SignatureOf</c> is 0 with no field, steady and ALLOCATION-FREE with one, and moves with everything the bake reads — an outline point, the position, a lane
        /// record added (and back when it goes: a seek). 🔴 Red-proof: leave the lane records out of the hash ⇒ the lane step reads equal.
        /// </summary>
        [Fact]
        public void CE4624_TheBakeSignature_IsStable_AllocatesNothing_AndMovesWithTheFieldsContent()
        {
            using var empty = new EntityRepository();
            empty.RegisterComponent<Minefield>(); empty.RegisterComponent<SimTransform>();
            Assert.Equal(0UL, MinefieldSet.SignatureOf(empty));

            var rig = new Rig(LongField, knownAtStart: 0);
            rig.R.Tick();
            ulong a = MinefieldSet.SignatureOf(rig.R);
            Assert.NotEqual(0UL, a);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool stable = true;
            for (int i = 0; i < 100; i++) stable &= MinefieldSet.SignatureOf(rig.R) == a;
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(stable);

            rig.R.SetManagedComponent(rig.Field, new TestAreaOutline { Points = new[] { new Vector2(130, 0), new Vector2(160, 0), new Vector2(160, 560), new Vector2(131, 560) } });
            ulong edited = MinefieldSet.SignatureOf(rig.R);
            Assert.NotEqual(a, edited);
            rig.R.SetManagedComponent(rig.Field, new TestAreaOutline { Points = LongField });
            Assert.Equal(a, MinefieldSet.SignatureOf(rig.R));
            var moved = rig.R.GetComponent<SimTransform>(rig.Field); moved.Position = new Vector3(1, 0, 0); rig.R.SetComponent(rig.Field, moved);
            Assert.NotEqual(a, MinefieldSet.SignatureOf(rig.R));
            moved.Position = Vector3.Zero; rig.R.SetComponent(rig.Field, moved);

            var lanes = new MinefieldLanes { Field = FieldGuid };
            lanes.TryAdd(new LaneItem(FieldGuid, new Vector2(120, 250), new Vector2(170, 250), 6f, (byte)(Blue + 1)));
            var le = rig.R.CreateEntity(); rig.R.AddComponent(le, lanes);
            Assert.NotEqual(a, MinefieldSet.SignatureOf(rig.R));
            rig.R.DestroyEntity(le);
            Assert.Equal(a, MinefieldSet.SignatureOf(rig.R));   // a seek back: the lane record is gone, the hash is the field's again
        }

        /// <summary>
        /// ⭐ MR-20, KR-14 — the filter's arithmetic on a hand-made polygon map: the side's WORST cell decides (mined ×200 over suspected ×20 over unknown / known-clear ×1); side 0 applies no knowledge; a lane polygon is open (×1)
        /// to the side that cleared it and to the sides its known-to mask names, priced as the field to the rest; a minefield COMPOSITE over a wood / a crater obeys the wood / crater rules (wheels out of both, infantry in
        /// both, the wood priced). 🔴 Red-proof: price the first cell, not the worst ⇒ the mixed polygon reads ×1.
        /// </summary>
        [Fact]
        public void CE4624_TheFilter_PricesTheWorstCell_TheLaneByWhoClearedIt_AndACompositeKeepsItsRules()
        {
            var rig = new Rig(LongField, knownAtStart: 0);
            rig.Lay(Open);
            // side Blue: cell (0,0) suspected, (1,0) mined; Red: only (2,0) known-clear
            var o = new List<Vector2>(); MinefieldKnowledgeOps.TryOutline(rig.R, rig.Field, o, out var anchor);
            var kb = new MinefieldKnowledge(); kb.Header.Subject = FieldGuid; kb.Header.SidePlus1 = Blue + 1; kb.Header.Kind = PlaceKnowledgeKind.Minefield;
            kb.Set(0, 0, MineCellKnowledge.Suspected); kb.Set(1, 0, MineCellKnowledge.KnownMined);
            var kr = new MinefieldKnowledge(); kr.Header.Subject = FieldGuid; kr.Header.SidePlus1 = Red + 1; kr.Header.Kind = PlaceKnowledgeKind.Minefield;
            kr.Set(2, 0, MineCellKnowledge.KnownClear);
            var kd = new MinefieldKnowledge(); kd.Header.Subject = FieldGuid; kd.Header.SidePlus1 = 4 + 1; kd.Header.Kind = PlaceKnowledgeKind.Minefield;
            kd.Set(1, 0, MineCellKnowledge.KnownMined);
            rig.R.AddComponent(rig.R.CreateEntity(), kb); rig.R.AddComponent(rig.R.CreateEntity(), kr); rig.R.AddComponent(rig.R.CreateEntity(), kd);
            var doors = rig.Doors();
            int C(int col, int row) => row * MinefieldKnowledge.MaxCols + col;
            var field = rig.Terrain.Minefields[0];
            var polys = new Dictionary<long, MinePoly>
            {
                [1] = new() { Field = 0, Cells = new[] { C(5, 5), C(0, 0) } },               // unknown + suspected (Blue)
                [2] = new() { Field = 0, Cells = new[] { C(5, 5), C(0, 0), C(1, 0) } },      // … + known-mined (Blue)
                [3] = new() { Field = 0, Cells = new[] { C(5, 5), C(2, 0) } },               // Red: known-clear + unknown
            };
            var lane = new TerrainMinefield { Field = FieldGuid, Params = field.Params, Anchor = field.Anchor, Outline = field.Outline, Min = field.Min, Max = field.Max,
                Lanes = new[] { new MinefieldLane(new Vector2(0, 0), new Vector2(1, 0), 4f, (byte)(Red + 1), (ushort)(1 << 4)) } };   // cleared by Red; side 4 is told
            polys[4] = new MinePoly { Field = 1, Lane = 0, Cells = new[] { C(1, 0) } };
            var map = new MinefieldPolyMap(new[] { field, lane }, polys);
            byte b = Blue + 1, r = Red + 1;
            Assert.Equal(MinefieldPolyMap.SuspectedCost, map.FactorFor(polys[1], doors.Knowledge, b));
            Assert.Equal(MinefieldPolyMap.KnownMinedCost, map.FactorFor(polys[2], doors.Knowledge, b));
            Assert.Equal(1f, map.FactorFor(polys[1], doors.Knowledge, r));            // Red knows nothing of those cells
            Assert.Equal(1f, map.FactorFor(polys[3], doors.Knowledge, r));            // known-clear: plain ground
            Assert.Equal(1f, map.FactorFor(polys[2], doors.Knowledge, 0));            // no side: no knowledge applied
            Assert.True(MinefieldPolyMap.KnownMinedCost > MinefieldPolyMap.SuspectedCost && MinefieldPolyMap.SuspectedCost > 1f);
            // the lane polygon covers cell (1, 0): Blue and side 4 know it mined
            Assert.Equal(1f, map.FactorFor(polys[4], doors.Knowledge, r));                                         // the clearing side
            Assert.Equal(1f, map.FactorFor(polys[4], doors.Knowledge, 4 + 1));                                     // a side its known-to mask names
            Assert.Equal(MinefieldPolyMap.KnownMinedCost, map.FactorFor(polys[4], doors.Knowledge, b));            // any other: priced as the field
            Assert.Equal(MinefieldPolyMap.KnownMinedCost, map.FactorFor(polys[2], doors.Knowledge, 4 + 1));        // …and the same side on the field proper
        }

        /// <summary>
        /// ⭐ MR-3 — every area test treats a composite like its plain area: a minefield-over-wood polygon (6) is refused to a wheeled mover (woods cost 0) and priced for the others; a minefield-over-crater polygon (7) is
        /// refused to every vehicle and open to infantry; the plain minefield polygon (5) is refused to nobody. 🔴 Red-proof: take <c>IsCrater</c> back to <c>== CraterArea</c> ⇒ a vehicle drives through a mined crater.
        /// </summary>
        [Fact]
        public void CE4624_EveryAreaTest_TreatsACompositeLikeItsPlainArea()
        {
            var plain = new Dictionary<long, int>();
            var infantry = new DoorAwareQueryFilter(plain, null, canOpenDoors: true);
            var wheeled = new DoorAwareQueryFilter(plain, null, canOpenDoors: false);
            var tracked = wheeled.WithWoodsCost(DoorAwareQueryFilter.TrackedWoodsCost);
            DtPoly Poly(int area) { var p = new DtPoly(0, 6) { flags = 1 }; p.SetArea(area); return p; }
            var a = new DotRecast.Core.Numerics.RcVec3f(0, 0, 0); var b2 = new DotRecast.Core.Numerics.RcVec3f(1, 0, 0);
            float Cost(DoorAwareQueryFilter f, int area) { var p = Poly(area); return f.GetCost(a, b2, 1, null!, p, 2, null!, p, 0, null!, p); }

            foreach (int area in new[] { NavDoorways.WoodsArea, NavDoorways.MinefieldWoodsArea })
            {
                Assert.False(wheeled.PassFilter(2, null!, Poly(area)), $"area {area}: wheels never enter a wood");
                Assert.True(tracked.PassFilter(2, null!, Poly(area)));
                Assert.True(infantry.PassFilter(2, null!, Poly(area)));
                Assert.Equal(DoorAwareQueryFilter.TrackedWoodsCost, Cost(tracked, area), 3);
                Assert.Equal(DoorAwareQueryFilter.InfantryWoodsCost, Cost(infantry, area), 3);
            }
            foreach (int area in new[] { NavDoorways.CraterArea, NavDoorways.MinefieldCraterArea })
            {
                Assert.False(wheeled.PassFilter(2, null!, Poly(area)), $"area {area}: no vehicle drives through a crater");
                Assert.False(tracked.PassFilter(2, null!, Poly(area)));
                Assert.True(infantry.PassFilter(2, null!, Poly(area)));
            }
            Assert.True(wheeled.PassFilter(2, null!, Poly(NavDoorways.MinefieldArea)), "plain minefield ground is priced, never closed");
            Assert.True(NavDoorways.IsMinefield(NavDoorways.MinefieldWoodsArea) && !NavDoorways.IsMinefield(NavDoorways.WoodsArea));
        }
    }
}
