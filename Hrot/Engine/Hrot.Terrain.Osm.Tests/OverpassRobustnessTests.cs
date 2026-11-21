using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

/// <summary>
/// ⭐ CE-4384 (handoff L-2, OSM §7) — Overpass robustness on hand-written answers (⛔ no real OSM data in git): ① an answer Overpass could not finish (a "runtime error: … out of
/// memory" remark in a 200, or a 504 after the retries) ⇒ the box split into four, fetched, merged by OSM id (an element in two parts once; nodes, then ways); ② any other
/// runtime-error remark in a 200 is a FAILURE naming the service, not an empty area; ③ the error says when to try again (the server's Retry-After when given).
/// 🔴 Red-proof (compiles): <c>IsTooLarge</c> always false ⇒ the out-of-memory answer fails instead of splitting and rail ① reds.
/// </summary>
public class OverpassRobustnessTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static readonly GeoBox Box = new(50.0, 15.0, 50.02, 15.04);

    // the query's bbox (south, west, north, east) back out of the form body
    private static async Task<GeoBox> BoxOf(HttpRequestMessage r)
    {
        var body = Uri.UnescapeDataString((await r.Content!.ReadAsStringAsync()).Replace('+', ' '));
        var m = Regex.Match(body, @"\[bbox:([-\d.]+),([-\d.]+),([-\d.]+),([-\d.]+)\]");
        double D(int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        return new GeoBox(D(1), D(2), D(3), D(4));
    }

    private static bool IsWhole(GeoBox b) => Math.Abs(b.South - Box.South) < 1e-6 && Math.Abs(b.North - Box.North) < 1e-6 && Math.Abs(b.West - Box.West) < 1e-6 && Math.Abs(b.East - Box.East) < 1e-6;

    // one part's answer: a node per corner of the part (rounded so neighbours share them), a way along its south edge
    private static string Part(GeoBox b)
    {
        long Id(double lat, double lon) => (long)Math.Round(lat * 1e4) * 1_000_000 + (long)Math.Round(lon * 1e4);
        long a = Id(b.South, b.West), c = Id(b.South, b.East);
        string N(long id, double lat, double lon) => $"<node id=\"{id}\" lat=\"{lat.ToString(CultureInfo.InvariantCulture)}\" lon=\"{lon.ToString(CultureInfo.InvariantCulture)}\"/>";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><osm version=\"0.6\" generator=\"Overpass API test\"><note>test</note><meta osm_base=\"2026-11-10T08:00:00Z\"/>"
             + N(a, b.South, b.West) + N(c, b.South, b.East) + $"<way id=\"777\"><nd ref=\"{a}\"/><nd ref=\"{c}\"/><tag k=\"highway\" v=\"residential\"/></way></osm>";
    }

    private static HttpResponseMessage Ok(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml) };
    private const string OutOfMemory = "<?xml version=\"1.0\"?><osm version=\"0.6\"><note>x</note><node id=\"1\" lat=\"50\" lon=\"15\"/><remark> runtime error: Query run out of memory using about 2048 MB of RAM. </remark></osm>";

    private OverpassSource Source(RecordingHandler h, int depth = OverpassSource.DefaultMaxSplitDepth)
        => new(HttpRetry.CreateClient(handler: h), delay: (_, _) => Task.CompletedTask, maxSplitDepth: depth);

    [Fact]
    public async Task OutOfMemory_SplitsIntoFour_AndMergesByOsmId()
    {
        var h = new RecordingHandler(async r => IsWhole(await BoxOf(r)) ? Ok(OutOfMemory) : Ok(Part(await BoxOf(r))));
        var progress = new List<string>();
        string file = _dir.Combine("a.osm");
        await Source(h).FetchAsync(Box, file, new SyncProgress(p => progress.Add(p.Message)), default);
        Assert.Equal(5, h.Requests.Count);   // the whole box, then its four parts
        var model = OsmXmlReader.Read(file, AreaFrame.FromBox(Box).Frame);
        Assert.Equal(6, model.Nodes.Count);   // 4 parts × their 2 south corners = 8 sent; 2 rows × 3 columns = 6 distinct (the shared middle corners once)
        Assert.Single(model.WayById);         // way 777 came back in all four parts: once
        Assert.Equal("2026-11-10T08:00:00Z", model.DataTimestamp);
        var text = File.ReadAllText(file);
        Assert.True(text.LastIndexOf("<node", StringComparison.Ordinal) < text.IndexOf("<way", StringComparison.Ordinal), "nodes before ways");
        Assert.Contains(progress, m => m.Contains("4 parts"));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.part*"));   // the part files are gone
    }

    [Fact]
    public async Task A504AfterTheRetries_SplitsToo()
    {
        var h = new RecordingHandler(async r => IsWhole(await BoxOf(r)) ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout) : Ok(Part(await BoxOf(r))));
        await Source(h).FetchAsync(Box, _dir.Combine("a.osm"), null, default);
        Assert.Equal(4 + 4, h.Requests.Count);   // the whole box tried 4 times (the retry policy), then four parts
    }

    [Fact]
    public async Task OtherRuntimeErrors_AreAFailure_NamingTheService_NotAnEmptyArea()
    {
        var h = new RecordingHandler(_ => Ok("<?xml version=\"1.0\"?><osm version=\"0.6\"><remark> runtime error: open64: 2 No such file or directory /osm3s_v0.7.62_osm_base Dispatcher_Client::1 </remark></osm>"));
        var e = await Assert.ThrowsAsync<OsmServiceException>(() => Source(h).FetchAsync(Box, _dir.Combine("a.osm"), null, default));
        Assert.Single(h.Requests);
        Assert.Contains("Overpass (overpass-api.de)", e.Message);
        Assert.Contains("No such file", e.Message);
        Assert.Contains("try again in a minute or two", e.Message);
    }

    [Fact]
    public async Task TooLargeEvenSplit_IsAFailure_SayingPickASmallerBox()
    {
        var h = new RecordingHandler(_ => Ok(OutOfMemory));
        var e = await Assert.ThrowsAsync<OsmServiceException>(() => Source(h, depth: 1).FetchAsync(Box, _dir.Combine("a.osm"), null, default));
        Assert.Equal(1 + 1, h.Requests.Count);   // the whole box, then the first part (also too large at the last depth)
        Assert.Contains("pick a smaller box", e.Message);
        Assert.Contains("Overpass", e.Message);
    }

    [Fact]
    public async Task RateLimited_TheErrorSaysTheServersWait()
    {
        var h = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30)) } });
        var e = await Assert.ThrowsAsync<OsmServiceException>(() => Source(h).FetchAsync(Box, _dir.Combine("a.osm"), null, default));
        Assert.Contains("Overpass", e.Message);
        Assert.Contains("the service asks to wait 30 s", e.Message);
        Assert.Equal(HttpStatusCode.TooManyRequests, e.Status);
    }

    private sealed class SyncProgress : IProgress<OsmProgress>
    {
        private readonly Action<OsmProgress> _a;
        public SyncProgress(Action<OsmProgress> a) => _a = a;
        public void Report(OsmProgress value) => _a(value);
    }
}
