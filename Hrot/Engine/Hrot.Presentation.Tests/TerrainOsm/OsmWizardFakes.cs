using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;

namespace Hrot.Presentation.Tests.TerrainOsm;

/// <summary>
/// ⭐ CE-4375 W-2 — the mockup's fake place (<c>scripts/gen-terrain-osm-wizard-mockup.py</c> <c>world()</c>), ported: a village street grid, a main road, a motorway, a river, woods,
/// scrub, a terrace sharing walls, garages behind houses. ⭐ Kept in MOCKUP CANVAS PIXELS (830 × 476, y down) exactly as the generator draws it, and mapped to local metres at
/// <see cref="MetresPerPixel"/> — so the rails' frames and the mockup show the same place. ⚠ The jitter is <see cref="Random"/>(3), not Python's: same shape, other wobble.
/// </summary>
public static class OsmFakeWorld
{
    public const double CanvasW = 830, CanvasH = 476;
    /// <summary>The mockup's box: canvas px (150, 140) – (600, 470) = 4.8 × 3.5 km (the generator's <c>bx0 … by1</c>).</summary>
    public const double BoxX0 = 150, BoxY0 = 140, BoxX1 = 600, BoxY1 = 470;
    public const double MetresPerPixel = 4800.0 / (BoxX1 - BoxX0);
    public const double WidthM = 4800, HeightM = 3500;
    /// <summary>Kolín, Central Bohemia — the mockup's search result.</summary>
    public const double Lat = 50.0270, Lon = 15.2003;

    public static readonly GeoBox Box = WebMercator.BoxAround(Lat, Lon, WidthM, HeightM);

    public static readonly List<(string Class, Vector2[] Px)> Roads = new();
    public static readonly List<(Vector2[] Px, int Storeys, bool Enterable)> Buildings = new();
    public static readonly List<Vector2[]> Woods = new(), Scrub = new(), Walls = new();
    public static readonly Vector2[] River;

    static OsmFakeWorld()
    {
        var r = new Random(3);
        static Vector2 P(double x, double y) => new((float)x, (float)y);
        Roads.Add(("primary", new[] { P(-5, 300), P(260, 285), P(520, 250), P(CanvasW + 5, 210) }));
        Roads.Add(("motorway", new[] { P(600, -5), P(640, 200), P(700, CanvasH + 5) }));
        Roads.Add(("secondary", new[] { P(330, -5), P(350, 270), P(380, CanvasH + 5) }));
        Roads.Add(("track", new[] { P(40, 60), P(200, 120), P(300, 110) }));
        Roads.Add(("rail", new[] { P(-5, 440), P(400, 420), P(CanvasW + 5, 380) }));
        for (int i = 0; i < 5; i++) { double y = 180 + i * 34; Roads.Add(("residential", new[] { P(180, y), P(520, y - 18) })); }
        for (int i = 0; i < 4; i++) { double x = 200 + i * 90; Roads.Add(("residential", new[] { P(x, 170), P(x + 10, 340) })); }
        for (int gx = 0; gx < 4; gx++)
            for (int gy = 0; gy < 4; gy++)
            {
                double x0 = 208 + gx * 90, y0 = 186 + gy * 34;
                if (gy == 1 && gx == 1)   // a terrace row sharing walls
                {
                    for (int k = 0; k < 5; k++) Buildings.Add((Rect(x0 + k * 13, y0, 13, 11), 2, true));
                    continue;
                }
                for (int k = 0; k < 3; k++)
                {
                    double w = 10 + r.NextDouble() * 8, h = 8 + r.NextDouble() * 5;
                    double bx = x0 + k * 25 + r.NextDouble() * 4;
                    int[] pick = (gx, gy) != (2, 2) ? new[] { 1, 2, 2, 2, 3, 3, 4 } : new[] { 4, 5, 6 };
                    Buildings.Add((Rect(bx, y0, w, h), pick[r.Next(pick.Length)], true));
                    if (r.NextDouble() < 0.45) Buildings.Add((Rect(bx + 2, y0 + h + 3, 6, 5), 1, false));   // a garage behind it — solid
                }
            }
        Woods.Add(new[] { P(20, 20), P(170, 10), P(250, 70), P(210, 150), P(60, 160), P(10, 100) });
        Woods.Add(new[] { P(660, 280), P(810, 300), P(820, 470), P(700, 490), P(640, 400) });
        River = new[] { P(520, CanvasH + 5), P(560, 420), P(590, 330), P(570, 200), P(620, 90), P(700, -5) };
        Scrub.Add(new[] { P(460, 30), P(540, 40), P(520, 110), P(450, 90) });
        Walls.Add(new[] { P(190, 172), P(540, 158) });
        Walls.Add(new[] { P(200, 345), P(540, 330) });
    }

