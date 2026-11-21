using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
using Xunit;

namespace Hrot.Presentation.Tests.TerrainOsm;

/// <summary>
/// ⭐ CE-4381 (E-1; Nominatim's policy: no search-as-you-type) — typing into the search box asks nothing; only <see cref="OsmWizardModel.RunSearch"/> asks, and the window calls it
/// only from the box's Enter (<c>ImGuiInputTextFlags.EnterReturnsTrue</c>, <c>OsmTerrainWizardWindow.SearchOverlay</c>). 🔴 Red-proof (compiles): <c>RunSearch</c> called from
/// <c>Pump</c> when the text changed ⇒ the typing half reds.
/// </summary>
public sealed class TheWizardSearchesOnEnterOnlyTests
{
    private sealed class Counting : IPlaceSearch
    {
        public int Calls;
        public Task<IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)>> SearchAsync(string query, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<IReadOnlyList<(string, double, double, GeoBox?)>>(new List<(string, double, double, GeoBox?)>());
        }
    }

    [Fact]
    public void Typing_AsksNothing_Enter_AsksOnce()
    {
        var search = new Counting();
        var m = new OsmWizardModel(new OsmWizardServices(new FakeOsmPipeline(), new FakeTileSource(), search, null, System.IO.Path.GetTempPath(), System.IO.Path.GetTempPath()));
        foreach (var t in new[] { "K", "Ko", "Kol", "Kolí", "Kolín" }) { m.SearchText = t; m.Pump(); }
        Thread.Sleep(50); m.Pump();
        Assert.Equal(0, search.Calls);
        m.RunSearch();   // Enter
        for (int i = 0; i < 50 && m.Searching; i++) { Thread.Sleep(10); m.Pump(); }
        Assert.Equal(1, search.Calls);
    }
}
