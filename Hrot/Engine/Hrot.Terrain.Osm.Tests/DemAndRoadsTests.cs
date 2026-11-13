using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Fdp.Modules.Geographic.Transforms;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;
using Hrot.Terrain.Osm.Dem;
using Hrot.Terrain.Osm.Roads;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>CE-4374 — the elevation reader (a minimal COG reader + Copernicus GLO-30 naming, bicubic, sea, file edges) and the road builder
/// (netconvert with the explicit projection, F-7). Synthetic GeoTIFFs written by the test itself — no real data in git.</summary>
public sealed class DemAndRoadsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dem-tests-" + Guid.NewGuid().ToString("N"));
    public DemAndRoadsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // a plane in degrees: z = 100 + 400·(lat − 49) + 250·(lon − 15)
    private static float Plane(double lat, double lon) => (float)(100 + 400 * (lat - 49) + 250 * (lon - 15));

    /// <summary>Writes a 1° square as a tiled, deflated, predictor-3 float32 GeoTIFF (PixelIsArea), like Copernicus GLO-30, smaller.</summary>
    internal static void WriteCog(string path, int lat, int lon, int size, int tile, Func<double, double, float> z, bool pixelIsPoint = false)
    {
        double step = 1.0 / size;
        int across = (size + tile - 1) / tile;
        var tiles = new List<byte[]>();
        for (int ty = 0; ty < across; ty++)
            for (int tx = 0; tx < across; tx++)
            {
                var raw = new byte[tile * tile * 4];
                int rowBytes = tile * 4;
                var row = new byte[rowBytes];
                for (int y = 0; y < tile; y++)
                {
                    var vals = new byte[rowBytes];
                    for (int x = 0; x < tile; x++)
                    {
                        int c = tx * tile + x, r = ty * tile + y;
                        double clat = lat + 1 - (r + (pixelIsPoint ? 0 : 0.5)) * step, clon = lon + (c + (pixelIsPoint ? 0 : 0.5)) * step;
                        BinaryPrimitives.WriteSingleLittleEndian(vals.AsSpan(x * 4), z(clat, clon));
                    }
                    for (int x = 0; x < tile; x++) for (int k = 0; k < 4; k++) row[(3 - k) * tile + x] = vals[x * 4 + k];
                    for (int i = rowBytes - 1; i >= 1; i--) row[i] = unchecked((byte)(row[i] - row[i - 1]));
                    row.CopyTo(raw, y * rowBytes);
                }
                using var ms = new MemoryStream();
                using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);
                tiles.Add(ms.ToArray());
            }

        using var f = new MemoryStream();
        void U16(int v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); f.Write(b); }
        void U32(long v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)v); f.Write(b); }
        f.Write("II"u8); U16(42); U32(8);
        var tags = new List<(int Tag, int Type, int Count, byte[] Data)>();
        byte[] Short(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); return b; }
        byte[] Longs(IEnumerable<long> v) => v.SelectMany(x => { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)x); return b; }).ToArray();
        byte[] Doubles(params double[] v) => v.SelectMany(x => { var b = new byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(b, x); return b; }).ToArray();
        byte[] Shorts(params int[] v) => v.SelectMany(Short).ToArray();
        int n = tiles.Count;
        long[] offsets = new long[n];
        tags.Add((256, 3, 1, Short(size))); tags.Add((257, 3, 1, Short(size))); tags.Add((258, 3, 1, Short(32))); tags.Add((259, 3, 1, Short(8)));
        tags.Add((277, 3, 1, Short(1))); tags.Add((317, 3, 1, Short(3))); tags.Add((322, 3, 1, Short(tile))); tags.Add((323, 3, 1, Short(tile)));
        tags.Add((324, 4, n, Longs(offsets))); tags.Add((325, 4, n, Longs(tiles.Select(t => (long)t.Length)))); tags.Add((339, 3, 1, Short(3)));
        tags.Add((33550, 12, 3, Doubles(step, step, 0)));
        tags.Add((33922, 12, 6, Doubles(0, 0, 0, lon, lat + 1, 0)));
        tags.Add((34735, 3, 8, Shorts(1, 1, 0, 1, 1025, 0, 1, pixelIsPoint ? 2 : 1)));
        // layout: IFD at 8, its out-of-line values after it, then the tiles
        int ifdSize = 2 + tags.Count * 12 + 4;
        long extra = 8 + ifdSize;
        var outOfLine = new List<(int Index, long At)>();
        long cursor = extra;
        for (int i = 0; i < tags.Count; i++) if (tags[i].Data.Length > 4) { outOfLine.Add((i, cursor)); cursor += tags[i].Data.Length; }
        for (int i = 0; i < n; i++) { offsets[i] = cursor; cursor += tiles[i].Length; }
        tags[8] = (324, 4, n, Longs(offsets));
        U16(tags.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            U16(t.Tag); U16(t.Type); U32(t.Count);
            if (t.Data.Length <= 4) { var pad = new byte[4]; t.Data.CopyTo(pad, 0); f.Write(pad); }
            else U32(outOfLine.First(o => o.Index == i).At);
        }
        U32(0);
        foreach (var (i, _) in outOfLine) f.Write(tags[i].Data);
        foreach (var t in tiles) f.Write(t);
        File.WriteAllBytes(path, f.ToArray());
    }

    [Fact]
    public void FileNames_FollowCopernicus()
    {
        Assert.Equal("Copernicus_DSM_COG_10_N49_00_E015_00_DEM.tif", CopernicusDem.FileName(49, 15));
        Assert.Equal("Copernicus_DSM_COG_10_S23_00_W043_00_DEM.tif", CopernicusDem.FileName(-23, -43));
    }

    [Fact]
    public async Task TheCogReader_DecodesDeflatePredictor3_AndTheGeoreference()
    {
        WriteCog(Path.Combine(_dir, CopernicusDem.FileName(49, 15)), 49, 15, 96, 32, Plane);
        var r = await CogReader.OpenAsync(new FolderRangeSource(_dir), CopernicusDem.FileName(49, 15), default);
        Assert.NotNull(r);
        Assert.Equal(96, r!.Width); Assert.Equal(32, r.TileWidth);
        Assert.Equal(15 + 0.5 / 96, r.OriginLon, 9); Assert.Equal(50 - 0.5 / 96, r.OriginLat, 9);
        await r.LoadWindowAsync(0, 0, 95, 95, default);
        Assert.Equal(Plane(50 - 70.5 / 96, 15 + 40.5 / 96), r.Pixel(40, 70));
    }

    [Fact]
    public async Task HeightAt_IsBicubic_AndContinuousAcrossTheFileEdge()
    {
        WriteCog(Path.Combine(_dir, CopernicusDem.FileName(49, 15)), 49, 15, 96, 32, Plane);
        WriteCog(Path.Combine(_dir, CopernicusDem.FileName(49, 16)), 49, 16, 96, 32, Plane);
        var dem = new CopernicusDem(new FolderRangeSource(_dir));
        await dem.PrepareAsync(new GeoBox(49.40, 15.90, 49.45, 16.10), _dir, null, default);
        foreach (var (lat, lon) in new[] { (49.42, 15.93), (49.421, 15.999), (49.421, 16.0), (49.421, 16.001), (49.44, 16.07) })
            Assert.Equal(Plane(lat, lon), dem.HeightAt(lat, lon), 2);   // Catmull-Rom reproduces a plane exactly, also across the edge
        Assert.Equal(30, dem.ResolutionMetres);
    }

    [Fact]
    public async Task AMissingFile_IsOpenSea()
    {
        var dem = new CopernicusDem(new FolderRangeSource(_dir));
        await dem.PrepareAsync(new GeoBox(10.1, 10.1, 10.2, 10.2), _dir, null, default);
        Assert.Equal(0f, dem.HeightAt(10.15, 10.15));
    }

    [Fact]
    public async Task PixelIsPoint_IsReadAsPostCentres()
    {
        WriteCog(Path.Combine(_dir, CopernicusDem.FileName(49, 15)), 49, 15, 64, 32, Plane, pixelIsPoint: true);
        var r = await CogReader.OpenAsync(new FolderRangeSource(_dir), CopernicusDem.FileName(49, 15), default);
        Assert.Equal(15.0, r!.OriginLon, 9); Assert.Equal(50.0, r.OriginLat, 9);
    }

    [Fact]
    public void TheRoadBuilder_ProjectsExplicitly_SoNetEqualsLocal()
    {
        var b = new NetconvertRoadBuilder(-100, -100, 100, 100);
        var args = b.Arguments("a.osm", new UtmFrame(23, true, 701200, 7459300), "out.net.xml");
        int p = args.ToList().IndexOf("--proj");
        Assert.Equal("+proj=utm +zone=23 +south +datum=WGS84 +units=m +no_defs", args[p + 1]);
        Assert.Equal("-701200", args[args.ToList().IndexOf("--offset.x") + 1]);
        Assert.Equal("-7459300", args[args.ToList().IndexOf("--offset.y") + 1]);
        Assert.Contains("--offset.disable-normalization", args);
    }

    /// <summary>⭐ <c>CE-4409</c> — netconvert is started with <c>SUMO_HOME</c> set to the LOCATED install's home (🔒 user, 2026-11-10, Windows: <i>"environment variable SUMO_HOME is not set properly"</i>, exit 1):
    /// the home of an install laid out as SUMO ships it (<c>bin/</c> beside <c>data/</c> and <c>tools/</c>) is found from the executable, and the child process sees it.
    /// 🔴 Red-proof (compiles): <c>Run</c> not setting the variable ⇒ the child prints the parent's (empty) value ⇒ red.</summary>
    [Fact]
    public void TheChild_GetsSumoHome_OfTheLocatedInstall()
    {
        string home = Path.Combine(_dir, "sumo-install");
        Directory.CreateDirectory(Path.Combine(home, "bin")); Directory.CreateDirectory(Path.Combine(home, "data")); Directory.CreateDirectory(Path.Combine(home, "tools"));
        string exe = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "netconvert.exe" : "netconvert");
        File.WriteAllText(exe, "");
        Assert.Equal(home, NetconvertRoadBuilder.SumoHome(exe));

        var (code, stdout, _) = OperatingSystem.IsWindows()
            ? Netconvert.Run("cmd", new[] { "/c", "echo %SUMO_HOME%" }, null, home)
            : Netconvert.Run("/bin/sh", new[] { "-c", "printf %s \"$SUMO_HOME\"" }, null, home);
        Assert.Equal(0, code);
        Assert.Equal(home, stdout.Trim());
        Assert.Null(NetconvertRoadBuilder.SumoHome(Path.Combine(_dir, "nowhere", "netconvert")));   // no install layout ⇒ no override
    }

    /// <summary>F-7 — a known OSM node lands at its local metres (needs netconvert 1.20.0: scripts/install-sumo.*; skipped, reported, without it).</summary>
    [Fact]
    public async Task AKnownNode_LandsAtItsLocalMetres()
    {
        if (Netconvert.Locate().Problem != null) return;
        (double lat, double lon)[] pts = { (50.0275, 15.1960), (50.0275, 15.2000), (50.0300, 15.2000) };
        var (e0, n0) = Utm.FromGeodetic(pts[0].lat, pts[0].lon, 33, false);
        var frame = new UtmFrame(33, false, Math.Round(e0 / 100) * 100, Math.Round(n0 / 100) * 100);   // the frame rule (E-8)
        var osm = new System.Text.StringBuilder("<?xml version='1.0' encoding='UTF-8'?>\n<osm version='0.6' generator='test'>\n");
        for (int i = 0; i < pts.Length; i++)
            osm.Append(string.Create(CultureInfo.InvariantCulture, $"<node id='{i + 1}' lat='{pts[i].lat}' lon='{pts[i].lon}' version='1'/>\n"));
        osm.Append("<way id='10' version='1'><nd ref='1'/><nd ref='2'/><nd ref='3'/><tag k='highway' v='residential'/></way>\n</osm>\n");
        string osmFile = Path.Combine(_dir, "t.osm");
        File.WriteAllText(osmFile, osm.ToString());
        await new NetconvertRoadBuilder(-1000, -1000, 1000, 1000).BuildAsync(osmFile, frame, Path.Combine(_dir, "roads"), default);
        string net = File.ReadAllText(Path.Combine(_dir, "roads", "roads.net.xml"));
        Assert.DoesNotContain("<!-- generated on", net);
        // the middle node is a dead-end-free bend; the two ends become junctions — find the first end's junction
        var (e, n) = Utm.FromGeodetic(pts[0].lat, pts[0].lon, 33, false);
        double ex = e - frame.E0, ey = n - frame.N0;
        var doc = System.Xml.Linq.XDocument.Parse(net);
        var j = doc.Root!.Elements("junction").Where(x => (string?)x.Attribute("type") != "internal")
            .Select(x => (X: double.Parse((string)x.Attribute("x")!, CultureInfo.InvariantCulture), Y: double.Parse((string)x.Attribute("y")!, CultureInfo.InvariantCulture)))
            .OrderBy(q => Math.Pow(q.X - ex, 2) + Math.Pow(q.Y - ey, 2)).First();
        Assert.True(Math.Abs(j.X - ex) < 0.05 && Math.Abs(j.Y - ey) < 0.05, $"junction at ({j.X}, {j.Y}), expected ({ex:0.###}, {ey:0.###})");
    }

    /// <summary>
    /// ⭐ CE-6020 — a <c>highway=service</c> street (the streets between blocks of flats, yards, industry) is a road CARS drive. 📐 SUMO's OSM type map gives it
    /// <c>allow="delivery pedestrian bicycle"</c>: on Týn nad Vltavou 189 of 550 drivable ways became edges with no car lane, and the map drew no road among the
    /// buildings. 🔴 Red-proof: <see cref="NetconvertRoadBuilder.TypeOverrides"/> without the service type ⇒ the lane disallows <c>passenger</c> ⇒ red.
    /// </summary>
    [Fact]
    public async Task AServiceStreet_CarriesCars()
    {
        if (Netconvert.Locate().Problem != null) return;
        (double lat, double lon)[] pts = { (49.2230, 14.4200), (49.2230, 14.4230), (49.2245, 14.4230) };
        var (e0, n0) = Utm.FromGeodetic(pts[0].lat, pts[0].lon, 33, false);
        var frame = new UtmFrame(33, false, Math.Round(e0 / 100) * 100, Math.Round(n0 / 100) * 100);
        var osm = new System.Text.StringBuilder("<?xml version='1.0' encoding='UTF-8'?>\n<osm version='0.6' generator='test'>\n");
        for (int i = 0; i < pts.Length; i++)
            osm.Append(string.Create(CultureInfo.InvariantCulture, $"<node id='{i + 1}' lat='{pts[i].lat}' lon='{pts[i].lon}' version='1'/>\n"));
        osm.Append("<way id='10' version='1'><nd ref='1'/><nd ref='2'/><tag k='highway' v='residential'/></way>\n");
        osm.Append("<way id='11' version='1'><nd ref='2'/><nd ref='3'/><tag k='highway' v='service'/></way>\n</osm>\n");
        string osmFile = Path.Combine(_dir, "s.osm");
        File.WriteAllText(osmFile, osm.ToString());
        await new NetconvertRoadBuilder(-1000, -1000, 1000, 1000).BuildAsync(osmFile, frame, Path.Combine(_dir, "roads"), default);
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(_dir, "roads", "roads.net.xml"));
        var service = doc.Root!.Elements("edge").Where(x => (string?)x.Attribute("type") == "highway.service").ToList();
        Assert.NotEmpty(service);
        foreach (var lane in service.SelectMany(x => x.Elements("lane")))
        {
            string allow = (string?)lane.Attribute("allow") ?? "", disallow = (string?)lane.Attribute("disallow") ?? "";
            Assert.True((allow.Length == 0 || allow.Contains("passenger")) && !disallow.Contains("passenger"), $"service lane allow='{allow}' disallow='{disallow}'");
        }
        Assert.False(File.Exists(Path.Combine(_dir, "roads", "hrot-osm-types.typ.xml")), "the override file is a build input, not left in the roads folder");
    }

    /// <summary>
    /// ⭐ CE-6022 — a single Overpass answer writes the ways' nodes AFTER the ways (<c>out meta; &gt;; out meta qt;</c>); netconvert resolves points only from nodes it has
    /// already read, so the road vanished while the import succeeded (Štěchovice: 6 road edges of 2 554). 🔴 Red-proof: hand netconvert the file as given ⇒ no edge ⇒ red.
    /// </summary>
    [Fact]
    public async Task WaysBeforeTheirNodes_StillBecomeRoads()
    {
        if (Netconvert.Locate().Problem != null) return;
        (double lat, double lon)[] pts = { (49.8700, 14.4100), (49.8700, 14.4130), (49.8715, 14.4130) };
        var (e0, n0) = Utm.FromGeodetic(pts[0].lat, pts[0].lon, 33, false);
        var frame = new UtmFrame(33, false, Math.Round(e0 / 100) * 100, Math.Round(n0 / 100) * 100);
        var osm = new System.Text.StringBuilder("<?xml version='1.0' encoding='UTF-8'?>\n<osm version='0.6' generator='test'>\n");
        osm.Append("<way id='10' version='1'><nd ref='1'/><nd ref='2'/><nd ref='3'/><tag k='highway' v='residential'/></way>\n");   // the way first, as Overpass answers
        for (int i = 0; i < pts.Length; i++)
            osm.Append(string.Create(CultureInfo.InvariantCulture, $"<node id='{i + 1}' lat='{pts[i].lat}' lon='{pts[i].lon}' version='1'/>\n"));
        osm.Append("</osm>\n");
        string osmFile = Path.Combine(_dir, "late-nodes.osm");
        File.WriteAllText(osmFile, osm.ToString());
        await new NetconvertRoadBuilder(-1000, -1000, 1000, 1000).BuildAsync(osmFile, frame, Path.Combine(_dir, "roads"), default);
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(_dir, "roads", "roads.net.xml"));
        Assert.Contains(doc.Root!.Elements("edge"), x => (string?)x.Attribute("type") == "highway.residential");
        Assert.False(File.Exists(Path.Combine(_dir, "roads", "hrot-osm-nodes-first.osm")), "the reordered copy is a build input, not left in the roads folder");
    }

    /// <summary>
    /// ⭐ CE-6020 — opt-in HARNESS (not a gate): a real town through the SAME composition the editor's OSM wizard uses (<see cref="OsmPipelines.ForAreas"/>), from OSM files on disk.
    /// <c>HROT_OSM_RUN_DIR</c> holds the downloaded <c>*.osm</c> tiles (merged here by <see cref="OsmXmlMerge"/>) and a <c>box.txt</c> (<c>south west north east</c>); the input set is written to
    /// <c>HROT_OSM_RUN_OUT</c>. Raw OSM data never enters git — the files live wherever the caller put them.
    /// </summary>
    [Fact]
    public async Task Harness_ImportsAnAreaFromOsmFiles_ThroughTheWizardsPipeline()
    {
        string? dir = Environment.GetEnvironmentVariable("HROT_OSM_RUN_DIR"), outDir = Environment.GetEnvironmentVariable("HROT_OSM_RUN_OUT");
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(outDir)) return;
        var parts = Directory.GetFiles(dir, "*.osm").Where(f => !f.EndsWith("merged.osm", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        string merged = Path.Combine(dir, "merged.osm");
        OsmXmlMerge.Merge(parts, merged);
        var b = File.ReadAllText(Path.Combine(dir, "box.txt")).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("terrain-import-test/1.0");
        var pipeline = OsmPipelines.ForAreas(new FileOsmSource(merged), CopernicusDem.Online(http, Path.Combine(dir, "dem-tiles")), new DownloadCache(Path.Combine(dir, "cache")));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        await pipeline.WriteInputSetAsync(new GeoBox(b[0], b[1], b[2], b[3]), new OsmImportOptions { Name = Path.GetFileName(outDir) }, outDir,
            new Warnings(), default);
        Assert.True(File.Exists(Path.Combine(outDir, "roads", "roads.net.xml")), "no road net written");
    }

    private sealed class Warnings : IProgress<OsmProgress> { public void Report(OsmProgress p) { if (p.Warning != null) Console.WriteLine("WARNING " + p.Warning); } }

    /// <summary>Opt-in live check of the real public bucket (HROT_LIVE_DEM=1): Sněžka, 1 603 m, read from GLO-30 (a surface model ⇒ ± a few m).</summary>
    [Fact]
    public async Task Live_TheRealBucket_ReadsAKnownSummit()
    {
        if (Environment.GetEnvironmentVariable("HROT_LIVE_DEM") != "1") return;
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HROT-terrain-test/1.0");
        var dem = CopernicusDem.Online(http, Path.Combine(_dir, "cache"));
        await dem.PrepareAsync(new GeoBox(50.730, 15.730, 50.742, 15.750), _dir, null, default);
        float z = dem.HeightAt(50.7360, 15.7399);
        Assert.InRange(z, 1570f, 1620f);
    }
}