    private static Vector2[] Rect(double x, double y, double w, double h)
        => new[] { new Vector2((float)x, (float)y), new Vector2((float)(x + w), (float)y), new Vector2((float)(x + w), (float)(y + h)), new Vector2((float)x, (float)(y + h)) };

    /// <summary>Mockup canvas px → local metres (x east, y north; origin the box's south-west corner).</summary>
    public static Vector2 ToLocal(Vector2 px) => new((float)((px.X - BoxX0) * MetresPerPixel), (float)((BoxY1 - px.Y) * MetresPerPixel));
    /// <summary>Local metres → mockup canvas px.</summary>
    public static Vector2 ToPx(double x, double y) => new((float)(BoxX0 + x / MetresPerPixel), (float)(BoxY1 - y / MetresPerPixel));

    public static Vector2[] ToLocal(Vector2[] px) { var o = new Vector2[px.Length]; for (int i = 0; i < px.Length; i++) o[i] = ToLocal(px[i]); return o; }

    /// <summary>The frame: zone 33 N, local origin = the box's south-west corner in UTM.</summary>
    public static UtmFrame Frame
    {
        get
        {
            int zone = WebMercator.UtmZone(Lon);
            var (e, n) = Fdp.Modules.Geographic.Transforms.Utm.FromGeodetic(Box.South, Box.West, zone, false);
            return new UtmFrame(zone, false, e, n);
        }
    }

    /// <summary>The preview of the fake place (honest counts: what the lists hold).</summary>
    public static OsmPreview Preview(BudgetVerdict verdict = BudgetVerdict.Ok, string? reason = null, IReadOnlyList<string>? warnings = null, OsmCounts? counts = null)
    {
        var roads = new List<PreviewRoad>();
        double roadM = 0;
        foreach (var (cls, px) in Roads)
        {
            var pts = ToLocal(px);
            roads.Add(new PreviewRoad(cls, pts));
            for (int i = 1; i < pts.Length; i++) roadM += Vector2.Distance(pts[i - 1], pts[i]);
        }
        var blds = new List<PreviewBuilding>();
        int doors = 0, windows = 0;
        for (int i = 0; i < Buildings.Count; i++)
        {
            var (px, st, ent) = Buildings[i];
            blds.Add(new PreviewBuilding("w" + (1000 + i), ToLocal(px), st, ent));
            if (ent) { doors += 2; windows += 6 * st; }
        }
        var areas = new List<PreviewArea>();
        double woodsM2 = 0;
        foreach (var w in Woods) { var ring = ToLocal(w); areas.Add(new PreviewArea("woods", ring)); woodsM2 += Area(ring); }
        foreach (var w in Scrub) areas.Add(new PreviewArea("scrub", ToLocal(w)));
        areas.Add(new PreviewArea("water", RiverRing()));
        var lines = new List<PreviewLine>();
        foreach (var w in Walls) lines.Add(new PreviewLine("wall", ToLocal(w)));
        return new OsmPreview
        {
            Frame = Frame,
            Bounds = new[] { 0.0, 0.0, WidthM, HeightM },
            Counts = counts ?? new OsmCounts(blds.Count, doors, windows, 0, woodsM2, Math.Round(woodsM2 / 100.0), roadM / 1000.0, Walls.Count, 1),
            Verdict = verdict,
            VerdictReason = reason,
            Warnings = warnings ?? new[] { "512 buildings: storeys from their type", "3 courtyards split into parts", "9 shapes dropped (< 12 m² or too thin)", "2 rivers widened from lines (10 m)" },
            Roads = roads, Buildings = blds, Areas = areas, Lines = lines,
            Attribution = "© OpenStreetMap contributors · Copernicus DEM GLO-30",
        };
    }

    /// <summary>The river as a 10 m-wide ring (the pipeline widens river lines — the mockup's note).</summary>
    private static Vector2[] RiverRing()
    {
        var c = ToLocal(River);
        var left = new Vector2[c.Length]; var right = new Vector2[c.Length];
        for (int i = 0; i < c.Length; i++)
        {
            var d = Vector2.Normalize(c[Math.Min(i + 1, c.Length - 1)] - c[Math.Max(i - 1, 0)]);
            var n = new Vector2(-d.Y, d.X) * 25f;
            left[i] = c[i] + n; right[c.Length - 1 - i] = c[i] - n;
        }
        var ring = new Vector2[c.Length * 2];
        left.CopyTo(ring, 0); right.CopyTo(ring, c.Length);
        return ring;
    }

