using Fdp.Toolkit.Orchestration;
using Hrot.Terrain.Import;
using Hrot.Terrain.Random;
using Hrot.UI.Common.TerrainRandom;

namespace Hrot.Editor.TerrainRandom;

/// <summary>
/// ⭐ Terrain authoring U-8 (docs/DESIGN_Terrain_Authoring.md §18f) — the editor's <see cref="IRandomTerrainService"/> over backend's <see cref="RandomTerrainPipeline"/> (T-1): it only ARRANGES (the settings → <see cref="RandomTerrainOptions"/>, where terrains and
/// scenarios live, the navmesh prefill) and keeps the last preview so a shuffle has its ground. ⛔ No generation of its own.
/// </summary>
public sealed class EditorRandomTerrainService : IRandomTerrainService
{
    private readonly string _nas;
    private readonly RandomTerrainPipeline _pipeline;
    private RandomTerrainPreview? _last;
    private readonly object _gate = new();

    /// <param name="nasRoot">The content root (null ⇒ the NAS); a rail passes a temp tree.</param>
    public EditorRandomTerrainService(string? nasRoot = null)
    {
        _nas = nasRoot ?? ContentLayout.NasRoot;
        _pipeline = new RandomTerrainPipeline(ContentLayout.TerrainAssets(_nas), new[] { ContentLayout.TerrainRecipeAssets(_nas) });
    }

    public static RandomTerrainOptions Options(RandomTerrainSettings s) => new()
    {
        Preset = (TerrainPreset)Math.Clamp(s.Preset, 0, 5), SizeMetres = s.SizeMetres, Relief = (ReliefLevel)Math.Clamp(s.Relief, 0, 2),
        Vegetation = (VegetationLevel)Math.Clamp(s.Vegetation, 0, 2), Water = (WaterLevel)Math.Clamp(s.Water, 0, 2), GroundSeed = s.GroundSeed, LayoutSeed = s.LayoutSeed,
    };

    private static RandomTerrainView ViewOf(RandomTerrainPreview p)
    {
        var by = p.Layout.GroupBy(b => b.Recipe).Select(g => (g.Key, g.Count(), 0)).ToList();
        return new RandomTerrainView(p.Preview, p.Layout.Count, by, p.Timings.GroundSeconds + p.Timings.LayoutSeconds + p.Timings.PreviewSeconds);
    }

    public RandomTerrainView Generate(RandomTerrainSettings settings)
    {
        var preview = _pipeline.Preview(Options(settings));
        lock (_gate) _last = preview;
        return ViewOf(preview);
    }

    public RandomTerrainView? ShuffleLayout(ulong layoutSeed)
    {
        RandomTerrainPreview? last; lock (_gate) last = _last;
        if (last is null) return null;
        var next = _pipeline.ShuffleLayout(last, layoutSeed);
        lock (_gate) _last = next;
        return ViewOf(next);
    }

    public bool NameExists(string name)
        => Directory.Exists(Path.Combine(ContentLayout.TerrainAssets(_nas), name)) || File.Exists(Path.Combine(ContentLayout.Scenarios(_nas), name, "scenario.json"));

    public async Task<string> OpenInEditorAsync(string name, CancellationToken cancellation)
    {
        RandomTerrainPreview? last; lock (_gate) last = _last;
        if (last is null) throw new InvalidOperationException("Nothing is generated yet.");
        var r = await _pipeline.OpenInEditorAsync(last, name, ContentLayout.Scenarios(_nas), ContentLayout.TerrainAssets(_nas), new NavmeshPrefill(), null, cancellation).ConfigureAwait(false);
        return Path.GetFileName(r.ScenarioFolder.TrimEnd('/', '\\'));
    }
}
