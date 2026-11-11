using System.Text.Json.Nodes;
using CarKinem.Road;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.Terrain.InputSet;
using Hrot.Traffic;
using Sim.Ingest;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ T-7a (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k-2) — the LOAD: a terrain with a scenario overlay that holds a bridge loads the base's road segments FIRST with the bridge's appended (so base indices stay), and the traffic
/// loader takes the overlay's SUMO net in the place of the base's — in the one <c>TerrainResidency.Prepare</c>, no second path. Needs netconvert (scripts/install-sumo.sh); without it the rail passes vacuously.
/// </summary>
public sealed class BridgeLoadTests : IDisposable
{
    private const string Key = "c2000000-0000-4000-8000-000000000002";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bld-" + Guid.NewGuid().ToString("N")[..10]);

    [Fact]
    public void TheResidency_LoadsBaseRoadsFirstThenTheBridge_AndTheTrafficLoaderTakesTheOverlaysNet()
    {
        var located = Netconvert.Locate();
        if (located.Path == null || located.Problem != null) return;
        string terrains = Path.Combine(_root, "assets", "terrain"), folder = Path.Combine(terrains, "t"), scenario = Path.Combine(_root, "scn");
        Directory.CreateDirectory(folder); Directory.CreateDirectory(scenario);
        File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2,"world":"w.geojson","roadNetworks":["roads.json"],"sumoNet":{"file":"t.net.xml","anchor":[-150,-200]}}""");
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[]}""");
        File.WriteAllText(Path.Combine(folder, "roads.json"), """
            {"$meta":{"docType":"Fdp.RoadNetwork","schemaVersion":1},
             "nodes":[{"id":0,"position":{"x":150,"y":200}},{"id":1,"position":{"x":190,"y":200}},{"id":2,"position":{"x":240,"y":200}},{"id":3,"position":{"x":300,"y":200}}],
             "segments":[
              {"id":0,"startNodeId":0,"endNodeId":1,"controlPoints":{"p0":{"x":150,"y":200},"t0":{"x":40,"y":0},"p1":{"x":190,"y":200},"t1":{"x":40,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1},
              {"id":1,"startNodeId":2,"endNodeId":3,"controlPoints":{"p0":{"x":240,"y":200},"t0":{"x":60,"y":0},"p1":{"x":300,"y":200},"t1":{"x":60,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1}]}
            """);
        File.WriteAllText(Path.Combine(_root, "n.nod.xml"), """<nodes><node id="W0" x="150" y="200"/><node id="W1" x="190" y="200"/><node id="E0" x="240" y="200"/><node id="E1" x="300" y="200"/></nodes>""");
        File.WriteAllText(Path.Combine(_root, "e.edg.xml"), """<edges><edge id="w" from="W0" to="W1" numLanes="1" speed="14"/><edge id="w-r" from="W1" to="W0" numLanes="1" speed="14"/><edge id="e" from="E0" to="E1" numLanes="1" speed="14"/><edge id="e-r" from="E1" to="E0" numLanes="1" speed="14"/></edges>""");
        Assert.Equal(0, Netconvert.Run(located.Path, new[] { "-n", Path.Combine(_root, "n.nod.xml"), "-e", Path.Combine(_root, "e.edg.xml"), "-o", Path.Combine(folder, "t.net.xml") }, null, Netconvert.SumoHomeOf(located.Path)).Code);

        var dom = new JsonObject
        {
            ["header"] = new JsonObject { ["terrainName"] = "t" },
            ["entities"] = new JsonObject { [Key] = new JsonObject
            {
                ["TerrainFeature"] = new JsonObject { ["Kind"] = TerrainFeature.Bridge, ["Template"] = "", ["HeadingDeg"] = 0, ["Pad"] = 4, ["Seed"] = 0 },
                ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(192, 200, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
                ["EditablePolyline"] = new JsonObject { ["Points"] = new JsonArray(new JsonObject { ["X"] = 0, ["Y"] = 0 }, new JsonObject { ["X"] = 46, ["Y"] = 0 }), ["Version"] = 0 },
            } },
        };
        var catalog = new TerrainCatalog(new[] { terrains });
        Assert.True(OverlayCompiler.Compile(dom, scenario, catalog.ResolveDefinition, bakeNavtiles: false, bridgeNet: new BridgeNetBuilder()).Wrote);
        string overlay = Path.Combine(scenario, OverlayCompiler.FolderName);

        TerrainResidency.Staged Load(string? ov)
        {
            using var holder = new RoadNetworkHolder();
            var res = new TerrainResidency(catalog, holder);
            res.AttachContent(new SumoNetTerrainLoader());
            return res.Prepare("t", ov);
        }
        var plain = Load(null);
        var merged = Load(overlay);
        try
        {
            Assert.Equal(2, plain.RoadNetwork.Segments.Length);
            Assert.Equal(3, merged.RoadNetwork.Segments.Length);                                   // the base's first, the bridge's appended
            for (int i = 0; i < 2; i++) Assert.Equal(plain.RoadNetwork.Segments[i].P1, merged.RoadNetwork.Segments[i].P1);
            var basePath = Assert.IsType<TrafficNetCache>(Assert.Single(plain.Contents).Prepared).Path;
            var ovCache = Assert.IsType<TrafficNetCache>(Assert.Single(merged.Contents).Prepared);
            Assert.StartsWith(Path.GetFullPath(folder), basePath);
            Assert.StartsWith(Path.GetFullPath(overlay), ovCache.Path);                            // the overlay's net, loaded where the base's is
            Assert.NotNull(new NetworkRouter(ovCache.Model).Route("w", "e"));
        }
        finally { plain.RoadNetwork.Dispose(); merged.RoadNetwork.Dispose(); }
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