    private static double Area(Vector2[] ring)
    {
        double a = 0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) a += (double)ring[j].X * ring[i].Y - (double)ring[i].X * ring[j].Y;
        return Math.Abs(a) * 0.5;
    }

    // ── point tests for the tile painter (mockup px) ──
    internal static bool Inside(Vector2[] poly, Vector2 p)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    internal static float DistanceToLine(Vector2[] line, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 1; i < line.Length; i++)
        {
            var a = line[i - 1]; var ab = line[i] - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            best = Math.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }
}

/// <summary>⭐ W-2 — the fake pipeline: <see cref="IOsmTerrainPipeline"/> over <see cref="OsmFakeWorld"/>. Each call can be HELD (a rail shows the running state) and released.</summary>
public sealed class FakeOsmPipeline : IOsmTerrainPipeline
{
    public double MaxSideMetres { get; init; } = 6000;
    public BudgetVerdict Verdict { get; set; } = BudgetVerdict.Ok;
    public string? VerdictReason { get; set; }
    public string? FailPreviewWith { get; set; }
    /// <summary>When set, PreviewAsync / WriteInputSetAsync report this progress and wait for <see cref="Release"/>.</summary>
    public bool Hold { get; set; }
    public OsmProgress HeldAt { get; set; } = new(OsmStage.Buildings, 0.62, "Buildings — 880 of 1 412");
    public int PreviewCalls, WriteCalls;
    public CancellationToken LastToken;
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() { var r = _release; _release = new(TaskCreationOptions.RunContinuationsAsynchronously); r.TrySetResult(); }

    public (double WidthM, double HeightM, string? Refusal) Measure(GeoBox area)
    {
        var (w, h) = WebMercator.SizeOf(area);
        if (area.North <= area.South || area.East <= area.West) return (w, h, "the box is inverted");
        return (w, h, w > MaxSideMetres || h > MaxSideMetres ? $"the box is larger than {MaxSideMetres / 1000:0} × {MaxSideMetres / 1000:0} km" : null);
    }

    public async Task<OsmPreview> PreviewAsync(GeoBox area, OsmImportOptions options, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        Interlocked.Increment(ref PreviewCalls);
        LastToken = cancellation;
        progress?.Report(new OsmProgress(OsmStage.Fetch, 0.1, "Downloading the map data"));
        if (Hold) { progress?.Report(HeldAt); await _release.Task.WaitAsync(cancellation).ConfigureAwait(false); }
        cancellation.ThrowIfCancellationRequested();
        if (FailPreviewWith is { } why) throw new InvalidOperationException(why);
        return OsmFakeWorld.Preview(Verdict, VerdictReason);
    }

    public async Task<string> WriteInputSetAsync(GeoBox area, OsmImportOptions options, string outputFolder, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        Interlocked.Increment(ref WriteCalls);
        LastToken = cancellation;
        foreach (OsmStage st in Enum.GetValues<OsmStage>())
        {
            if (Hold && st == HeldAt.Stage) { progress?.Report(HeldAt); await _release.Task.WaitAsync(cancellation).ConfigureAwait(false); }
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(new OsmProgress(st, 1.0, st.ToString()));
        }
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(Path.Combine(outputFolder, "manifest.json"), "{}");
        return outputFolder;
    }
}

/// <summary>⭐ W-2 — the fake search: the mockup's three results for "Kolín".</summary>
public sealed class FakePlaceSearch : IPlaceSearch
{
    public string? LastQuery;
    public Task<IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)>> SearchAsync(string query, CancellationToken cancellation)
    {
        LastQuery = query;
        IReadOnlyList<(string, double, double, GeoBox?)> r = query.Trim().Length == 0 ? Array.Empty<(string, double, double, GeoBox?)>() : new (string, double, double, GeoBox?)[]
        {
            ("Kolín, town, Central Bohemia, Czechia", OsmFakeWorld.Lat, OsmFakeWorld.Lon, OsmFakeWorld.Box),
            ("Kolín district, district, Czechia", 50.0, 15.15, WebMercator.BoxAround(50.0, 15.15, 30000, 25000)),
            ("Nový Kolín, village", 50.06, 15.22, null),
        };
        return Task.FromResult(r);
    }
}

/// <summary>
/// ⭐ W-2 — fake XYZ tiles: each 256 px tile PAINTED from <see cref="OsmFakeWorld"/> in the mockup's light OSM-like colours (the generator's <c>tiles()</c>) and encoded as a real PNG,
/// so the window's raylib upload path (<c>LoadImageFromMemory</c>) is exercised. Outside the place: the light ground.
/// </summary>
public sealed class FakeTileSource : ITileSource
{
    public string Attribution => "© OpenStreetMap contributors";
    public int Calls;

