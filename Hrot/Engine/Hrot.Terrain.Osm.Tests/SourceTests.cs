using System.Net;
using System.Text.RegularExpressions;
using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

public class AreaFrameTests
{
    [Fact]
    public void Frame_IsTheZoneOfTheCentre_E0N0RoundedTo100m_BoundsTheInnerRectangle()
    {
        var a = AreaFrame.FromBox(Fx.Box);
        Assert.Equal(new UtmFrame(33, false, 500000, 5430000), a.Frame);
        Assert.Equal(0, a.Frame.E0 % 100); Assert.Equal(0, a.Frame.N0 % 100);
        var corners = new[] { AreaFrame.ToLocal(a.Frame, Fx.Box.South, Fx.Box.West), AreaFrame.ToLocal(a.Frame, Fx.Box.South, Fx.Box.East),
                              AreaFrame.ToLocal(a.Frame, Fx.Box.North, Fx.Box.West), AreaFrame.ToLocal(a.Frame, Fx.Box.North, Fx.Box.East) };
        // inner: never outside any corner, whole metres
        Assert.True(a.MinX >= Math.Max(corners[0].X, corners[2].X) && a.MaxX <= Math.Min(corners[1].X, corners[3].X));
        Assert.True(a.MinY >= Math.Max(corners[0].Y, corners[1].Y) && a.MaxY <= Math.Min(corners[2].Y, corners[3].Y));
        Assert.Equal(Math.Floor(a.MinX), a.MinX); Assert.Equal(Math.Floor(a.MaxY), a.MaxY);
        Assert.InRange(a.Width, 598, 603);
    }

    [Fact]
    public void SouthernHemisphere_UsesTheSouthFlag_AndRoundTrips()
    {
        var box = new GeoBox(-34.0, 150.9, -33.96, 150.96);
        var a = AreaFrame.FromBox(box);
        Assert.Equal(56, a.Frame.Zone); Assert.True(a.Frame.South);
        Assert.True(a.Frame.N0 > 6_000_000);
        var (x, y) = AreaFrame.ToLocal(a.Frame, -33.98, 150.93);
        var (lat, lon) = AreaFrame.ToGeo(a.Frame, x, y);
        Assert.Equal(-33.98, lat, 9); Assert.Equal(150.93, lon, 9);
        Assert.True(a.MinX < 0 && a.MaxX > 0 && a.MinY < 0 && a.MaxY > 0);
    }

    [Fact]
    public void ABoxAcrossAZoneEdge_IsAccepted_InTheCentresZone()
    {
        var box = new GeoBox(49.0, 17.97, 49.03, 18.03);   // the 33 / 34 edge at 18°; the centre is exactly on it
        var a = AreaFrame.FromBox(box);
        Assert.Contains(a.Frame.Zone, new[] { 33, 34 });
        Assert.Null(AreaFrame.Measure(box).Refusal);
        // the far corner still lands where it should (zone-edge maths in metres, not degrees)
        var (x, y) = AreaFrame.ToLocal(a.Frame, 49.03, 18.03);
        var (lat, lon) = AreaFrame.ToGeo(a.Frame, x, y);
        Assert.Equal(49.03, lat, 8); Assert.Equal(18.03, lon, 8);
        Assert.InRange(a.Width, 4200, 4400);   // the inner rectangle of a box tilted by the meridian convergence
    }

    [Fact]
    public void WithMargin_GrowsTheBoxByAboutTheMarginOnEverySide()
    {
        var big = AreaFrame.WithMargin(Fx.Box, 200);
        var (w, h, _) = AreaFrame.Measure(big);
        Assert.InRange(w, 995, 1005); Assert.InRange(h, 895, 905);   // 600 × 500 + 2 × 200
    }

