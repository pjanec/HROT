using System.Globalization;
using System.Text.Json;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ CE-4381 (OSM design §6 step 1, §9) — the Area step's place search through Nominatim, under its usage policy: the pipeline's ONE <see cref="HttpRetry"/> client (its
/// User-Agent names the app and the repo), at most ONE request a second (later calls wait their turn), and only when the author presses Enter (the wizard calls
/// <see cref="SearchAsync"/> from its Enter handler only — ⛔ never per keystroke). A result's <c>boundingbox</c> becomes its <see cref="GeoBox"/>.
/// </summary>
public sealed class NominatimPlaceSearch : IPlaceSearch
{
    public const string DefaultUrl = "https://nominatim.openstreetmap.org/search";
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    public const int MaxResults = 8;

    private readonly HttpRetry _http;
    private readonly string _url;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly SemaphoreSlim _one = new(1, 1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <param name="now">the clock (tests)</param>
    /// <param name="wait">waits out the 1 s interval, and between retry attempts (tests record it)</param>
    public NominatimPlaceSearch(HttpClient http, string url = DefaultUrl, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        _url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _wait = wait ?? ((t, ct) => Task.Delay(t, ct));
        _http = new HttpRetry(http, "the place search (" + new Uri(_url).Host + ")", 1, _wait);
    }

    /// <summary>The request URL for a query.</summary>
    public string UrlOf(string query)
        => _url + "?format=jsonv2&limit=" + MaxResults.ToString(CultureInfo.InvariantCulture) + "&q=" + Uri.EscapeDataString(query.Trim());

    public async Task<IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)>> SearchAsync(string query, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<(string, double, double, GeoBox?)>();
        await _one.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var since = _now() - _last;
            if (since < MinInterval) await _wait(MinInterval - since, cancellation).ConfigureAwait(false);
            _last = _now();
            using var resp = await _http.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, UrlOf(query)), cancellation).ConfigureAwait(false);
            await using var body = await resp.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: cancellation).ConfigureAwait(false);
            return Parse(doc.RootElement);
        }
        finally { _last = _now(); _one.Release(); }
    }

    /// <summary>Nominatim's <c>jsonv2</c> array → name, centre and box (<c>boundingbox</c> = [south, north, west, east], as strings).</summary>
    public static IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)> Parse(JsonElement root)
    {
        var list = new List<(string, double, double, GeoBox?)>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in root.EnumerateArray())
        {
            if (!Num(r, "lat", out double lat) || !Num(r, "lon", out double lon)) continue;
            string name = r.TryGetProperty("display_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : $"{lat:F4}, {lon:F4}";
            GeoBox? box = null;
            if (r.TryGetProperty("boundingbox", out var bb) && bb.ValueKind == JsonValueKind.Array && bb.GetArrayLength() == 4
                && D(bb[0], out double s) && D(bb[1], out double no) && D(bb[2], out double w) && D(bb[3], out double e) && no > s && e > w)
                box = new GeoBox(s, w, no, e);
            list.Add((name, lat, lon, box));
        }
        return list;
    }

    private static bool Num(JsonElement r, string name, out double v) { v = 0; return r.TryGetProperty(name, out var p) && D(p, out v); }

    private static bool D(JsonElement p, out double v)
    {
        v = 0;
        if (p.ValueKind == JsonValueKind.Number) return p.TryGetDouble(out v);
        return p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
