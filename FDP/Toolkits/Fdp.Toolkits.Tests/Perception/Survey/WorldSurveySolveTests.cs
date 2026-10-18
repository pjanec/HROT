using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Perception.Survey.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S2 (docs/DESIGN_World_Survey_Sensor.md §5) — the muscle's answers on the SHIPPED bt-range's House A (a
    /// <c>house-2f</c> at (100, 100), its front door LOCKED by the terrain): a fan sees through an open window and not through a wall, a
    /// cloud in front of the window stops it as an obscurant, a door stops it until it is opened; the building survey reads door states
    /// live; a point is placed in its building, level and room.
    /// </summary>
    public sealed class WorldSurveySolveTests : IDisposable
    {
        private readonly TerrainWorld _terrain;
        private readonly EntityRepository _w;

        public WorldSurveySolveTests()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            _terrain = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
            _w = Fdp.Toolkit.Terrain.Tests.DoorFixtures.WorldWithDoors(_terrain);
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<AreaEffectCloud>();
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
        }

        public void Dispose() => _w.Dispose();

        private int HouseA => _terrain.Buildings.ToList().FindIndex(b => b.Label == "House A");

        private SurveyRay OneRay(float x, float y, float bearing, float range = 40f)
        {
            var req = WorldSurveyRequest.Fan(new[] { new SurveyOrigin { X = x, Y = y, Z = 0f, Bearing = bearing, HalfArc = 0f } }, rays: 1, range: range);
            return Assert.Single(WorldSurveySolve.Solve(_w, req).Rays);
        }

        private void Door(string id, TerrainDoorState state)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new DoorState { State = state });
            _w.SetManagedComponent(e, new TerrainObjectKey { Key = "bt-range/House A/" + id });
        }

        [Fact]
        public void CE3249_AFanSeesOutThroughAWindow_AndNotThroughAWall()
        {
            var south = OneRay(102.1f, 102f, -MathF.PI / 2f);   // through the front window (x 101.5…102.7, sill 0.9, head 2.1)
            Assert.True(south.Free > 30f, $"through the window: free {south.Free}");
            var east = OneRay(102.1f, 102f, 0f);                 // the inner wall at x = 105
            Assert.Equal(SurveyStop.Structure, east.Stop);
            Assert.InRange(east.Free, 2.5f, 3.2f);
        }

        [Fact]
        public void CE3249_ACloudInFrontOfTheWindow_StopsTheFan_AsAnObscurant()
        {
            var cloud = _w.CreateEntity();
            _w.AddComponent(cloud, new SimTransform { Position = new Vector3(102.1f, 95f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(cloud, new AreaEffectCloud { Kind = CloudKind.Smoke, Radius = 3f, Height = 4f, SightTransmittance = 0.1f,
                                                         BornAtSimSeconds = 0, Seconds = 60f });
            var south = OneRay(102.1f, 102f, -MathF.PI / 2f);
            Assert.Equal(SurveyStop.Obscurant, south.Stop);
            Assert.InRange(south.Free, 3f, 5f);   // the cloud's edge, 4 m south
        }

        [Fact]
        public void CE3249_TheLockedFrontDoor_StopsTheFan_UntilItIsOpen()
        {
            var shut = OneRay(104.2f, 102f, -MathF.PI / 2f);     // the front door: x 103.75…104.65
            Assert.Equal(SurveyStop.Structure, shut.Stop);
            Door("front", TerrainDoorState.Open);
            var open = OneRay(104.2f, 102f, -MathF.PI / 2f);
            Assert.True(open.Free > 30f, $"the open door: free {open.Free}");
        }

        [Fact]
        public void CE3249_TheBuildingSurvey_GivesRoomsPortalsFaces_AndTheDoorsAsTheyAreNow()
        {
            var answer = WorldSurveySolve.Solve(_w, WorldSurveyRequest.OfBuildings(HouseA));
            var b = Assert.Single(answer.Buildings);
            Assert.Equal("House A", b.Label);
            Assert.Equal(2, b.RoofLevel);
            Assert.Equal(4, answer.Rooms.Count);   // 2 below, 1 above, the roof
            Assert.Equal(8, answer.Faces.Count);
            var front = Assert.Single(answer.Portals, p => p.IsDoor && p.RoomB == -1 && p.X < 105f);
            Assert.Equal((byte)TerrainDoorState.Locked, front.DoorState);

            Door("front", TerrainDoorState.Destroyed);
            var after = WorldSurveySolve.Solve(_w, WorldSurveyRequest.OfBuildings(HouseA));
            Assert.Equal((byte)TerrainDoorState.Destroyed, Assert.Single(after.Portals, p => p.IsDoor && p.RoomB == -1 && p.X < 105f).DoorState);
        }

        [Fact]
        public void CE3249_AnAreaSurvey_FindsTheHousesItMeets()
        {
            var answer = WorldSurveySolve.Solve(_w, WorldSurveyRequest.OfArea(new Vector2(105f, 104f), 2f));
            Assert.Equal(HouseA, Assert.Single(answer.Buildings).Building);
        }

        [Fact]
        public void CE3249_APointSurvey_PlacesThePoint()
        {
            var answer = WorldSurveySolve.Solve(_w, WorldSurveyRequest.OfPoints(new[]
            {
                new Vector3(102f, 104f, 0f),    // the ground west room
                new Vector3(102f, 104f, 6f),    // the roof
                new Vector3(80f, 80f, 0f),      // outside
            }));
            var room = answer.Points[0];
            Assert.Equal((HouseA, 0), (room.Building, room.Level));
            Assert.True(room.Room >= 0 && room.Indoors);
            var roof = answer.Points[1];
            Assert.Equal(2, roof.Level);
            Assert.True(roof.Room >= 0);
            Assert.False(roof.Indoors, "the roof is outdoors");
            Assert.Equal(6f, roof.RoofZ, 3);
            var outside = answer.Points[2];
            Assert.Equal(-1, outside.Building);
            Assert.Equal(_terrain.GroundHeightAt(80f, 80f), outside.GroundZ, 3);
        }

        [Fact]
        public void CE3249_TheRequestAndTheAnswer_RoundTripAsJson_AndTheHashIsStable()
        {
            var req = WorldSurveyRequest.Fan(new[] { new SurveyOrigin { X = 1, Y = 2, Z = 3, Bearing = 0.5f, HalfArc = 0.2f } }, rays: 7, range: 80f);
            Assert.True(WorldSurveyRequestJson.TryDecode(WorldSurveyRequestJson.Encode(req), out var back));
            Assert.Equal((WorldSurveyKind.VisibilityFan, 7, 80f, 0.5f), (back!.Kind, back.Rays, back.Range, back.Origins[0].Bearing));
            Assert.False(WorldSurveyRequestJson.TryDecode("{not json", out _));

            var answer = WorldSurveySolve.Solve(_w, WorldSurveyRequest.OfBuildings(HouseA));
            string json = WorldSurveyRequestJson.EncodeAnswer(answer);
            var again = WorldSurveyRequestJson.DecodeAnswer(json)!;
            Assert.Equal(answer.Portals.Count, again.Portals.Count);
            Assert.Equal(WorldSurveyRequestJson.Hash(json), WorldSurveyRequestJson.Hash(WorldSurveyRequestJson.EncodeAnswer(again)));
        }
    }
}
