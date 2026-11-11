using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.TerrainRandom;
using Hrot.UI.Common.TerrainRandom;
using Xunit;

namespace Hrot.Editor.Tests.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-8 (docs/DESIGN_Terrain_Authoring.md §18f) — the editor's random-terrain service over backend's real <c>RandomTerrainPipeline</c> on a throw-away content tree (a small 600 m <i>Villages</i> terrain): <i>Generate</i> gives a preview with blocks and
/// a verdict, <i>Shuffle layout</i> keeps the SAME ground (the preview's bounds and roads are unchanged) with another layout, <i>Open in editor</i> writes the terrain and a scenario of block entities, and a name that exists is refused.
/// </summary>
public sealed class EditorRandomTerrainServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "erts-" + Guid.NewGuid().ToString("N")[..10]);

    public EditorRandomTerrainServiceTests()
    {
        string src = Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!;
        foreach (var kind in new[] { "terrain-recipes", "buildings" })
        {
            Directory.CreateDirectory(Path.Combine(_root, "assets", kind));
            foreach (var f in Directory.EnumerateFiles(Path.Combine(src, "assets", kind))) File.Copy(f, Path.Combine(_root, "assets", kind, Path.GetFileName(f)));
        }
        Directory.CreateDirectory(Path.Combine(_root, "assets", "terrain")); Directory.CreateDirectory(Path.Combine(_root, "scenarios"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public async Task U8_GenerateShuffleAndOpen_OverTheRealPipeline()
    {
        var svc = new EditorRandomTerrainService(_root);
        var settings = new RandomTerrainSettings(Preset: 2, SizeMetres: 600, Relief: 1, Vegetation: 1, Water: 0, GroundSeed: 5, LayoutSeed: 5);
        var first = svc.Generate(settings);
        Assert.True(first.Blocks > 0, "the villages preset places blocks");
        Assert.NotEmpty(first.Preview.Roads);

        var shuffled = svc.ShuffleLayout(77)!;
        Assert.Equal(first.Preview.Bounds, shuffled.Preview.Bounds);                                                   // the ground is kept
        Assert.Equal(first.Preview.Roads.Count, shuffled.Preview.Roads.Count);
        Assert.NotEqual(first.Preview.Buildings.Select(b => b.Footprint[0]).Take(5), shuffled.Preview.Buildings.Select(b => b.Footprint[0]).Take(5));   // another layout

        Assert.False(svc.NameExists("wiz-1"));
        string scenario = await svc.OpenInEditorAsync("wiz-1", CancellationToken.None);
        Assert.Equal("wiz-1", scenario);
        Assert.True(svc.NameExists("wiz-1"));
        string dom = File.ReadAllText(Path.Combine(_root, "scenarios", "wiz-1", "scenario.json"));
        Assert.Contains("TerrainBlock", dom);
        Assert.True(File.Exists(Path.Combine(_root, "assets", "terrain", "wiz-1", "terrain.json")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.OpenInEditorAsync("wiz-1", CancellationToken.None));   // never overwrites
    }
}
