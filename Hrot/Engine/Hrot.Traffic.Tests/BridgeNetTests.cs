using System.Text.Json.Nodes;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;
using Sim.Ingest;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ T-7a (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k-2) — a bridge is road in SUMO's net too: the compile makes <c>&lt;overlay&gt;/&lt;base&gt;.net.xml</c> with <c>netconvert -s base -e bridges.edg.xml</c>. The base here is a SUMO
/// net cut by a river: a west road (<c>w</c>; netconvert shifts the net by (-150,-200), the terrain declares that as its anchor) and an east road (<c>e</c>) with a gap between x 190 and 240. Needs netconvert (scripts/install-sumo.sh); without it the rails say so and pass vacuously.
/// </summary>
public sealed class BridgeNetTests : IDisposable
{
    private const string Key = "c2000000-0000-4000-8000-000000000002";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bnt-" + Guid.NewGuid().ToString("N")[..10]);
    private string Terrain => Path.Combine(_root, "assets", "terrain", "t");
    private string Scenario => Path.Combine(_root, "scenarios", "s1");
    private string Overlay => Path.Combine(Scenario, OverlayCompiler.FolderName);
    private string Definition => Path.Combine(Terrain, "terrain.json");

    private static readonly bool HasNetconvert = Netconvert.Locate() is { Path: not null, Problem: null };

