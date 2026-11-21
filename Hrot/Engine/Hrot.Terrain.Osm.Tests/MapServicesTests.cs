using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>A local fake HTTP server: records every request, counts how many run at once, answers from a function.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _answer;
    private int _now;
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();
    public int MaxConcurrent;
    public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) => _answer = answer;
    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : this(r => Task.FromResult(answer(r))) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        int n = Interlocked.Increment(ref _now);
        int m; while ((m = MaxConcurrent) < n && Interlocked.CompareExchange(ref MaxConcurrent, n, m) != m) { }
        try { return await _answer(request).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _now); }
    }
}

/// <summary>
/// ⭐ CE-4381 (E-1) — the wizard's REAL map tiles and place search, against a local fake HTTP handler: the URL and the User-Agent; ≤ 2 tile requests at once; a fresh tile
/// from the disk cache with no request; a stale one revalidated (304 renews it); offline, the stale tile; Nominatim's box parsed and ≤ 1 search a second.
/// 🔴 Red-proofs (each compiles): the in-flight gate at 6 ⇒ the ≤ 2 rail reds · the freshness check removed ⇒ the cache-hit rail reds · the 1 s wait skipped ⇒ the throttle rail reds.
/// </summary>
public class MapServicesTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
    private static HttpResponseMessage Tile(TimeSpan? maxAge = null, string? etag = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        if (maxAge is { } a) r.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = a };
        if (etag != null) r.Headers.ETag = new EntityTagHeaderValue(etag);
        return r;
    }

    [Fact]
    public async Task Tiles_AskTheTileUrl_WithTheAppsUserAgent_AndAttribution()
    {
        var h = new RecordingHandler(_ => Tile());
        var src = new OsmTileSource(HttpRetry.CreateClient(handler: h), _dir.Path);
        Assert.Equal(Png, await src.GetTileAsync(14, 8846, 5572, default));
        var req = Assert.Single(h.Requests);
        Assert.Equal("https://tile.openstreetmap.org/14/8846/5572.png", req.RequestUri!.ToString());
        Assert.Contains("HROT-terrain/", req.Headers.UserAgent.ToString());
        Assert.Contains(HttpRetry.RepoUrl, req.Headers.UserAgent.ToString());
        Assert.Equal("© OpenStreetMap contributors", src.Attribution);
        Assert.Null(await src.GetTileAsync(2, 4, 0, default));   // outside the zoom's grid: never asked
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Tiles_AtMostTwoRequestsAtOnce()
    {
        var h = new RecordingHandler(async _ => { await Task.Delay(40); return Tile(); });
        var src = new OsmTileSource(HttpRetry.CreateClient(handler: h), _dir.Path);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => src.GetTileAsync(10, i, 3, default)));
        Assert.Equal(8, h.Requests.Count);
        Assert.Equal(OsmTileSource.MaxInFlight, h.MaxConcurrent);
    }

    [Fact]
    public async Task Tiles_FreshFromTheDiskCache_StaleRevalidated_OfflineStaleServed()
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        HttpStatusCode next = HttpStatusCode.OK;
        var h = new RecordingHandler(r => next == HttpStatusCode.OK ? Tile(TimeSpan.FromHours(1), "\"v1\"")
                                        : next == HttpStatusCode.NotModified ? new HttpResponseMessage(HttpStatusCode.NotModified) { Headers = { CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(1) } } }
                                        : throw new HttpRequestException("offline"));
        var src = new OsmTileSource(HttpRetry.CreateClient(handler: h), _dir.Path, now: () => now, delay: (_, _) => Task.CompletedTask);
        await src.GetTileAsync(12, 1, 2, default);
        Assert.Equal(Png, await src.GetTileAsync(12, 1, 2, default));   // fresh (max-age 1 h): no request
        Assert.Single(h.Requests);

        now += TimeSpan.FromHours(2); next = HttpStatusCode.NotModified;   // stale ⇒ revalidated with its ETag; 304 ⇒ the cached bytes, renewed
        Assert.Equal(Png, await src.GetTileAsync(12, 1, 2, default));
        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("\"v1\"", h.Requests.Last().Headers.IfNoneMatch.ToString());
        Assert.Equal(Png, await src.GetTileAsync(12, 1, 2, default));   // renewed: fresh again
        Assert.Equal(2, h.Requests.Count);

        now += TimeSpan.FromHours(2); next = HttpStatusCode.ServiceUnavailable;   // offline: the stale tile still shows
        Assert.Equal(Png, await src.GetTileAsync(12, 1, 2, default));
        Assert.Null(await src.GetTileAsync(12, 3, 3, default));   // never cached + offline ⇒ none
    }

    [Fact]
    public async Task Tiles_TheUrlIsASetting()
    {
        var h = new RecordingHandler(_ => Tile());
        var src = new OsmTileSource(HttpRetry.CreateClient(handler: h), _dir.Path, "https://tiles.example.org/osm/{z}/{x}/{y}.png");
        await src.GetTileAsync(3, 1, 2, default);
        Assert.Equal("https://tiles.example.org/osm/3/1/2.png", h.Requests.Single().RequestUri!.ToString());
    }

    private const string Kolin = """
        [{"place_id":1,"lat":"50.0281","lon":"15.2006","display_name":"Kolín, Středočeský kraj, Česko","boundingbox":["49.9900","50.0600","15.1200","15.2700"]},
         {"place_id":2,"lat":"50.0","lon":"15.1","display_name":"no box"}]
        """;

    [Fact]
    public async Task Search_AsksNominatim_WithTheUserAgent_AndParsesTheBox()
    {
        var h = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Kolin) });
        var s = new NominatimPlaceSearch(HttpRetry.CreateClient(handler: h));
        var r = await s.SearchAsync("Kolín ", default);
        var req = Assert.Single(h.Requests);
        Assert.Equal("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=8&q=Kol%C3%ADn", req.RequestUri!.AbsoluteUri);
        Assert.Contains("HROT-terrain/", req.Headers.UserAgent.ToString());
        Assert.Equal(2, r.Count);
        Assert.Equal("Kolín, Středočeský kraj, Česko", r[0].Name);
        Assert.Equal(new GeoBox(49.99, 15.12, 50.06, 15.27), r[0].Box);
        Assert.Null(r[1].Box);
        Assert.Empty(await s.SearchAsync("  ", default));   // nothing typed: no request
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Search_AtMostOneRequestASecond()
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var waits = new List<TimeSpan>();
        var h = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var s = new NominatimPlaceSearch(HttpRetry.CreateClient(handler: h), now: () => now, wait: (t, _) => { waits.Add(t); now += t; return Task.CompletedTask; });
        await s.SearchAsync("a", default);
        now += TimeSpan.FromMilliseconds(300);
        await s.SearchAsync("b", default);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(700) }, waits);   // the second waited out the rest of the second
        now += TimeSpan.FromSeconds(5);
        await s.SearchAsync("c", default);
        Assert.Single(waits);   // a second later: no wait
        Assert.Equal(3, h.Requests.Count);
    }

    /// <summary>The real services, live (opt-in <c>HROT_OSM_LIVE=1</c>): tile 0/0/0 and a search for Kolín.</summary>
    [SkippableFact]
    public async Task Live_TileZero_AndKolin()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HROT_OSM_LIVE") == "1", "opt-in: HROT_OSM_LIVE=1");
        using var http = HttpRetry.CreateClient(TimeSpan.FromSeconds(30));
        var png = await new OsmTileSource(http, _dir.Path).GetTileAsync(0, 0, 0, default);
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png!.Take(4).ToArray());
        var r = await new NominatimPlaceSearch(http).SearchAsync("Kolín", default);
        Assert.Contains(r, x => x.Box is { } b && b.South < 50.03 && b.North > 50.02 && b.West < 15.2 && b.East > 15.19);
    }
}
