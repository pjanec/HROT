using System.Numerics;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4369</c> (L-7, R-4190 T-2 / T-5, docs/DESIGN_Terrain_World.md §11) — the woods hide what is IN them by distance and from above,
    /// not what is beside them: on flat ground a 500 m square wood of the default 115 trees / ha with a 15 m canopy.
    /// </summary>
    public class TerrainWoodsTests
    {
        private static TerrainWorld Wood(string extra = "")
            => TerrainWorldParser.Parse("""
                {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-1500,-1500,1500,1500],"groundZ":0},"features":[
                 {"type":"Feature","properties":{"kind":"surface","surface":"forest" EXTRA},
                  "geometry":{"type":"Polygon","coordinates":[[[0,0],[500,0],[500,500],[0,500],[0,0]]]}}]}
                """.Replace("EXTRA", extra), "wood");

        private static Vector3 Man(float x, float y) => new(x, y, 1.7f);

        [Fact]
        public void CE4369_InAWood_ASoldierIsSeenAt20m_AndHiddenAt100m()
        {
            var w = Wood();
            Assert.False(w.SegmentBlocked(Man(250, 250), Man(270, 250)), "20 m apart in the wood: seen");
            Assert.True(w.SegmentBlocked(Man(250, 250), Man(350, 250)), "100 m apart in the wood: hidden");
            Assert.False(w.SegmentBlocked(Man(-300, 250), Man(-200, 250)), "100 m apart beside the wood: seen");
        }

        [Fact]
        public void CE4369_FromTheAir_UnderTheTreesIsHidden_TheEdgeIsSeen()
        {
            var w = Wood();
            Assert.True(w.SegmentBlocked(new Vector3(250, 250, 400), Man(250, 250)), "straight down through the crown: hidden");
            Assert.False(w.SegmentBlocked(new Vector3(-1000, 250, 400), Man(5, 250)), "5 m inside the edge, the aircraft on that side: seen");
            Assert.True(w.SegmentBlocked(new Vector3(-1000, 250, 400), Man(250, 250)), "250 m inside, the same aircraft: hidden");
        }

        [Fact]
        public void CE4369_ASparseWood_HidesLess()
        {
            var sparse = Wood(""","density":20""");
            Assert.False(sparse.SegmentBlocked(Man(250, 250), Man(350, 250)), "20 trees / ha: 100 m is still seen");
            Assert.Equal(20f, sparse.Woods!.DensityAt(250, 250));
        }

        [Fact]
        public void CE4369_TheSightTrace_ReportsTheWoodsAsOneCrossing()
        {
            var r = Wood().QuerySight(Man(250, 250), Man(350, 250));
            var c = Assert.Single(r.Crossed);
            Assert.Equal(TerrainWorld.WoodsKind, c.Kind);
            Assert.True(r.Transmittance < TerrainWorld.SightThreshold);
        }

        // ── S2: a lone tree (R-4190 T-1, T-6) ─────────────────────────────────────────────────────────────────────────────────────
        private const string LoneTree = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"tree","label":"oak"},"geometry":{"type":"Point","coordinates":[30,30]}}]}
            """;

        [Fact]
        public void CE4369_ALoneTree_IsATrunk_ThatBlocksSight_AndStopsSmallArms()
        {
            var w = TerrainWorldParser.Parse(LoneTree, "tree");
            var trunk = Assert.Single(w.Prisms);
            Assert.Equal(TerrainPrismKind.Tree, trunk.Kind);
            Assert.Equal(TerrainWorldParser.TreeTrunkHeight, trunk.TopZ - trunk.BaseZ, 3);
            Assert.Equal(2 * TerrainWorldParser.TreeTrunkRadius, trunk.Max.X - trunk.Min.X, 2);
            Assert.True(w.SegmentBlocked(Man(20, 30), Man(40, 30)), "a line through the trunk is blocked");
            Assert.False(w.SegmentBlocked(Man(20, 31), Man(40, 31)), "a metre beside it is clear");
            var fire = w.QueryFire(Man(20, 30), Man(40, 30));
            var hit = Assert.Single(fire);
            Assert.True(hit.ResistanceMmRha >= 100f, $"a trunk stops small arms ({hit.ResistanceMmRha} mm RHA)");
        }

        [Fact]
        public void CE4369_ALoneTree_IsAnObstacleInTheNavmesh()
        {
            var nav = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory { Layers = Fdp.Toolkit.Navigation.NavLayerMask.Vehicle, TileCache = null }
                .Build(TerrainWorldParser.Parse(LoneTree, "tree"))!;
            // IsWalkable snaps within its search box, so a 0.3 m trunk is read by where the point lands: off the trunk by its radius + the agent's
            Assert.True(nav.ProjectToNavmesh(new Vector3(30, 30, 0), out var onTrunk));
            float off = Vector2.Distance(new Vector2(onTrunk.X, onTrunk.Y), new Vector2(30, 30));
            Assert.True(off >= 1.5f, $"the trunk is a hole in the vehicle mesh (the point moved {off:F2} m)");
            Assert.True(nav.ProjectToNavmesh(new Vector3(10, 30, 0), out var open));
            Assert.True(Vector2.Distance(new Vector2(open.X, open.Y), new Vector2(10, 30)) < 0.1f, "open ground stays where it is");
        }

        // ── S3: who may drive through a wood (R-4190 T-3, Q-T1 / Q-T2) ───────────────────────────────────────────────────────────────
        // A wood 100 m deep from edge to edge of the map: the only way east is through it.
        private const string WoodBarrier = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,300,80],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"surface","surface":"forest"},
              "geometry":{"type":"Polygon","coordinates":[[[100,-10],[200,-10],[200,90],[100,90],[100,-10]]]}}]}
            """;

        [Fact]
        public void CE4369_AWood_StopsAWheeledVehicle_ButNotATrackedOne_NorInfantry()
        {
            var nav = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory { TileCache = null }
                .Build(TerrainWorldParser.Parse(WoodBarrier, "barrier"))!;
            Vector3 west = new(40, 40, 0), east = new(260, 40, 0);
            uint wheeled = (uint)Fdp.Toolkit.Navigation.NavLayerMask.Vehicle;
            uint tracked = wheeled | Fdp.Toolkit.Navigation.NavLayerSelection.TrackedModifier;
            uint infantry = (uint)Fdp.Toolkit.Navigation.NavLayerMask.Infantry;
            Assert.True(nav.IsWalkable(new Vector3(150, 40, 0)), "the wood is ground: the vehicle mesh covers it");
            Assert.False(nav.PathExists(west, east, wheeled), "a wheeled vehicle may not enter a wood");
            Assert.True(nav.PathExists(west, east, tracked), "a tracked vehicle drives through it");
            Assert.True(nav.PathExists(west, east, infantry), "infantry walks through it");
        }

        // A wood with a 30 m gap of open ground at its north end: a tracked vehicle drives round it rather than through (×4).
        private const string WoodWithGap = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,300,200],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"surface","surface":"forest"},
              "geometry":{"type":"Polygon","coordinates":[[[100,-10],[200,-10],[200,170],[100,170],[100,-10]]]}}]}
            """;

        [Fact]
        public void CE4369_ATrackedVehicle_PrefersTheWayRound_InfantryCutsThrough()
        {
            var nav = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory { TileCache = null }
                .Build(TerrainWorldParser.Parse(WoodWithGap, "gap"))!;
            Vector3 west = new(40, 60, 0), east = new(260, 60, 0);
            float tracked = nav.PathCost(west, east, (uint)Fdp.Toolkit.Navigation.NavLayerMask.Vehicle | Fdp.Toolkit.Navigation.NavLayerSelection.TrackedModifier);
            float infantry = nav.PathCost(west, east, (uint)Fdp.Toolkit.Navigation.NavLayerMask.Infantry);
            Assert.True(tracked > 300f, $"tracked goes round the wood ({tracked:F0} m)");
            Assert.True(infantry < 240f, $"infantry walks straight through ({infantry:F0} m)");
        }
    }
}