    public Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellation)
    {
        Interlocked.Increment(ref Calls);
        return Task.Run<byte[]?>(() => Png.Encode(Paint(zoom, x, y), 256, 256), cancellation);
    }

    private static byte[] Paint(int z, int tx, int ty)
    {
        var rgb = new byte[256 * 256 * 3];
        var frame = OsmFakeWorld.Frame;
        for (int py = 0; py < 256; py++)
            for (int px = 0; px < 256; px++)
            {
                var (lat, lon) = WebMercator.ToGeo(tx * 256 + px + 0.5, ty * 256 + py + 0.5, z);
                var (e, n) = Fdp.Modules.Geographic.Transforms.Utm.FromGeodetic(lat, lon, frame.Zone, frame.South);
                var p = OsmFakeWorld.ToPx(e - frame.E0, n - frame.N0);
                uint c = Colour(p, (float)(1.0 / (OsmFakeWorld.MetresPerPixel / Metres(lat, z))));
                int o = (py * 256 + px) * 3;
                rgb[o] = (byte)(c >> 16); rgb[o + 1] = (byte)(c >> 8); rgb[o + 2] = (byte)c;
            }
        return rgb;
    }

    /// <summary>Ground metres per tile pixel at this latitude and zoom.</summary>
    private static double Metres(double lat, int z) => 156543.03392 * Math.Cos(lat * Math.PI / 180) / Math.Pow(2, z);

    /// <summary>The mockup's tile colours at mockup px <paramref name="p"/>; <paramref name="pxPerTilePx"/> scales the drawn widths.</summary>
    private static uint Colour(Vector2 p, float pxPerTilePx)
    {
        const uint Ground = 0xF2EFE9;
        if (p.X < -20 || p.Y < -20 || p.X > OsmFakeWorld.CanvasW + 20 || p.Y > OsmFakeWorld.CanvasH + 20) return Ground;
        float tol = Math.Max(pxPerTilePx, 0.6f);
        foreach (var (px, _, _) in OsmFakeWorld.Buildings) if (OsmFakeWorld.Inside(px, p)) return 0xD9D0C9;
        foreach (var (cls, pts) in OsmFakeWorld.Roads)
        {
            float w = cls switch { "motorway" or "trunk" => 5f, "primary" => 4f, "secondary" or "tertiary" => 3.2f, "track" => 1.2f, "rail" => 1.4f, _ => 2.2f } * 1.8f * 0.5f;
            float d = OsmFakeWorld.DistanceToLine(pts, p);
            if (d <= w) return cls switch { "motorway" => 0xE892A2, "primary" => 0xFCD6A4, "secondary" => 0xF7FABF, "rail" => 0x999999, _ => 0xFFFFFF };
            if (d <= w + 0.75f + tol * 0.3f) return 0xC8C0B8;
        }
        if (OsmFakeWorld.DistanceToLine(OsmFakeWorld.River, p) <= 4.5f) return 0xAAD3DF;
        foreach (var wd in OsmFakeWorld.Woods) if (OsmFakeWorld.Inside(wd, p)) return 0xADD19E;
        return Ground;
    }
}

/// <summary>A minimal PNG encoder (RGB, 8-bit, zlib via <see cref="ZLibStream"/>) — enough for the fake tiles.</summary>
internal static class Png
{
    public static byte[] Encode(byte[] rgb, int w, int h)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) raw.WriteTo(zs);
        using var o = new MemoryStream();
        o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(o, "IHDR", ihdr); Chunk(o, "IDAT", z.ToArray()); Chunk(o, "IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    private static void Be(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, (uint)data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4]; Be(crc, 0, Crc(td)); s.Write(crc);
    }

    private static readonly uint[] Table = MakeTable();
    private static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }
    private static uint Crc(byte[] b) { uint c = 0xFFFFFFFF; foreach (var x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8); return c ^ 0xFFFFFFFF; }
}

/// <summary>⭐ W-2 — a fake importer: records the call; "imports" by creating the terrain folder.</summary>
public sealed class FakeTerrainImporter : Hrot.Terrain.InputSet.ITerrainImporter
{
    public string Root = "";
    public readonly Dictionary<string, string[]> Users = new();
    public int Calls;
    public Task<string> ImportAsync(string inputSet, string terrainName, bool replace, IProgress<Hrot.Terrain.InputSet.ImportProgress>? progress, CancellationToken cancellation)
    {
        Calls++;
        var dir = Path.Combine(Root, terrainName);
        Directory.CreateDirectory(dir);
        return Task.FromResult(dir);
    }
    public IReadOnlyList<string> ScenariosUsing(string terrainName) => Users.TryGetValue(terrainName, out var u) ? u : Array.Empty<string>();
}
