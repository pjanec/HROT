using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Spatial.Bepu;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-5032</c> GW-1 (📄 docs/DESIGN_Glass_Windows.md D-1, D-2, D-5, D-10) — a glazed window carries a thin GLASS PANE that the sight, fire and
    /// sound queries cross (sight unchanged, 20 dB of sound, ≈ 0.3 mm of resistance), that is NOT a prism (the navmesh, physics and cover never see it),
    /// never stops a hull, casts no blast shadow, spares no wall and leaves no decal, and that goes with its wall where the wall is breached or the
    /// building collapsed.
    /// </summary>
    public class GlassPaneTests
    {
        // the brick house of BreachFixtures with a window in its south wall: x 22 .. 23.2, z 0.9 .. 2.1 (the template's sill and head)
        private static string WindowHouse(string window) => $$"""
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick", "openings": [ {{window}} ] },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        private const string Window = """{ "kind": "window", "at": 2, "width": 1.2 }""";
        private static TerrainWorld House(string window = Window) => TerrainWorldParser.Parse(WindowHouse(window), "range");

        // straight through the middle of the window, from outside into the room
        private static readonly Vector3 Outside = new(22.6f, 15f, 1.5f), Inside = new(22.6f, 25f, 1.5f);

        [Fact]
        public void AGlazedWindow_HasOnePane_InItsOpening_AndThePaneIsNoPrism()
        {
            var w = House();
            int south = BreachFixtures.SouthWall(w);
            Assert.Single(w.PaneLeaves);
            Assert.Equal((south, 0), w.Panes[0]);
            var pane = w.PaneLeaves[0];
            Assert.Equal(TerrainWorld.PaneMaterial, pane.Material?.Name);
            Assert.Equal(0.9f, pane.BaseZ, 3);
            Assert.Equal(2.1f, pane.TopZ, 3);
            Assert.Equal(22f, pane.Min.X, 2);
            Assert.Equal(23.2f, pane.Max.X, 2);
            Assert.Equal(south, pane.Panel);
            Assert.Equal(0, pane.Building);
            Assert.DoesNotContain(w.Prisms, p => p.Material?.Name == TerrainWorld.PaneMaterial);   // ⛔ the navmesh / physics / cover never see it
            Assert.Same(pane, w.QueryLeaves[^1]);
        }

        [Theory]
        [InlineData("""{ "kind": "window", "at": 2, "width": 1.2, "glazed": false }""")]   // a broken-out window, authored
        [InlineData("""{ "kind": "gap", "at": 2, "width": 1.2 }""")]
        [InlineData("""{ "kind": "door", "at": 2, "width": 1.2 }""")]
        public void NoGlass_NoPane(string opening)
        {
            var w = House(opening);
            Assert.Empty(w.PaneLeaves);
            Assert.DoesNotContain(w.QuerySight(Outside, Inside).Crossed, c => c.Kind == TerrainWorld.PaneKind);
        }

        [Fact]
        public void OnlyAWindow_MayBeGlazed()
            => Assert.Throws<ArgumentException>(() => House("""{ "kind": "gap", "at": 2, "width": 1.2, "glazed": true }"""));

        [Fact]
        public void Sight_CrossesThePane_AndSeesThroughIt()
        {
            var w = House();
            var r = w.QuerySight(Outside, Inside);
            var pane = Assert.Single(r.Crossed, c => c.Kind == TerrainWorld.PaneKind);
            Assert.Equal(TerrainWorld.PaneMaterial, pane.Material);
            Assert.Equal(1f, pane.Transmittance);
            Assert.Equal(1f, r.Transmittance);                       // a pane changes NO visibility (D-5)
            Assert.False(w.SegmentBlocked(Outside, Inside));
        }

        [Fact]
        public void Fire_CrossesThePane_ForAFractionOfAMillimetre_AndLeavesNoDecal()
        {
            var w = House();
            var pane = Assert.Single(w.QueryFire(Outside, Inside), c => c.Kind == TerrainWorld.PaneKind);
            Assert.InRange(pane.ResistanceMmRha, 0.1f, 1f);

            using var repo = BreachFixtures.World(w);
            var q = TerrainWorldQuery.ForView(repo, w);
            var fire = new List<TraceCrossing>();
            q.Trace(Outside, Inside, TracePurpose.Fire, fire);
            Assert.DoesNotContain(fire, c => c.ClosedBarrier);       // not a barrier: a blast is not stopped by it
            Assert.Equal(1f, AreaEffect.TerrainShadow(fire, 0f, 10f)); // nor does it cast a shadow of its own

            float damage = 100f, pen = 5f;
            var impacts = new List<RoundImpact>();
            Assert.False(TerrainPenetration.Carry(q, Outside, Inside, ref damage, ref pen, out _, null, impacts));
            Assert.Empty(impacts);                                    // ⛔ no decal on glass: its cracks are its window's state (GW-2/3)
        }

        [Fact]
        public void Sound_PaysTheGlass_20dB()
        {
            var w = House();
            using var repo = BreachFixtures.World(w);
            var sound = new List<TraceCrossing>();
            TerrainWorldQuery.ForView(repo, w).Trace(Outside, Inside, TracePurpose.Sound, sound);
            var pane = Assert.Single(sound);
            Assert.Equal(TerrainWorld.PaneKind, pane.Kind);
            Assert.Equal(20f, pane.Loss);
        }

        [Fact]
        public void AHull_NeverMeetsThePane()
        {
            var w = House();
            var hull = new List<TerrainWorld.FireCrossing>();
            w.QueryHull(Outside, Inside, hull, null, 0.5f);
            Assert.DoesNotContain(hull, c => c.Kind == TerrainWorld.PaneKind);
        }

        [Fact]
        public void ABreachOverTheWindow_TakesTheGlass()
        {
            var w = House();
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(BreachFixtures.SouthWall(w), 1.5f, 3.5f, 0f, 2.5f, BreachKind.Passage));
            var doors = DoorStates.Of(repo, w);
            Assert.DoesNotContain(w.QuerySight(Outside, Inside, doors).Crossed, c => c.Kind == TerrainWorld.PaneKind);
            Assert.DoesNotContain(w.QueryFire(Outside, Inside, doors), c => c.Kind == TerrainWorld.PaneKind);
        }

        [Fact]
        public void ABreachBesideTheWindow_LeavesTheGlass()
        {
            var w = House();
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(BreachFixtures.SouthWall(w), 5f, 7f, 0f, 2.5f, BreachKind.Passage));
            Assert.Single(w.QueryFire(Outside, Inside, DoorStates.Of(repo, w)), c => c.Kind == TerrainWorld.PaneKind);
        }

        [Fact]
        public void ACollapse_TakesTheGlass()
        {
            var w = House();
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Collapsed(repo, w, 0);
            Assert.DoesNotContain(w.QueryFire(Outside, Inside, DoorStates.Of(repo, w)), c => c.Kind == TerrainWorld.PaneKind);
        }

        [Fact]
        public void TheSpatialIndex_FindsThePane()
        {
            var w = House();
            var before = w.QueryFire(Outside, Inside).Select(c => (c.Kind, c.Label)).ToList();
            using var index = BepuTerrainIndex.AttachTo(w);
            var after = w.QueryFire(Outside, Inside).Select(c => (c.Kind, c.Label)).ToList();
            Assert.Contains(after, c => c.Kind == TerrainWorld.PaneKind);
            Assert.Equal(before, after);
        }
    }
}
