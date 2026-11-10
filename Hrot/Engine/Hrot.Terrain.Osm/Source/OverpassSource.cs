using System.Globalization;
using System.Net;
using System.Text;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ E-1: ONE Overpass query for the box (+ margin) returning OSM XML with metadata (<c>out meta; &gt;; out meta qt;</c>) — the SAME file later
/// goes to netconvert. Read-only query service for OSM DATA; behind <see cref="IOsmSource"/> so another source can be added.
/// </summary>
public sealed class OverpassSource : IOsmSource
{
    public const string DefaultUrl = "https://overpass-api.de/api/interpreter";
    /// <summary>⭐ CE-4384 ① — how many times a box may be split into four (2 ⇒ at most 16 parts).</summary>
    public const int DefaultMaxSplitDepth = 2;
    private readonly HttpRetry _http;
    private readonly string _url, _service;
    private readonly int _maxSplitDepth;

    public OverpassSource(HttpClient http, string url = DefaultUrl, Func<TimeSpan, CancellationToken, Task>? delay = null, int maxSplitDepth = DefaultMaxSplitDepth)
    {
        _url = url;
        _service = "Overpass (" + new Uri(url).Host + ")";
        _http = new HttpRetry(http, _service, 3, delay);
        _maxSplitDepth = Math.Max(0, maxSplitDepth);
    }

    public string Name => "Overpass " + _url;

    public static string BuildQuery(GeoBox b)
    {
        string N(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("[out:xml][timeout:300][bbox:").Append(N(b.South)).Append(',').Append(N(b.West)).Append(',').Append(N(b.North)).Append(',').Append(N(b.East)).Append("];\n(\n");
        foreach (var key in new[] { "highway", "railway", "building", "building:part", "entrance", "landuse", "natural", "barrier", "waterway" })
            sb.Append("  nwr[\"").Append(key).Append("\"];\n");
        sb.Append(");\nout meta;\n>;\nout meta qt;\n");
        return sb.ToString();
    }

    /// <summary>
    /// ⭐ CE-4384 (OSM §7) — the box in ONE query; when Overpass cannot answer it (a 504 after the retries, or a 200 whose <c>&lt;remark&gt;</c> says it ran out of memory or
    /// time), the box is split into four, each fetched the same way (up to <see cref="DefaultMaxSplitDepth"/> times), and the parts merged by OSM id. ⭐ Any other
    /// <c>runtime error</c> remark is a FAILURE naming the service — ⛔ never read as an empty area.
    /// </summary>
    public async Task FetchAsync(GeoBox boxWithMargin, string destinationFile, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        progress?.Report(new OsmProgress(OsmStage.Fetch, 0, "Asking Overpass for the map data…"));
        int parts = await FetchPartAsync(boxWithMargin, destinationFile, 0, progress, cancellation).ConfigureAwait(false);
        progress?.Report(new OsmProgress(OsmStage.Fetch, 1, parts == 1 ? "Map data downloaded" : $"Map data downloaded in {parts} parts"));
    }

    private async Task<int> FetchPartAsync(GeoBox box, string file, int depth, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        string? tooLarge;
        try
        {
            await DownloadAsync(box, file, cancellation).ConfigureAwait(false);
            tooLarge = RemarkOf(file) is { } remark ? (IsTooLarge(remark) ? remark : throw new OsmServiceException(_service, $"{_service} answered with an error: {remark} — {HttpRetry.RetryHint(null)}")) : null;
        }
        catch (OsmServiceException e) when (e.Status == HttpStatusCode.GatewayTimeout && depth < _maxSplitDepth) { tooLarge = e.Message; }
        if (tooLarge == null) return 1;
        if (depth >= _maxSplitDepth)
            throw new OsmServiceException(_service, $"{_service} could not answer even a {Math.Pow(2, depth):0}×{Math.Pow(2, depth):0} part of the area: {tooLarge} — pick a smaller box, or {HttpRetry.RetryHint(null)}");

        progress?.Report(new OsmProgress(OsmStage.Fetch, 0, $"The area is too large for one Overpass answer — fetching it in {4 * (int)Math.Pow(4, depth)} parts…"));
        double midLat = (box.South + box.North) * 0.5, midLon = (box.West + box.East) * 0.5;
        var quads = new[] { new GeoBox(box.South, box.West, midLat, midLon), new GeoBox(box.South, midLon, midLat, box.East), new GeoBox(midLat, box.West, box.North, midLon), new GeoBox(midLat, midLon, box.North, box.East) };
        var files = new string[4];
        int count = 0;
        try
        {
            for (int i = 0; i < 4; i++)
            {
                files[i] = file + ".part" + depth + i;
                count += await FetchPartAsync(quads[i], files[i], depth + 1, progress, cancellation).ConfigureAwait(false);
            }
            OsmXmlMerge.Merge(files, file);
        }
        finally { foreach (var f in files) if (f != null && File.Exists(f)) File.Delete(f); }
        return count;
    }

    private async Task DownloadAsync(GeoBox box, string destinationFile, CancellationToken cancellation)
    {
        string query = BuildQuery(box);
        using var resp = await _http.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) })
        }, cancellation).ConfigureAwait(false);
        await using (var body = await resp.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
        await using (var fs = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            await body.CopyToAsync(fs, cancellation).ConfigureAwait(false);
    }

    /// <summary>The <c>runtime error</c> remark Overpass puts at the END of an answer it could not finish (it still answers 200), or null.</summary>
    public static string? RemarkOf(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        long start = Math.Max(0, fs.Length - 65536);
        fs.Seek(start, SeekOrigin.Begin);
        var tail = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
        int i = tail.LastIndexOf("<remark>", StringComparison.Ordinal);
        if (i < 0) return null;
        int j = tail.IndexOf("</remark>", i, StringComparison.Ordinal);
        string text = (j > i ? tail[(i + 8)..j] : tail[(i + 8)..]).Trim();
        return text.Contains("runtime error", StringComparison.OrdinalIgnoreCase) || text.Contains("error", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    /// <summary>A remark that means "too much for one answer": out of memory, or the query's time ran out.</summary>
    public static bool IsTooLarge(string remark)
        => remark.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || remark.Contains("timed out", StringComparison.OrdinalIgnoreCase)
        || remark.Contains("timeout", StringComparison.OrdinalIgnoreCase);
}
