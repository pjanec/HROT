namespace Hrot.Terrain.Osm.Dem;

/// <summary>
/// ⭐ Copernicus DEM GLO-30 (30 m, global, free with attribution) as an <see cref="IDemSource"/> — Terrain From OSM §5.2, §14.5 E-6 / E-7.
/// One 1° cloud-optimised GeoTIFF per degree square; only the internal tiles the area touches are fetched (range requests). A missing tile is
/// open sea ⇒ 0 m. Sampling is bicubic (Catmull-Rom) between posts; a post past a file's edge is taken from the neighbouring file.
/// </summary>
public sealed class CopernicusDem : IDemSource
{
    public const string PublicBucket = "https://copernicus-dem-30m.s3.amazonaws.com/";
    private readonly IRangeSource _src;
    private readonly Dictionary<(int Lat, int Lon), CogReader?> _files = new();

    public CopernicusDem(IRangeSource src) => _src = src;

    /// <summary>The public bucket over HTTP, cached on disk.</summary>
    public static CopernicusDem Online(HttpClient http, string cacheFolder)
        => new(new HttpRangeSource(http, n => PublicBucket + Path.GetFileNameWithoutExtension(n) + "/" + n, cacheFolder));

    public double ResolutionMetres => 30;
    public string Name => "Copernicus DEM GLO-30";

    /// <summary>The file holding the degree square whose SOUTH-WEST corner is (lat, lon): <c>Copernicus_DSM_COG_10_N49_00_E015_00_DEM.tif</c>.</summary>
    public static string FileName(int lat, int lon)
        => $"Copernicus_DSM_COG_10_{(lat < 0 ? 'S' : 'N')}{Math.Abs(lat):00}_00_{(lon < 0 ? 'W' : 'E')}{Math.Abs(lon):000}_00_DEM.tif";

    public async Task PrepareAsync(GeoBox area, string cacheFolder, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        // a margin of ≈ 3 posts so bicubic samples at the box edge have their neighbours
        const double m = 0.003;
        double s = area.South - m, n = area.North + m, w = area.West - m, e = area.East + m;
        var squares = new List<(int, int)>();
        for (int lat = (int)Math.Floor(s); lat <= (int)Math.Floor(n); lat++)
            for (int lon = (int)Math.Floor(w); lon <= (int)Math.Floor(e); lon++) squares.Add((lat, lon));
        int done = 0;
        foreach (var (lat, lon) in squares)
        {
            cancellation.ThrowIfCancellationRequested();
            var file = await Open(lat, lon, cancellation);
            if (file != null)
            {
                var (c0, r0) = PixelOf(file, Math.Max(s, lat), Math.Max(w, lon));
                var (c1, r1) = PixelOf(file, Math.Min(n, lat + 1), Math.Min(e, lon + 1));
                await file.LoadWindowAsync(Math.Min(c0, c1) - 2, Math.Min(r0, r1) - 2, Math.Max(c0, c1) + 2, Math.Max(r0, r1) + 2, cancellation);
            }
            progress?.Report(new OsmProgress(OsmStage.Heights, ++done / (double)squares.Count, $"elevation {FileName(lat, lon)}"));
        }
    }

    private async Task<CogReader?> Open(int lat, int lon, CancellationToken ct)
    {
        if (_files.TryGetValue((lat, lon), out var f)) return f;
        f = await CogReader.OpenAsync(_src, FileName(lat, lon), ct);
        _files[(lat, lon)] = f;
        return f;
    }

    private static (int Col, int Row) PixelOf(CogReader f, double lat, double lon)
        => ((int)Math.Round((lon - f.OriginLon) / f.StepLon), (int)Math.Round((f.OriginLat - lat) / f.StepLat));

    public float HeightAt(double latDeg, double lonDeg)
    {
        var f = File(latDeg, lonDeg);
        if (f == null) return 0f;
        double fc = (lonDeg - f.OriginLon) / f.StepLon, fr = (f.OriginLat - latDeg) / f.StepLat;
        int c = (int)Math.Floor(fc), r = (int)Math.Floor(fr);
        double tx = fc - c, ty = fr - r;
        Span<double> col = stackalloc double[4];
        for (int j = -1; j <= 2; j++)
        {
            double p0 = Post(f, c - 1, r + j), p1 = Post(f, c, r + j), p2 = Post(f, c + 1, r + j), p3 = Post(f, c + 2, r + j);
            col[j + 1] = CatmullRom(p0, p1, p2, p3, tx);
        }
        return (float)CatmullRom(col[0], col[1], col[2], col[3], ty);
    }

    private CogReader? File(double lat, double lon)
        => _files.TryGetValue(((int)Math.Floor(lat), (int)Math.Floor(lon)), out var f) ? f : throw new InvalidOperationException(
            $"elevation at {lat:0.####}, {lon:0.####} was not prepared (PrepareAsync the area first)");

    // a post inside the file, else the nearest post of the file that holds that position (0 m when that is open sea)
    private double Post(CogReader f, int col, int row)
    {
        if (col >= 0 && row >= 0 && col < f.Width && row < f.Height) return Clean(f, f.Pixel(col, row));
        double lat = f.OriginLat - row * f.StepLat, lon = f.OriginLon + col * f.StepLon;
        if (!_files.TryGetValue(((int)Math.Floor(lat), (int)Math.Floor(lon)), out var g))
            return Clean(f, f.Pixel(Math.Clamp(col, 0, f.Width - 1), Math.Clamp(row, 0, f.Height - 1)));
        if (g == null) return 0;
        var (gc, gr) = PixelOf(g, lat, lon);
        return Clean(g, g.Pixel(Math.Clamp(gc, 0, g.Width - 1), Math.Clamp(gr, 0, g.Height - 1)));
    }

    private static double Clean(CogReader f, float v) => f.NoData is float nd && v == nd ? 0 : v;

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
        => p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
}