    [Theory]
    [InlineData(49.1, 15.0, 49.0, 15.1, "inverted")]
    [InlineData(49.0, 15.1, 49.1, 15.0, "inverted")]
    [InlineData(85.0, 15.0, 86.0, 15.1, "UTM")]
    public void InvalidBoxes_AreRefusedWithAReason(double s, double w, double n, double e, string word) =>
        Assert.Contains(word, AreaFrame.Measure(new GeoBox(s, w, n, e)).Refusal!);
}

public class OsmXmlReaderTests
{
    [Fact]
    public void TheFixture_IsReadIntoACompactModel()
    {
        var m = Fx.Model.Value;
        int nodes = Regex.Matches(File.ReadAllText(Fx.FixturePath), "<node ").Count;
        Assert.Equal(nodes, m.Nodes.Count);
        Assert.Equal(2, m.Relations.Count);
        Assert.Equal(36, m.Ways.Count);
        Assert.Equal("2026-01-01T00:00:00Z", m.DataTimestamp);
        Assert.Contains(m.Ways, w => w.Tags.ContainsKey("bridge") && w.Tags["layer"] == "1");
    }

    [Fact]
    public void AKnownNode_LandsAtItsLocalMetres()
    {
        var m = Fx.Model.Value;
        var entrance = m.NodeTags.Single(kv => kv.Value.ContainsKey("entrance")).Key;
        Assert.Equal(-40.0, m.Nodes[entrance].X, 2);   // the fixture was designed in local metres of this frame
        Assert.Equal(-30.0, m.Nodes[entrance].Y, 2);
    }

    [Fact]
    public void ADoctype_IsRefused()
    {
        const string xml = "<?xml version=\"1.0\"?><!DOCTYPE osm [<!ENTITY x \"y\">]><osm/>";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        Assert.ThrowsAny<System.Xml.XmlException>(() => OsmXmlReader.Read(ms, Fx.Bounds.Frame));
    }

    [Fact]
    public void WaysAndRelations_KeepTheirTagsAndMembers()
    {
        var m = Fx.Model.Value;
        var rel = m.Relations.Single(r => r.Id == 9001);
        Assert.Equal(2, rel.Members.Length); Assert.Contains(rel.Members, x => x.Role == "inner");
        Assert.Equal("apartments", rel.Tags["building"]);
        Assert.True(m.WayById[5006].Tags.ContainsKey("roof:shape"));
    }
}

public class FakeHandler : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Seen = new();
    public readonly List<string> Bodies = new();
    private readonly Queue<Func<HttpResponseMessage>> _replies = new();
    private Func<HttpResponseMessage>? _always;
    public FakeHandler Then(HttpStatusCode code, string body = "") { _replies.Enqueue(() => new HttpResponseMessage(code) { Content = new StringContent(body) }); return this; }
    public FakeHandler Throw(Exception e) { _replies.Enqueue(() => throw e); return this; }
    public FakeHandler Always(HttpStatusCode code) { _always = () => new HttpResponseMessage(code); return this; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Seen.Add(request);
        Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return _replies.Count > 0 ? _replies.Dequeue()() : (_always ?? throw new InvalidOperationException("no reply queued"))();
    }
}

public class OverpassTests
{
    private static (OverpassSource Src, FakeHandler H, List<TimeSpan> Waits) Make(FakeHandler h)
    {
        var waits = new List<TimeSpan>();
        var src = new OverpassSource(HttpRetry.CreateClient(handler: h), delay: (t, _) => { waits.Add(t); return Task.CompletedTask; });
        return (src, h, waits);
    }

    [Fact]
    public void TheQuery_IsOneXmlRequest_BoxAndTheFeatureKeys_WithMetaAndTheRecursion()
    {
        string q = OverpassSource.BuildQuery(new GeoBox(49.020629, 14.995883, 49.025145, 15.004117));
        Assert.Contains("[out:xml]", q);
        Assert.Contains("[bbox:49.020629,14.995883,49.025145,15.004117]", q);
        foreach (var key in new[] { "highway", "railway", "building", "building:part", "entrance", "landuse", "natural", "barrier", "waterway" })
            Assert.Contains($"nwr[\"{key}\"];", q);
        Assert.Contains("out meta;\n>;\nout meta qt;", q);   // E-1: the same file serves netconvert
    }

