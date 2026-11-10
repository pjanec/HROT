using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.Import;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.TerrainOsm;

/// <summary>
/// ⭐ CE-4383 (handoff L-1) — the wizard LIVE, headless, through its MODEL (no window): the REAL place search finds the place, a small box round it, the REAL Overpass preview
/// (verdict, counts), then Import with the real DEM, netconvert and the importer into a temporary NAS — and the New Scenario picker's catalog lists the terrain. Every stage
/// timed into the output. ⭐ Opt-in: <c>HROT_OSM_LIVE=1</c> (network; netconvert on PATH — <c>scripts/install-sumo.*</c>); <c>HROT_OSM_LIVE_PLACE</c> (default "Kolín") and
/// <c>HROT_OSM_LIVE_SIDE_M</c> (default 600) choose the box. ⚠ From the cloud VM Overpass is unreachable: the run then fails at the preview with the service named.
/// </summary>
public sealed class TheWizardRunsLiveTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _nas = Path.Combine(Path.GetTempPath(), "osm-live-" + Guid.NewGuid().ToString("N"));
    public TheWizardRunsLiveTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { try { Directory.Delete(_nas, true); } catch (IOException) { } }

    private static void PumpUntil(OsmWizardModel m, Func<bool> done, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (!done()) { if (sw.Elapsed.TotalSeconds > seconds) throw new TimeoutException("the wizard did not reach the state" + (m.Failure is { } f ? ": " + f : "")); m.Pump(); Thread.Sleep(20); }
    }

    [SkippableFact]
    public void TheWizard_SearchesPreviewsAndImports_ARealSmallBox()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HROT_OSM_LIVE") == "1", "opt-in: HROT_OSM_LIVE=1");
        string place = Environment.GetEnvironmentVariable("HROT_OSM_LIVE_PLACE") ?? "Kolín";
        double side = double.TryParse(Environment.GetEnvironmentVariable("HROT_OSM_LIVE_SIDE_M"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sm) ? sm : 600;
        string terrains = ContentLayout.TerrainAssets(_nas), cache = Path.Combine(_nas, "osm-cache");
        using var http = HttpRetry.CreateClient();
        var services = new OsmWizardServices(OsmPipelines.Online(cache, http), new OsmTileSource(http, Path.Combine(cache, "map-tiles")), new NominatimPlaceSearch(http),
                                             new TerrainImporter(terrains, ContentLayout.Scenarios(_nas)), terrains, Path.Combine(_nas, "terrain-input"));
        var m = new OsmWizardModel(services);
        var sw = Stopwatch.StartNew();

        m.SearchText = place; m.RunSearch();
        PumpUntil(m, () => !m.Searching, 30);
        Assert.True(m.SearchResults.Count > 0, m.SearchFailure ?? $"no result for {place}");
        var r = m.SearchResults[0];
        _out.WriteLine($"search \"{place}\": {sw.Elapsed.TotalSeconds:F1} s → {r.Name}");
        m.SetBox(WebMercator.BoxAround(r.Lat, r.Lon, side, side));
        Assert.True(m.Primary(), m.BoxRefusal);   // Area → Options
        m.Name = "osm-live-" + DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        sw.Restart();
        Assert.True(m.Primary());                 // Options → Preview
        PumpUntil(m, () => m.PreviewState is OsmRunState.Done or OsmRunState.Failed, 900);
        Assert.True(m.PreviewState == OsmRunState.Done, m.Failure);
        var p = m.Preview!;
        _out.WriteLine($"preview: {sw.Elapsed.TotalSeconds:F1} s — {p.Counts.Buildings} enterable buildings, {p.Buildings.Count} footprints, {p.Counts.RoadKm:F1} km roads, {p.Trees.Count} trees, {p.Notes.Count} notes, verdict {p.Verdict}, data of {p.DataTimestamp}");
        Assert.NotEmpty(p.Roads);
        sw.Restart();
        Assert.True(m.Primary());                 // Import
        PumpUntil(m, () => m.ImportState is OsmRunState.Done or OsmRunState.Failed, 1800);
        foreach (var s in m.Stages) _out.WriteLine($"  {s.Label}: {s.State} — {s.Produced}");
        Assert.True(m.ImportState == OsmRunState.Done, m.Failure);
        _out.WriteLine($"import: {sw.Elapsed.TotalSeconds:F1} s → {m.TerrainFolder}");
        Assert.Contains(m.Name, TerrainCatalog.ForNode(null, _nas).List());   // the New Scenario picker lists it
    }
}
