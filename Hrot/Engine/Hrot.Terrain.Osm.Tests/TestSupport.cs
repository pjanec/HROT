using Clipper2Lib;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>A test elevation source: a function of lat / lon, counting its calls.</summary>
public sealed class FuncDem : IDemSource
{
    private readonly Func<double, double, double> _f;
    public int PrepareCalls, HeightCalls;
    public Action<int>? OnHeight;
    public Func<CancellationToken, Task>? OnPrepare;
    public FuncDem(Func<double, double, double> f) => _f = f;
    public double ResolutionMetres => 30;
    public string Name => "Test DEM";
    public async Task PrepareAsync(GeoBox area, string cacheFolder, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        PrepareCalls++;
        if (OnPrepare != null) await OnPrepare(cancellation);
    }
    public float HeightAt(double latDeg, double lonDeg) { HeightCalls++; OnHeight?.Invoke(HeightCalls); return (float)_f(latDeg, lonDeg); }

    /// <summary>z = 100 + 10 000 × (lon − 15°): a 13.7 % slope rising to the east, constant northwards.</summary>
    public static FuncDem EastSlope() => new((lat, lon) => 100 + (lon - 15.0) * 10000.0);
    public static FuncDem Flat() => new((_, _) => 50.0);
}

public sealed class TestRoads : IRoadBuilder
{
    public int Calls;
    public Task BuildAsync(string osmFile, UtmFrame frame, string roadsFolder, CancellationToken cancellation)
    {
        Calls++;
        Directory.CreateDirectory(roadsFolder);
        File.WriteAllText(Path.Combine(roadsFolder, "roads.net.xml"), "<net version=\"1.20\"/>\n");
        return Task.CompletedTask;
    }
}

public sealed class CountingSource : IOsmSource
{
    private readonly IOsmSource _inner;
    public int Fetches;
    public CountingSource(IOsmSource inner) => _inner = inner;
    public string Name => _inner.Name;
    public Task FetchAsync(GeoBox box, string dest, IProgress<OsmProgress>? p, CancellationToken ct) { Fetches++; return _inner.FetchAsync(box, dest, p, ct); }
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-osm-tests-" + Guid.NewGuid().ToString("N")[..10]);
    public TempDir() => Directory.CreateDirectory(Path);
    public string Combine(string p) => System.IO.Path.Combine(Path, p);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

/// <summary>The synthetic town (Fixtures/synthetic-town.osm) and everything derived from it once.</summary>
public static class Fx
{
    /// <summary>The box the fixture was designed for: ±301 m × ±251 m around (zone 33 N, e0 500000, n0 5430000).</summary>
    public static readonly GeoBox Box = new(49.02062887887906, 14.995882860179693, 49.02514466038413, 15.004117139820307);
    public static string FixturePath => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-town.osm");
    public static readonly AreaBounds Bounds = AreaFrame.FromBox(Box);
    public static OsmImportOptions Options { get; } = new() { Name = "fixture-town" };
    public static readonly Lazy<OsmModel> Model = new(() => OsmXmlReader.Read(FixturePath, Bounds.Frame));
    public static readonly Lazy<BuildingRulesResult> Buildings = new(() => BuildingRules.Apply(Model.Value, Options, Bounds));

    // way / relation ids of the fixture (see the generator's order)
    public const string H1 = "w5006", H2 = "w5007", H3 = "w5008", H4 = "w5009";
    public const string Courtyard = "r9001", Garage = "w5012", Shed = "w5013", SmallYes = "w5014", Ruins = "w5015", Greenhouse = "w5017",
        Church = "w5018", Levels3 = "w5019", Commercial = "w5021", PlainYes = "w5022";

    public static BuildingDraft B(string id) => Buildings.Value.Buildings.Single(b => b.Id == id);
    public static Building Finished(string id, Func<double, double, double>? ground = null) => BuildingRules.Finish(B(id), ground);

    public static OsmTerrainPipeline Pipeline(FuncDem dem, TestRoads roads, DownloadCache cache, OsmBudget? budget = null, IOsmSource? source = null) =>
        new(source ?? new FileOsmSource(FixturePath), dem, roads, cache, budget);
}

/// <summary>Builds a tiny <see cref="OsmModel"/> in local metres without XML — for single-rule rails.</summary>
public sealed class ModelBuilder
{
    public OsmModel Model { get; } = new();
    private long _node = 1, _way = 1000;
    public static readonly AreaBounds Bounds = new(new UtmFrame(33, false, 500000, 5430000), -300, -300, 300, 300);

    public long Node(double x, double y, params (string, string)[] tags)
    {
        long id = _node++;
        Model.Nodes[id] = (x, y);
        if (tags.Length > 0) Model.NodeTags[id] = tags.ToDictionary(t => t.Item1, t => t.Item2);
        return id;
    }

    public long Way(IEnumerable<long> nodeIds, params (string, string)[] tags)
    {
        long id = _way++;
        var w = new OsmWay(id, nodeIds.ToArray(), tags.Length == 0 ? OsmTags.None : tags.ToDictionary(t => t.Item1, t => t.Item2));
        Model.Ways.Add(w); Model.WayById[id] = w;
        return id;
    }

    public long Line(double[] xy, params (string, string)[] tags) => Way(Enumerable.Range(0, xy.Length / 2).Select(i => Node(xy[2 * i], xy[2 * i + 1])), tags);

    /// <summary>An axis-aligned closed rectangle way with fresh nodes.</summary>
    public long Rect(double x0, double y0, double x1, double y1, params (string, string)[] tags)
    {
        var ids = new[] { Node(x0, y0), Node(x1, y0), Node(x1, y1), Node(x0, y1) };
        return Way(ids.Append(ids[0]), tags);
    }

    public long Road(double x0, double y0, double x1, double y1, params (string, string)[] extra) =>
        Line(new[] { x0, y0, x1, y1 }, new[] { ("highway", "residential") }.Concat(extra).ToArray());
}
