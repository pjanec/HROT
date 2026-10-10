using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests
{
    /// <summary>
    /// ⭐ Buildings programme Stage 1 — <c>GET /terrain/levels</c>, <c>/terrain/query</c>, <c>/doors</c> read the resident
    /// <see cref="TerrainWorld"/> and report its own answers (📄 docs/DESIGN_Terrain_Combat_Tuning.md §4).
    /// </summary>
    public sealed class TerrainReportTests
    {
        private const string World = """
        { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 100, 100], "groundZ": 0 },
          "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H", "doors": { "front": "locked" },
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [ { "from": [0,0], "to": [10,0],
                  "openings": [ { "kind": "door", "at": 4.5, "width": 1, "doorId": "front" }, { "kind": "window", "at": 1.5, "width": 1.2 } ] } ] },
                             { "height": 3, "walls": [] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

        private static EntityRepository Repo(bool withTerrain = true)
        {
            var repo = new EntityRepository();
            if (withTerrain)
            {
                repo.RegisterManagedComponent<TerrainWorld>();
                repo.SetSingletonManaged(TerrainWorldParser.Parse(World, "range"));
            }
            return repo;
        }

        [Fact]
        public void Levels_ListTheGroundAsLevel0_AndEachFloorAbove()
        {
            using var repo = Repo();
            var levels = (JsonArray)TerrainReport.Levels(repo, 22, 24)["levels"]!;
            Assert.Equal(new[] { 0, 1, 2 }, levels.Select(l => (int)l!["level"]!));
            Assert.Equal(new[] { 0f, 3f, 6f }, levels.Select(l => (float)l!["z"]!));
            Assert.Equal("ground", (string?)levels[0]!["kind"]);
        }

        [Fact]
        public void Query_ReportsTheWindowAsClear_AndTheWallAsOpaque_WithTheMaterial()
        {
            using var repo = Repo();
            var window = TerrainReport.Query(repo, new Vector3(22.1f, 10, 1.6f), new Vector3(22.1f, 24, 1.6f));
            Assert.Equal(1f, (float)window["transmittance"]!);
            Assert.True((bool)window["seesThrough"]!);
            var wall = TerrainReport.Query(repo, new Vector3(23.5f, 10, 1.6f), new Vector3(23.5f, 24, 1.6f));
            Assert.Equal(0f, (float)wall["transmittance"]!);
            Assert.Equal("concrete", (string?)wall["crossed"]![0]!["material"]);
            Assert.Equal("H", (string?)wall["crossed"]![0]!["building"]);
        }

        /// <summary>⭐ R-217 — <c>purpose=fire</c>: the window is a plain opening (the round arrives whole); the concrete wall stops a rifle
        /// round and passes an anti-tank one — the same <c>TerrainPenetration.Cross</c> the bullets use.</summary>
        [Fact]
        public void QueryFire_TheWindowLetsTheRoundThrough_TheWallStopsARifle_NotAnAntiTankRound()
        {
            using var repo = Repo();
            var window = TerrainReport.QueryFire(repo, new Vector3(22.1f, 10, 1.6f), new Vector3(22.1f, 24, 1.6f), penetration: 5f, damage: 25f);
            Assert.False((bool)window["stopped"]!);
            Assert.Equal(25f, (float)window["arrivingDamage"]!);

            var rifle = TerrainReport.QueryFire(repo, new Vector3(23.5f, 10, 1.6f), new Vector3(23.5f, 24, 1.6f), penetration: 5f, damage: 25f);
            Assert.True((bool)rifle["stopped"]!);
            var wall = rifle["crossed"]![0]!;
            Assert.Equal("concrete", (string?)wall["material"]);
            Assert.False((bool)wall["passes"]!);
            Assert.Equal(1500f * (float)wall["pathMetres"]!, (float)wall["resistanceMmRha"]!, 1);

            var atgm = TerrainReport.QueryFire(repo, new Vector3(23.5f, 10, 1.6f), new Vector3(23.5f, 24, 1.6f), penetration: 800f, damage: 2000f);
            Assert.False((bool)atgm["stopped"]!);
            Assert.Equal(2000f, (float)atgm["arrivingDamage"]!);
            Assert.True((float)atgm["arrivingPenetration"]! < 800f);
        }

        [Fact]
        public void Doors_ListByKey_WithTheInitialState_AndNoRuntimeIdYet()
        {
            using var repo = Repo();
            var door = Assert.Single((JsonArray)TerrainReport.Doors(repo)["doors"]!)!;
            Assert.Equal("range/H/front", (string?)door["key"]);
            Assert.Equal("Locked", (string?)door["state"]);
            Assert.Null(door["runtimeId"]);
        }

        [Fact]
        public void Doors_NameTheDoorEntity_AndReportItsState_FromTheWorldsView()
        {
            using var repo = Repo();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            repo.RegisterComponent<Fdp.Toolkit.Terrain.DoorState>();
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(1007L));
            repo.AddComponent(e, new Fdp.Toolkit.Terrain.DoorState { State = Fdp.Toolkit.Terrain.TerrainDoorState.Open });
            repo.SetManagedComponent(e, new Fdp.Toolkit.Terrain.TerrainObjectKey { Key = "range/H/front" });

            var door = Assert.Single((JsonArray)TerrainReport.Doors(repo)["doors"]!)!;
            Assert.Equal(1007L, (long)door["runtimeId"]!);
            Assert.Equal("Open", (string?)door["entityState"]);
            Assert.Equal("Open", (string?)door["state"]);     // ⭐ R-219 — the state is the world's door entity, read from this view
        }

        [Fact]
        public void NoResidentTerrain_SaysSo_RatherThanAnEmptyAnswer()
        {
            using var repo = Repo(withTerrain: false);
            Assert.Null(TerrainReport.Levels(repo, 0, 0)["terrain"]);
            Assert.Contains("no terrain", (string?)TerrainReport.Doors(repo)["note"]);
        }
    }
}
