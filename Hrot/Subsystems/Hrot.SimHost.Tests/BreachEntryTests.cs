using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4602</c> (B-5; 📄 docs/DESIGN_Vehicle_Breaching.md §7.2 V-19) — EVERY BREACH IS A WAY IN: a house with NO door has no way in for a clearing squad; a
    /// PASSAGE in its wall (a tank's bump, a charge, the bulldozer's cut) is a Gap portal from outside in the building survey the same tick, and the clearing's room
    /// order enters by it. 🔴 Red-proof: drop <c>AppendBreachPortals</c> from <c>WorldSurveySolve</c> ⇒ no portal, no room to clear.
    /// </summary>
    public sealed class BreachEntryTests
    {
        private const string DoorlessHouse = """
            { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 60, 60], "groundZ": 0 }, "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3 }, { "from": [10,0], "to": [10,8], "thickness": 0.3 },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3 }, { "from": [0,8], "to": [0,0], "thickness": 0.3 } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        [Fact]
        public void ADoorlessHouse_IsEnteredByAPassageInItsWall()
        {
            var t = TerrainWorldParser.Parse(DoorlessHouse, "range");
            using var w = new EntityRepository();
            w.RegisterComponent<BuildingDamage>(); w.RegisterManagedComponent<TerrainObjectKey>(); w.RegisterComponent<DoorState>(); w.RegisterComponent<SimTransform>();
            w.SetSingletonManaged(t);
            w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
            var squad = new Vector3(25f, 10f, 0f);

            var before = WorldSurveySolve.Solve(w, WorldSurveyRequest.OfBuildings(0));
            Span<byte> states = stackalloc byte[before.Rooms.Count];
            Assert.False(RoomOrder.Next(before, 0, states, squad, out _, out _), "no way in");

            int south = Array.FindIndex(t.Panels.ToArray(), p => MathF.Abs(p.A.Y - 20f) < 1e-3f && MathF.Abs(p.B.Y - 20f) < 1e-3f);
            var d = new BuildingDamage { Building = 0 };
            BuildingDamages.Add(ref d, new WallBreach(south, 4f, 5.4f, 0f, 2.2f, BreachKind.Passage), t);
            var e = w.CreateEntity(); w.AddComponent(e, d); w.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(t, 0) });

            var after = WorldSurveySolve.Solve(w, WorldSurveyRequest.OfBuildings(0));
            var gap = Assert.Single(after.Portals, p => p.Kind == (byte)BuildingPortalKind.Gap && p.RoomB == -1);
            Assert.Equal(24.7f, gap.X, 1); Assert.Equal(20f, gap.Y, 1);
            Span<byte> states2 = stackalloc byte[after.Rooms.Count];
            Assert.True(RoomOrder.Next(after, 0, states2, squad, out var room, out var way));
            Assert.Equal(gap.X, way.X); Assert.True(room.Indoors);
        }
    }
}
