using System.Globalization;
using System.Text;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ E-1: ONE Overpass query for the box (+ margin) returning OSM XML with metadata (<c>out meta; &gt;; out meta qt;</c>) — the SAME file later
/// goes to netconvert. Read-only query service for OSM DATA; behind <see cref="IOsmSource"/> so another source can be added.
/// </summary>
public sealed class OverpassSource : IOsmSource
{
    public const string DefaultUrl = "https://overpass-api.de/api/interpreter";
    private readonly HttpRetry _http;
    private readonly string _url;

    public OverpassSource(HttpClient http, string url = DefaultUrl, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _url = url;
        _http = new HttpRetry(http, "Overpass (" + new Uri(url).Host + ")", 3, delay);
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

    public async Task FetchAsync(GeoBox boxWithMargin, string destinationFile, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        progress?.Report(new OsmProgress(OsmStage.Fetch, 0, "Asking Overpass for the map data…"));
        string query = BuildQuery(boxWithMargin);
        using var resp = await _http.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) })
        }, cancellation).ConfigureAwait(false);
        await using (var body = await resp.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
        await using (var fs = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            await body.CopyToAsync(fs, cancellation).ConfigureAwait(false);
        progress?.Report(new OsmProgress(OsmStage.Fetch, 1, "Map data downloaded"));
    }
}
