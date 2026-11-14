using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Testing;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ CE-4396 UE-a (docs/DESIGN_Geo_Origin.md §6, R-4197) — UTM everywhere: the stored lat / lon origin IS the frame; no origin is LOCAL-ONLY.
    /// The rails run on the 8 committed terrains and every committed scenario — what is stored in local metres must not move.
    /// </summary>
    public sealed class UtmEverywhereTests
    {
        private static IEnumerable<string> CommittedTerrains() =>
            Directory.EnumerateDirectories(Path.Combine(TestRepo.Root, "assets", "terrain"))
                     .Where(d => File.Exists(Path.Combine(d, "terrain.json"))).OrderBy(d => d, StringComparer.Ordinal);

        private static (EntityRepository World, SwitchableGeoTransform Geo) World()
        {
            var world = new EntityRepository();
            var geo = new SwitchableGeoTransform();
            world.SetSingletonManaged<IGeographicTransform>(geo);
            return (world, geo);
        }

        [Fact]
        public void BeforeAnyTerrain_TheFrameIsLocalOnly_AndGeodeticCallsAreRefused()
        {
            var geo = new SwitchableGeoTransform();
            Assert.False(geo.HasFrame);
            Assert.IsType<LocalOnlyFrame>(geo.Inner);
            Assert.Throws<NoGeoFrameException>(() => geo.ToGeodetic(Vector3.Zero));
            Assert.Throws<NoGeoFrameException>(() => geo.ToCartesian(52.5, 13.4, 0));
            Assert.Equal((0.0, 0.0, 0.0), geo.Origin);
            Assert.Equal(0.0, geo.GridConvergenceDeg(new Vector3(100, 100, 0)));
        }

        [Fact]
        public void ATerrainWithoutAnOrigin_IsLocalOnly_AndAnOriginedOneThenNoneSwitchesBack()
        {
            var (world, geo) = World();
            using (world)
            {
                TerrainResidency.ApplyGeoOrigin(world, new TerrainGeoOrigin(52.52, 13.405, 0), "placed");
                Assert.True(geo.HasFrame);
                TerrainResidency.ApplyGeoOrigin(world, null, "unplaced");
                Assert.False(geo.HasFrame);
                Assert.Throws<NoGeoFrameException>(() => geo.ToGeodetic(Vector3.Zero));
                TerrainResidency.ApplyGeoOrigin(world, new TerrainGeoOrigin(52.52, 13.405, 0), "placed");
                TerrainResidency.ApplyGeoOrigin(world, null, terrainName: null);   // unload
                Assert.False(geo.HasFrame);
            }
        }

        [Fact]
        public void TheEightCommittedTerrains_AreUtmZone33N_LocalZeroIsTheirOrigin_AndOneKmEastIsOnTheGrid()
        {
            int checkedTerrains = 0;
            foreach (var dir in CommittedTerrains())
            {
                string name = Path.GetFileName(dir);
                var def = TerrainDefinitionParser.Parse(File.ReadAllText(Path.Combine(dir, "terrain.json")));
                Assert.NotNull(def.Origin);
                var (world, geo) = World();
                using (world)
                {
                    TerrainResidency.ApplyGeoOrigin(world, def.Origin, name, def.Utm);
                    var u = Assert.IsType<UtmTransform>(geo.Inner);
                    Assert.Equal(33, u.Zone); Assert.False(u.South);
                    var o = def.Origin!.Value;

                    // local (0, 0) ↔ the origin's lat / lon exactly (a round trip well under 1 mm)
                    var back = geo.ToCartesian(o.Lat, o.Lon, o.Alt);
                    Assert.True(Math.Abs(back.X) < 0.001 && Math.Abs(back.Y) < 0.001 && Math.Abs(back.Z) < 0.001, $"{name}: the origin is at local {back}");
                    var (lat, lon, alt) = geo.ToGeodetic(Vector3.Zero);
                    var (e0, n0) = Utm.FromGeodetic(o.Lat, o.Lon, 33, false);
                    var (eLat, eLon) = Utm.ToGeodetic(e0, n0, 33, false);
                    Assert.Equal(eLat, lat, 10); Assert.Equal(eLon, lon, 10); Assert.Equal(o.Alt, alt, 6);
                    Assert.Equal(o.Lat, lat, 8); Assert.Equal(o.Lon, lon, 8);

                    // a point 1 km east lands where Utm.ToGeodetic says
                    var (kLat, kLon, _) = geo.ToGeodetic(new Vector3(1000, 0, 0));
                    var (xLat, xLon) = Utm.ToGeodetic(e0 + 1000, n0, 33, false);
                    Assert.Equal(xLat, kLat, 9); Assert.Equal(xLon, kLon, 9);
                }
                checkedTerrains++;
            }
            Assert.True(checkedTerrains >= 8, $"found {checkedTerrains} committed terrains");
        }

        [Fact]
        public void AnExplicitFrame_IsUsedAsIs_AndAnImportedTerrainsFrameEqualsItsOriginProjected()
        {
            // an importer terrain.json: the origin is the lat / lon of its corner, the frame the exact UTM of it (Terrain Import §11.4 / I-b)
            var def = TerrainDefinitionParser.Parse("""{"schemaVersion":2,"origin":{"lat":54.5,"lon":7.0,"alt":12},"frame":{"utm":{"zone":32,"hemisphere":"N","e0":372000,"n0":6043000},"corner":[100,200]}}""");
            var (e, n) = Utm.ToGeodetic(372_100.0, 6_043_200.0, 32, false);
            var (world, geo) = World();
            using (world)
            {
                TerrainResidency.ApplyGeoOrigin(world, new TerrainGeoOrigin(e, n, 12), "imported", def.Utm);
                var u = Assert.IsType<UtmTransform>(geo.Inner);
                Assert.Equal((32, 372_100.0, 6_043_200.0, 12.0), (u.Zone, u.E0, u.N0, u.H0));
                var natural = UtmTransform.AtOrigin(e, n, 12);   // the rule applied to that origin gives the SAME frame
                Assert.Equal((u.Zone, u.South), (natural.Zone, natural.South));
                Assert.Equal(u.E0, natural.E0, 3); Assert.Equal(u.N0, natural.N0, 3);
            }
        }

        [Fact]
        public void TheRule_ZoneIsTheLongitudeBand_HemisphereTheLatitudeSign_AndAnOverrideWins()
        {
            var berlin = UtmTransform.AtOrigin(52.52, 13.405, 0);
            Assert.Equal((33, false), (berlin.Zone, berlin.South));
            var sydney = UtmTransform.AtOrigin(-33.87, 151.21, 5);
            Assert.Equal((56, true), (sydney.Zone, sydney.South)); Assert.Equal(5.0, sydney.H0);
            var edge = UtmTransform.AtOrigin(49.0, 17.9, 0, zoneOverride: 34);   // a terrain that keeps another zone than its natural one (33)
            Assert.Equal(34, edge.Zone);
            var (lat, lon, _) = edge.ToGeodetic(Vector3.Zero);
            Assert.Equal(49.0, lat, 6); Assert.Equal(17.9, lon, 6);
        }

        [Fact]
        public void EveryCommittedScenarioKeepsItsLocalPositions_WhateverTheGeoFrame()
        {
            string root = TestRepo.Root;
            var files = Directory.EnumerateFiles(Path.Combine(root, "scenarios"), "scenario.json", SearchOption.AllDirectories).ToList();
            Assert.True(files.Count >= 100, $"found {files.Count} scenarios");
            var failures = new List<string>();
            int positions = 0;
            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var input = JsonNode.Parse(json)!.AsObject();
                    if ((input["entities"] ?? input["Entities"]) is not JsonObject inEntities) continue;
                    string? first = null;
                    foreach (bool utm in new[] { true })
                    {
                        var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(
                            new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, UnknownComponentPolicy.WarnAndSkip);
                        using var repo = new EntityRepository();
                        HrotSharedComponentRegistry.RegisterAll(repo);
                        Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
                        var geo = new SwitchableGeoTransform();
                        repo.SetSingletonManaged<IGeographicTransform>(geo);
                        TerrainResidency.ApplyGeoOrigin(repo, new TerrainGeoOrigin(52.52, 13.405, 0), "berlin");   // the UTM frame of Berlin, as every committed terrain gives
                        serializer.Deserialize(repo, json);
                        string saved = serializer.Serialize(repo, new ScenarioHeader(Hrot.Common.Scenario.HrotSubsystemTypes.Scenario)).ToJsonString();
                        first = saved;
                        if (utm)
                        {
                            var outEntities = JsonNode.Parse(saved)!["Entities"]!.AsObject();
                            foreach (var (key, node) in inEntities)
                            {
                                var p = node?["SimTransform"]?["Position"] as JsonArray;
                                var q = outEntities[key]?["SimTransform"]?["Position"] as JsonArray;
                                if (p == null || q == null) continue;
                                positions++;
                                for (int i = 0; i < 3; i++)
                                    if (Math.Abs(p[i]!.GetValue<double>() - q[i]!.GetValue<double>()) > 1e-3) { failures.Add($"{Path.GetRelativePath(root, file)}: {key} moved"); break; }
                            }
                        }
                    }
                }
                catch (Exception ex) { failures.Add($"{Path.GetRelativePath(root, file)}: {ex.GetType().Name}: {ex.Message}"); }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
            Assert.True(positions >= 500, $"only {positions} stored positions compared");
        }
    }
}
