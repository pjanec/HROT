using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ CE-4381 (OSM design §6 step 1, §9) — the wizard map's REAL tiles: XYZ PNGs from <see cref="DefaultUrl"/> (the URL a setting, <c>HROT_OSM_TILE_URL</c>), under the OSM tile
/// usage policy: the pipeline's ONE <see cref="HttpRetry"/> client (its User-Agent names the app and the repo), at most <see cref="MaxInFlight"/> requests at once, and a DISK cache
/// that honours the HTTP cache headers (<c>Cache-Control: max-age</c> / <c>Expires</c>; a stale tile is revalidated with <c>If-None-Match</c> / <c>If-Modified-Since</c>, a 304
/// renews it; offline, a stale tile is served). ⛔ No prefetch and no bulk download: it fetches only what the map asks for — the visible tiles (<c>OsmMapView</c>).
/// </summary>
public sealed class OsmTileSource : ITileSource
{
    public const string DefaultUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const int MaxInFlight = 2;
    /// <summary>How long a tile is fresh when the server gives no cache header (the policy's own minimum: 7 days).</summary>
    public static readonly TimeSpan DefaultFreshness = TimeSpan.FromDays(7);

    private readonly HttpRetry _http;
    private readonly string _url;
    private readonly string _cacheRoot;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(MaxInFlight, MaxInFlight);

    /// <param name="url">the XYZ template with <c>{z}</c>, <c>{x}</c>, <c>{y}</c></param>
    /// <param name="cacheRoot">the disk cache folder</param>
    /// <param name="now">the clock (tests)</param>
    /// <param name="delay">the wait between attempts (tests pass a no-op)</param>
    public OsmTileSource(HttpClient http, string cacheRoot, string url = DefaultUrl, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url;
        _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _http = new HttpRetry(http, "the map tiles (" + new Uri(_url.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0")).Host + ")", 1, delay);
    }

    public string Attribution => "© OpenStreetMap contributors";

    /// <summary>The tile's URL.</summary>
    public string UrlOf(int zoom, int x, int y)
        => _url.Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture)).Replace("{x}", x.ToString(CultureInfo.InvariantCulture)).Replace("{y}", y.ToString(CultureInfo.InvariantCulture));

    public async Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellation)
    {
        if (zoom < 0 || zoom > 22 || x < 0 || y < 0 || x >= (1 << zoom) || y >= (1 << zoom)) return null;
        string png = Path.Combine(_cacheRoot, zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".png");
        var meta = Meta.Read(png + ".meta");
        bool cached = meta != null && File.Exists(png);
        if (cached && meta!.Expires > _now()) return await File.ReadAllBytesAsync(png, cancellation).ConfigureAwait(false);   // fresh: no request

        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(() =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, UrlOf(zoom, x, y));
                    if (cached && meta!.ETag is { Length: > 0 } etag && EntityTagHeaderValue.TryParse(etag, out var tag)) req.Headers.IfNoneMatch.Add(tag);
                    if (cached && meta!.LastModified is { } lm) req.Headers.IfModifiedSince = lm;
                    return req;
                }, cancellation).ConfigureAwait(false);
            }
            catch (OsmServiceException) when (cached) { return await File.ReadAllBytesAsync(png, cancellation).ConfigureAwait(false); }   // offline: the stale tile
            catch (OsmServiceException) { return null; }   // unavailable: the map shows its light ground
            using (resp)
            {
                var expires = ExpiryOf(resp);
                if (resp.StatusCode == HttpStatusCode.NotModified && cached)
                {
                    new Meta(expires, meta!.ETag, meta.LastModified).Write(png + ".meta");
                    return await File.ReadAllBytesAsync(png, cancellation).ConfigureAwait(false);
                }
                if (resp.StatusCode == HttpStatusCode.NotModified) return null;
                var bytes = await resp.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
                if (bytes.Length == 0) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(png)!);
                string tmp = png + ".part-" + Guid.NewGuid().ToString("N");
                await File.WriteAllBytesAsync(tmp, bytes, cancellation).ConfigureAwait(false);
                File.Move(tmp, png, overwrite: true);
                new Meta(expires, resp.Headers.ETag?.ToString(), resp.Content.Headers.LastModified).Write(png + ".meta");
                return bytes;
            }
        }
        finally { _gate.Release(); }
    }

    private DateTimeOffset ExpiryOf(HttpResponseMessage resp)
    {
        var now = _now();
        var cc = resp.Headers.CacheControl;
        if (cc != null && (cc.NoStore || cc.NoCache)) return now;
        if (cc?.MaxAge is TimeSpan age) return now + age;
        if (resp.Content.Headers.Expires is { } e) return e;
        return now + DefaultFreshness;
    }

    /// <summary>A cached tile's expiry and validators, one line each, beside the PNG.</summary>
    private sealed record Meta(DateTimeOffset Expires, string? ETag, DateTimeOffset? LastModified)
    {
        public static Meta? Read(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var l = File.ReadAllLines(path);
                if (l.Length < 3 || !DateTimeOffset.TryParse(l[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exp)) return null;
                DateTimeOffset? lm = DateTimeOffset.TryParse(l[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var m) ? m : null;
                return new Meta(exp, l[1].Length > 0 ? l[1] : null, lm);
            }
            catch (IOException) { return null; }
        }

        public void Write(string path)
            => File.WriteAllLines(path, new[] { Expires.ToString("O", CultureInfo.InvariantCulture), ETag ?? "", LastModified?.ToString("O", CultureInfo.InvariantCulture) ?? "" });
    }
}