    [Fact]
    public async Task RateLimitedThenServerError_AreRetriedWithBackOff_ThenSucceeds()
    {
        using var tmp = new TempDir();
        var (src, h, waits) = Make(new FakeHandler().Then(HttpStatusCode.TooManyRequests).Then(HttpStatusCode.GatewayTimeout).Then(HttpStatusCode.OK, "<osm/>"));
        await src.FetchAsync(Fx.Box, tmp.Combine("a.osm"), null, default);
        Assert.Equal("<osm/>", File.ReadAllText(tmp.Combine("a.osm")));
        Assert.Equal(3, h.Seen.Count);
        Assert.Equal(2, waits.Count); Assert.True(waits[1] > waits[0]);
        Assert.Contains("data=", h.Bodies[0]);
        Assert.All(h.Seen, r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task ThreeRetriesAreTheLimit_TheErrorNamesTheService()
    {
        using var tmp = new TempDir();
        var (src, h, _) = Make(new FakeHandler().Always(HttpStatusCode.GatewayTimeout));
        var e = await Assert.ThrowsAsync<OsmServiceException>(() => src.FetchAsync(Fx.Box, tmp.Combine("a.osm"), null, default));
        Assert.Equal(4, h.Seen.Count);   // the call + 3 retries
        Assert.Contains("Overpass", e.Message); Assert.Contains("504", e.Message); Assert.Contains("Overpass", e.Service);
    }

    [Fact]
    public async Task AClientError_IsNotRetried()
    {
        using var tmp = new TempDir();
        var (src, h, _) = Make(new FakeHandler().Then(HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<OsmServiceException>(() => src.FetchAsync(Fx.Box, tmp.Combine("a.osm"), null, default));
        Assert.Single(h.Seen);
    }

    [Fact]
    public async Task NetworkErrorsAndTimeouts_AreRetried()
    {
        using var tmp = new TempDir();
        var (src, h, _) = Make(new FakeHandler().Throw(new HttpRequestException("connection reset")).Throw(new TaskCanceledException("timeout")).Then(HttpStatusCode.OK, "<osm/>"));
        await src.FetchAsync(Fx.Box, tmp.Combine("a.osm"), null, default);
        Assert.Equal(3, h.Seen.Count);
    }

    [Fact]
    public async Task ACancelledCall_IsNotRetried()
    {
        using var tmp = new TempDir();
        var cts = new CancellationTokenSource(); cts.Cancel();
        var (src, h, _) = Make(new FakeHandler().Then(HttpStatusCode.OK));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => src.FetchAsync(Fx.Box, tmp.Combine("a.osm"), null, cts.Token));
        Assert.Empty(h.Seen);
    }

    [Fact]
    public void TheClient_HasTheUserAgent_AndA60sTimeout()
    {
        var http = HttpRetry.CreateClient();
        Assert.Equal(TimeSpan.FromSeconds(60), http.Timeout);
        Assert.Matches(@"^HROT-terrain/\d+\.\d+\.\d+ \(\+https://github\.com/pjanec/HROT\)$", http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Matches(@"^HROT-terrain/\d+\.\d+\.\d+ \(\+https://github\.com/pjanec/HROT\)$", HttpRetry.UserAgent);
    }

    [Fact]
    public async Task TheCache_FetchesOncePerArea()
    {
        using var tmp = new TempDir();
        var cache = new DownloadCache(tmp.Path);
        int fetches = 0;
        for (int i = 0; i < 3; i++) await cache.GetOrFetchAsync(Fx.Box, "area.osm", dest => { fetches++; File.WriteAllText(dest, "x"); return Task.CompletedTask; });
        Assert.Equal(1, fetches);
        Assert.NotEqual(cache.FolderFor(Fx.Box), cache.FolderFor(new GeoBox(1, 2, 3, 4)));
    }
}