    private bool Setup()
    {
        if (!HasNetconvert) return false;
        Directory.CreateDirectory(Terrain); Directory.CreateDirectory(Scenario);
        File.WriteAllText(Definition, """{"schemaVersion":2,"name":"t","world":"w.geojson","roadNetworks":["roads.json"],"sumoNet":{"file":"t.net.xml","anchor":[-150,-200]}}""");
        File.WriteAllText(Path.Combine(Terrain, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[]}""");
        File.WriteAllText(Path.Combine(Terrain, "roads.json"), """
            {"$meta":{"docType":"Fdp.RoadNetwork","schemaVersion":1},
             "nodes":[{"id":0,"position":{"x":150,"y":200}},{"id":1,"position":{"x":190,"y":200}},{"id":2,"position":{"x":240,"y":200}},{"id":3,"position":{"x":300,"y":200}}],
             "segments":[
              {"id":0,"startNodeId":0,"endNodeId":1,"controlPoints":{"p0":{"x":150,"y":200},"t0":{"x":40,"y":0},"p1":{"x":190,"y":200},"t1":{"x":40,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1},
              {"id":1,"startNodeId":2,"endNodeId":3,"controlPoints":{"p0":{"x":240,"y":200},"t0":{"x":60,"y":0},"p1":{"x":300,"y":200},"t1":{"x":60,"y":0}},"speedLimit":14,"laneWidth":3.5,"laneCount":1}]}
            """);
        File.WriteAllText(Path.Combine(_root, "n.nod.xml"), """<nodes><node id="W0" x="150" y="200"/><node id="W1" x="190" y="200"/><node id="E0" x="240" y="200"/><node id="E1" x="300" y="200"/></nodes>""");
        File.WriteAllText(Path.Combine(_root, "e.edg.xml"), """<edges><edge id="w" from="W0" to="W1" numLanes="1" speed="14"/><edge id="w-r" from="W1" to="W0" numLanes="1" speed="14"/><edge id="e" from="E0" to="E1" numLanes="1" speed="14"/><edge id="e-r" from="E1" to="E0" numLanes="1" speed="14"/></edges>""");
        var loc = Netconvert.Locate();
        var (code, _, err) = Netconvert.Run(loc.Path!, new[] { "-n", Path.Combine(_root, "n.nod.xml"), "-e", Path.Combine(_root, "e.edg.xml"), "-o", Path.Combine(Terrain, "t.net.xml") }, null, Netconvert.SumoHomeOf(loc.Path!));
        Assert.True(code == 0, err);
        return true;
    }

    private static JsonObject Dom() => new()
    {
        ["header"] = new JsonObject { ["terrainName"] = "t" },
        ["entities"] = new JsonObject
        {
            [Key] = new JsonObject
            {
                ["TerrainFeature"] = new JsonObject { ["Kind"] = TerrainFeature.Bridge, ["Template"] = "", ["HeadingDeg"] = 0, ["Pad"] = 4, ["Seed"] = 0 },
                ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(192, 200, 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
                ["EditablePolyline"] = new JsonObject { ["Points"] = new JsonArray(new JsonObject { ["X"] = 0, ["Y"] = 0 }, new JsonObject { ["X"] = 46, ["Y"] = 0 }), ["Version"] = 0 },
            },
        },
    };

    private OverlayCompileResult Compile(IBridgeNet? net) => OverlayCompiler.Compile(Dom(), Scenario, n => n == "t" ? Definition : null, bakeNavtiles: false, bridgeNet: net);

    private static NetworkRouter Router(string netPath) => new(NetworkParser.ParseXml(File.ReadAllText(netPath)));

    /// <summary>
    /// A route from the west road to the east road does not exist in the base SUMO net, and does in the overlay's — over the bridge's edge. 🔴 Red-proof: build the overlay net with no bridge edges ⇒ no route.
    /// </summary>
    [Fact]
    public void ASumoRoute_BetweenTheBanks_UsesTheBridgeEdge_AfterTheCompile_AndDoesNotExistBefore()
    {
        if (!Setup()) return;
        Assert.Null(Router(Path.Combine(Terrain, "t.net.xml")).Route("w", "e"));
        var r = Compile(new BridgeNetBuilder());
        Assert.Equal(1, r.BridgeRoads);
        string net = Path.Combine(Overlay, "t.net.xml");
        Assert.True(File.Exists(net), "the overlay holds its own SUMO net");
        var route = Router(net).Route("w", "e");
        Assert.NotNull(route);
        Assert.Contains(BridgeNetBuilder.EdgeIds(Key).There, route!);
        var header = JsonNode.Parse(File.ReadAllText(Path.Combine(Overlay, TerrainOverlayHeader.FileName)))!;
        Assert.Equal("t.net.xml", (string)header["sumoNet"]!);
        var ids = header["bridges"]![0]!["sumoEdges"]!.AsArray().Select(n => (string)n!).ToArray();
        Assert.Equal(new[] { BridgeNetBuilder.EdgeIds(Key).There, BridgeNetBuilder.EdgeIds(Key).Back }, ids);
    }

    /// <summary>Every normal edge and lane id of the base net is in the overlay's net, with the same lane count (junction-internal ids are regenerated by netconvert — measured, §18k-1).</summary>
    [Fact]
    public void TheOverlayNet_KeepsEveryBaseNormalEdgeAndLaneId()
    {
        if (!Setup()) return;
        Compile(new BridgeNetBuilder());
        static Dictionary<string, string[]> Normal(string path)
        {
            var d = new Dictionary<string, string[]>();
            var doc = System.Xml.Linq.XDocument.Load(path);
            foreach (var e in doc.Descendants("edge"))
            {
                string id = (string)e.Attribute("id")!;
                if (id.StartsWith(':')) continue;
                d[id] = e.Elements("lane").Select(l => (string)l.Attribute("id")!).ToArray();
            }
            return d;
        }
        var b = Normal(Path.Combine(Terrain, "t.net.xml")); var o = Normal(Path.Combine(Overlay, "t.net.xml"));
        Assert.Equal(4, b.Count);
        foreach (var (id, lanes) in b) { Assert.True(o.ContainsKey(id), id); Assert.Equal(lanes, o[id]); }
        Assert.Equal(b.Count + 2, o.Count);
    }

    /// <summary>Two compiles are byte-identical — the net included (netconvert's generated-on comment is stripped) — and the hash covers the net.</summary>
    [Fact]
    public void TwoCompiles_AreByteIdentical_NetIncluded_AndTheHashCoversTheNet()
    {
        if (!Setup()) return;
        var a = Compile(new BridgeNetBuilder());
        byte[] n1 = File.ReadAllBytes(Path.Combine(Overlay, "t.net.xml")), j1 = File.ReadAllBytes(Path.Combine(Overlay, TerrainOverlayHeader.FileName));
        var b = Compile(new BridgeNetBuilder());
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.Equal(n1, File.ReadAllBytes(Path.Combine(Overlay, "t.net.xml")));
        Assert.Equal(j1, File.ReadAllBytes(Path.Combine(Overlay, TerrainOverlayHeader.FileName)));
        File.AppendAllText(Path.Combine(Overlay, "t.net.xml"), " ");
        Assert.NotEqual(a.ContentHash, TerrainContentHash.OfOverlay(Overlay));
    }

    /// <summary>Without netconvert the compile warns and skips the net: the vehicles' net still has the bridge, the header names no SUMO net.</summary>
    [Fact]
    public void WithoutANetconvert_TheNetIsSkipped_AndTheVehiclesNetStillGetsTheBridge()
    {
        if (!Setup()) return;
        var r = Compile(null);
        Assert.Equal(1, r.BridgeRoads);
        Assert.False(File.Exists(Path.Combine(Overlay, "t.net.xml")));
        Assert.True(File.Exists(Path.Combine(Overlay, TerrainOverlayHeader.RoadsFileName)));
        Assert.Null(JsonNode.Parse(File.ReadAllText(Path.Combine(Overlay, TerrainOverlayHeader.FileName)))!["sumoNet"]);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
