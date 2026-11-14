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

    /// <summary>The preset tile's picture: the preset at 1 km, a fixed seed, a river (the mockup's tiles all show one) and a coarse relief grid — the SAME pipeline the wizard's Generate runs, so a tile shows what the preset really makes.</summary>
    public RandomTerrainView? PresetPicture(int preset)
        => ViewOf(_pipeline.Preview(Options(new RandomTerrainSettings(preset, 1000, Relief: 1, Vegetation: 1, Water: 2, GroundSeed: 7, LayoutSeed: 7))), 12);

    private static RandomTerrainView ViewOf(RandomTerrainPreview p, int reliefN = 48)
    {
        var marks = p.Blocks.Select(b => new PlanBlock(b.Recipe, b.Seed, b.Min, b.Max, b.Houses)).ToList();
        var by = marks.GroupBy(b => b.Recipe).Select(g => (g.Key, g.Count(), g.Sum(b => b.Houses))).ToList();
        // ⭐ the ground's own height function, sampled for the plan's shading (T-1's ReliefGrid exists for exactly this)
        int n = reliefN;
        var heights = p.ReliefGrid(n);
        var (lo, hi) = p.ReliefRange;
        return new RandomTerrainView(p.Preview, p.Layout.Count, by, p.Timings.GroundSeconds + p.Timings.LayoutSeconds + p.Timings.PreviewSeconds, marks, new PlanRelief(n, heights, (float)lo, (float)hi));
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
