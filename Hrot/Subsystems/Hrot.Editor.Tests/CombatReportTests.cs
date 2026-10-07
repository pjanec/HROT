using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests
{
    /// <summary>
    /// ⭐ Tuning T-4 — <c>GET /combat/shots</c> reads the shot log the combat systems write; <c>GET /perception/los</c> explains the
    /// line of sight with the same strategy perception composes (📄 docs/DESIGN_Terrain_Combat_Tuning.md §4).
    /// </summary>
    public sealed class CombatReportTests
    {
        [Fact]
        public void Shots_ReadTheLog_NewestFirst_FilteredByShooter_WithNetworkIds()
        {
            using var world = new EntityRepository();
            var map = new NetworkEntityMap();
            var a = world.CreateEntity(); map.Register(1001, a);
            var b = world.CreateEntity(); map.Register(1002, b);
            Assert.Contains("no round has been fired", (string?)CombatReport.Shots(world, map, 20, null, null)["note"]);

            var log = ShotLog.For(world);
            log.Add(new ShotRecord { Shooter = a, Target = b, Bullet = new Entity(50, 1), Penetration = 5f, PenetrationSource = "Explicit: mount" });
            log.Add(new ShotRecord { Shooter = b, Target = a, Bullet = new Entity(51, 1) });
            var all = (JsonArray)CombatReport.Shots(world, map, 20, null, null)["shots"]!;
            Assert.Equal(2, all.Count);
            Assert.Equal(1002L, (long?)all[0]!["shooter"]);          // newest first
            var mine = (JsonArray)CombatReport.Shots(world, map, 20, 1001, null)["shots"]!;
            var shot = Assert.Single(mine)!;
            Assert.Equal(1002L, (long?)shot["target"]);
            Assert.Equal("InFlight", (string?)shot["outcome"]);
            Assert.Equal("Explicit: mount", (string?)shot["penetrationSource"]);
        }

        [Fact]
        public void Los_ExplainsTheWallThatHidesTheTarget_AndTheClearLine()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterManagedComponent<TerrainWorld>();
            world.SetSingletonManaged(TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 3, "label": "W" }, "geometry": { "type": "LineString", "coordinates": [[10,-5],[10,5]] } } ] }
            """));
            var map = new NetworkEntityMap();
            Entity At(long id, float x, float y)
            {
                var e = world.CreateEntity();
                world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
                map.Register(id, e);
                return e;
            }
            At(1, 0, 0); At(2, 20, 0); At(3, 0, 20);

            var hidden = CombatReport.Los(world, map, 1, 2);
            Assert.False((bool)hidden["visible"]!);
            Assert.Contains("not seen", (string?)hidden["verdict"]);
            var firstPoint = hidden["points"]![0]!;
            Assert.Contains("terrain", (string?)firstPoint["verdict"]);
            Assert.Equal("W", (string?)firstPoint["crossed"]![0]!["label"]);
            Assert.Equal(1.7f, (float)hidden["eyeHeight"]!, 3);

            var clear = CombatReport.Los(world, map, 1, 3);
            Assert.True((bool)clear["visible"]!);
            Assert.Equal(3, ((JsonArray)clear["points"]!).Count);   // a standing soldier's three body points

            Assert.NotNull(CombatReport.Los(world, map, 1, 99)["error"]);
        }
    }
}
