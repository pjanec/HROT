using Hrot.Terrain.Osm;

namespace Hrot.UI.Common.TerrainRandom;

/// <summary>The wizard's presets, in the order of the mockup (page 1): Plain terrain · Open countryside · Villages · Small town · Town with suburbs · Mixed (backend's <c>TerrainPreset</c>, by index).</summary>
public static class RandomTerrainChoices
{
    public static readonly (string Name, string Line)[] Presets =
    {
        ("Plain terrain", "ground, roads and water only"),
        ("Open countryside", "fields, hedgerows, farms, woods"),
        ("Villages", "a few villages joined by roads"),
        ("Small town", "a town of ~300 houses"),
        ("Town with suburbs", "a town, villages around it"),
        ("Mixed", "a town, villages, many woods"),
    };
    public static readonly (string Label, double Metres)[] Sizes = { ("1 km", 1000), ("2 km", 2000), ("3 km", 3000), ("5.5 km", 5500) };
    public static readonly string[] Reliefs = { "Flat", "Rolling", "Hilly" };
    public static readonly string[] Vegetations = { "None", "Light", "Heavy" };
    public static readonly string[] Waters = { "None", "Ponds", "River" };
}

/// <summary>What the wizard asks for (indices into <see cref="RandomTerrainChoices"/>); the two seeds are backend's: the ground's and the layout's.</summary>
public sealed record RandomTerrainSettings(int Preset, double SizeMetres, int Relief, int Vegetation, int Water, ulong GroundSeed, ulong LayoutSeed);

/// <summary>What a preview holds for the window: the OSM wizard's own preview records (one drawing serves both), how many blocks the layout placed, a line per recipe, the seconds it took.</summary>
/// <param name="Marks">Each laid-out block as the plan labels it (recipe, seed, extent, houses) — empty for a host that cannot say.</param>
/// <param name="Relief">The ground's heights for the plan's shading — null when the host has none (the plan is then flat).</param>
public sealed record RandomTerrainView(OsmPreview Preview, int Blocks, IReadOnlyList<(string Recipe, int Blocks, int Houses)> ByRecipe, double Seconds,
                                       IReadOnlyList<PlanBlock>? Marks = null, PlanRelief? Relief = null);

/// <summary>One block of the generated layout: its recipe, seed, extent (terrain-local metres, the outline's box) and house count.</summary>
public sealed record PlanBlock(string Recipe, uint Seed, System.Numerics.Vector2 Min, System.Numerics.Vector2 Max, int Houses);

/// <summary>The ground as an <paramref name="N"/> × <paramref name="N"/> grid of heights (row 0 = the southern row) over the preview's bounds, with its range in metres.</summary>
public sealed record PlanRelief(int N, float[] Heights, float Min, float Max);

/// <summary>
/// ⭐ Terrain authoring U-8 (docs/DESIGN_Terrain_Authoring.md §18f; mockup 1) — the random-terrain wizard's engine, ONE seam: backend's <c>RandomTerrainPipeline</c> (T-1) is behind it (the editor's implementation); the window and its model never
/// reference the synth or the importer. Generate and shuffle take seconds — the model runs them on a worker.
/// </summary>
public interface IRandomTerrainService
{
    /// <summary>Makes the ground and the layout (a new ground seed = <i>Shuffle ground</i>).</summary>
    RandomTerrainView Generate(RandomTerrainSettings settings);

    /// <summary>⭐ A small generated terrain of <paramref name="preset"/> (a fixed seed, ~1 km, a coarse relief) for the preset tile's PICTURE (mockup 1 ①); null when the host cannot make one — the tile then shows its stand-in. Runs on a worker.</summary>
    RandomTerrainView? PresetPicture(int preset) => null;

    /// <summary>The same ground, new places for the blocks (<i>Shuffle layout</i> — seconds); null before a <see cref="Generate"/>.</summary>
    RandomTerrainView? ShuffleLayout(ulong layoutSeed);

    /// <summary>Whether a terrain or scenario of this name exists (the engine never overwrites).</summary>
    bool NameExists(string name);

    /// <summary>Imports the ground as terrain <paramref name="name"/> and writes the block scenario; returns the scenario's name.</summary>
    Task<string> OpenInEditorAsync(string name, CancellationToken cancellation);
}
