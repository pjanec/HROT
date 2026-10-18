using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S1 (docs/DESIGN_World_Survey_Sensor.md §6) — the rooms, portals and faces DERIVED from a building's walls and
    /// openings, on the SHIPPED bt-range's <c>house-2f</c> (House A at rotation 0, House B at 90°): no authored room data.
    /// </summary>
    public class BuildingLayoutTests
    {
        private static TerrainWorld BtRange()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            return TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
        }

        private static (TerrainWorld World, int Index, BuildingLayout Layout) House(string label)
        {
            var w = BtRange();
            int i = w.Buildings.ToList().FindIndex(b => b.Label == label);
            Assert.True(i >= 0, label);
            return (w, i, BuildingLayout.Derive(w, i));
        }

        // House A's local frame is the world frame shifted by its position (rotation 0): the template's metres + (100, 100).
        private static Vector2 A(float x, float y) => new(100f + x, 100f + y);

        [Fact]
        public void CE3249_HouseA_HasTwoStoreysAndARoof_TwoRoomsBelow_OneAbove_TheRoofOutdoors()
        {
            var (w, _, l) = House("House A");
            float g = w.Buildings.Single(b => b.Label == "House A").BaseZ;
            Assert.Equal(new[] { g, g + 3f, g + 6f }, l.Levels.ToArray());
            Assert.Equal(2, l.RoofLevel);

            var ground = l.Rooms.Where(r => r.Level == 0).ToList();
            Assert.Equal(2, ground.Count);   // west and east of the inner wall
            Assert.Contains(ground, r => r.Centre.X < A(5, 0).X);
            Assert.Contains(ground, r => r.Centre.X > A(5, 0).X);
            Assert.All(ground, r => Assert.True(r.Indoors, "a ground room has the upper floor above it"));

            var upper = Assert.Single(l.Rooms, r => r.Level == 1);   // no inner wall upstairs
            Assert.True(upper.Indoors);
            var roof = Assert.Single(l.Rooms, r => r.Level == 2);
            Assert.False(roof.Indoors, "the roof is outdoors");
            Assert.InRange(roof.Area, 60f, 80f);   // the 10 × 8 m roof, less the wall bands
        }

        [Fact]
        public void CE3249_HouseA_PortalsJoinTheRightRooms()
        {
            var (w, _, l) = House("House A");
            int west = l.RoomAt(A(2.5f, 4f), 0), east = l.RoomAt(A(7f, 4f), 0), up = l.RoomAt(A(2.5f, 4f), 1);
            Assert.True(west >= 0 && east >= 0 && up >= 0 && west != east);

            BuildingPortal Door(string id) => Assert.Single(l.Portals, p => p.Door == w.DoorIndexOf("bt-range/House A/" + id));
            var hall = Door("hall");
            Assert.Equal(BuildingPortalKind.Door, hall.Kind);
            Assert.Equal(new[] { west, east }.OrderBy(x => x), new[] { hall.RoomA, hall.RoomB }.OrderBy(x => x));
            Assert.Equal(-1, hall.Face);
            var front = Door("front");
            Assert.Equal((west, -1), (front.RoomA, front.RoomB));
            Assert.True(front.Face >= 0, "an outside door lies on a face");
            Assert.Equal((east, -1), (Door("back").RoomA, Door("back").RoomB));

            var stair = Assert.Single(l.Portals, p => p.Kind == BuildingPortalKind.Stair);
            Assert.Equal((east, up), (stair.RoomA, stair.RoomB));
            Assert.InRange(stair.Width, 0.9f, 1.1f);

            var windows = l.Portals.Where(p => p.Kind == BuildingPortalKind.Window).ToList();
            Assert.Equal(5, windows.Count);   // 2 below (south, east), 3 above (2 south, 1 north)
            Assert.All(windows, p => { Assert.Equal(-1, p.RoomB); Assert.False(p.Walkable); Assert.True(p.Face >= 0); });
            Assert.Equal(8, l.Faces.Count);   // 4 outside walls on each storey
        }

        [Fact]
        public void CE3249_APointInTheStairwell_IsInNoRoom_AndLevelsReadFromHeight()
        {
            var (w, _, l) = House("House A");
            float g = w.Buildings.Single(b => b.Label == "House A").BaseZ;
            Assert.Equal(-1, l.RoomAt(A(8f, 3.5f), 1));   // the open stairwell upstairs
            Assert.True(l.RoomAt(A(8f, 3.5f), 0) >= 0, "below it, the east room");
            Assert.Equal(0, l.LevelAt(g));
            Assert.Equal(1, l.LevelAt(g + 3f));
            Assert.Equal(2, l.LevelAt(g + 6.1f));
            Assert.Equal(-1, l.RoomAt(A(-3f, 4f), 0));     // outside
        }

        [Fact]
        public void CE3249_TheRotatedHouseB_HasTheSameRooms()
        {
            var (_, _, l) = House("House B");
            Assert.Equal(2, l.Rooms.Count(r => r.Level == 0));
            Assert.Single(l.Rooms, r => r.Level == 1);
            Assert.Single(l.Portals, p => p.Kind == BuildingPortalKind.Stair);
            Assert.Equal(5, l.Portals.Count(p => p.Kind == BuildingPortalKind.Window));
        }

        // ⭐ S5 — ui's house-3f (UI-2, Defend §16 DA-22): house-2f + a roof storey with a stair house (door "roof") and a 1 m parapet;
        //   placed alone at (20, 20) for this rail.
        private static (TerrainWorld World, BuildingLayout Layout) House3f()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            const string Json = """
                {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,100,100],"groundZ":0},"features":[
                 {"type":"Feature","properties":{"kind":"building","template":"house-3f","label":"OBJ","baseZ":0,"rotation":0},
                  "geometry":{"type":"Point","coordinates":[20,20]}}]}
                """;
            var w = TerrainWorldParser.Parse(Json, "cqb", TerrainAssets.ForFolder(folder));
            return (w, BuildingLayout.Derive(w, 0));
        }

        private static Vector2 O(float x, float y) => new(20f + x, 20f + y);

        /// <summary>⭐ The platoon note's rail (BR-12 §2): the roof is ONE room (open, bounded by the parapet), the stair house a room of its
        /// own, its door the portal between them, the stair from the upper storey arrives in the stair house; the stair house's lid is
        /// no level; a point on the roof is on the roof level, in the roof room, outdoors.</summary>
        [Fact]
        public void CE3249_House3f_TheRoofIsARoom_TheStairHouseAnother_ItsDoorThePortal()
        {
            var (w, l) = House3f();
            Assert.Equal(new[] { 0f, 3f, 6f }, l.Levels.ToArray());   // the stair house's lid (8.5) is no level
            Assert.Equal(2, l.RoofLevel);

            int stairHouse = l.RoomAt(O(8f, 3f), 2), roof = l.RoomAt(O(3f, 4f), 2), upper = l.RoomAt(O(3f, 4f), 1);
            Assert.True(stairHouse >= 0 && roof >= 0 && upper >= 0 && stairHouse != roof);
            Assert.Equal(2, l.Rooms.Count(r => r.Level == 2));
            Assert.False(l.Rooms[roof].Indoors, "the roof is outdoors");
            Assert.True(l.Rooms[stairHouse].Indoors, "the stair house has its lid");

            var door = Assert.Single(l.Portals, p => p.Door == w.DoorIndexOf("cqb/OBJ/roof"));
            Assert.Equal(new[] { stairHouse, roof }.OrderBy(x => x), new[] { door.RoomA, door.RoomB }.OrderBy(x => x));
            Assert.Contains(l.Portals, p => p.Kind == BuildingPortalKind.Stair && p.RoomA == upper && p.RoomB == stairHouse);
            Assert.Equal(2, l.Portals.Count(p => p.Kind == BuildingPortalKind.Stair));
            Assert.Equal(2, l.LevelAt(6f));
            Assert.Equal(2, l.LevelAt(6.4f));
        }

        [Fact]
        public void CE3249_TheCache_DerivesOnce_AndAnInvalidateDerivesAgain()
        {
            var w = BtRange();
            int i = w.Buildings.ToList().FindIndex(b => b.Label == "House A");
            var first = BuildingLayoutCache.Of(w, i);
            Assert.Same(first, BuildingLayoutCache.Of(w, i));
            BuildingLayoutCache.Invalidate(w, i);
            var again = BuildingLayoutCache.Of(w, i);
            Assert.NotSame(first, again);
            Assert.Equal(first.Rooms.Count, again.Rooms.Count);
            Assert.Equal(i, BuildingLayoutCache.BuildingAt(w, A(2f, 2f)));
            Assert.Equal(-1, BuildingLayoutCache.BuildingAt(w, A(-20f, -20f)));
        }
    }
}
