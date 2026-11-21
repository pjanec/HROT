namespace Hrot.Terrain.Random;

/// <summary>The wizard's presets (docs/DESIGN_Terrain_Authoring.md §18f F-4). <see cref="Plain"/> has no layout: ground only.</summary>
public enum TerrainPreset { Plain, OpenCountryside, Villages, SmallTown, TownWithSuburbs, Mixed }

/// <summary>Scales the height range: flat 5 m, rolling 40 m, hilly 150 m.</summary>
public enum ReliefLevel { Flat, Rolling, Hilly }

/// <summary>Sets the trees per km² (and the woods and scrub with them): none, light, heavy.</summary>
public enum VegetationLevel { None, Light, Heavy }

/// <summary>none · ponds · a river.</summary>
public enum WaterLevel { None, Ponds, River }

/// <summary>
/// ⭐ CE-4428 F-5 — what the random-terrain wizard asks for. Two seeds on purpose: <see cref="GroundSeed"/> makes the ground (heights, roads, villages, water, woods), <see cref="LayoutSeed"/> only places blocks over it, so
/// <i>Shuffle layout</i> keeps the ground and <i>Shuffle ground</i> is a new <see cref="GroundSeed"/>.
/// </summary>
public sealed record RandomTerrainOptions
{
    public TerrainPreset Preset { get; init; } = TerrainPreset.Villages;
    /// <summary>The side of the square terrain, metres.</summary>
    public double SizeMetres { get; init; } = 1000;
    public ReliefLevel Relief { get; init; } = ReliefLevel.Rolling;
    public VegetationLevel Vegetation { get; init; } = VegetationLevel.Light;
    public WaterLevel Water { get; init; } = WaterLevel.Ponds;
    public ulong GroundSeed { get; init; } = 1;
    public ulong LayoutSeed { get; init; } = 1;

    /// <summary>The metre height range of <see cref="Relief"/>.</summary>
    public static double ReliefMetres(ReliefLevel r) => r switch { ReliefLevel.Flat => 5.0, ReliefLevel.Rolling => 40.0, _ => 150.0 };

    /// <summary>The share of the area the woods cover: light 12 %, heavy 45 % (the synth's own default). <see cref="VegetationLevel.None"/> has no woods at all (the pipeline strips them: the synth reads a share of 0 as "woods everywhere").</summary>
    public static double WoodsShareOf(VegetationLevel v) => v switch { VegetationLevel.None => 0.0, VegetationLevel.Light => 0.12, _ => 0.45 };

    /// <summary>The synth's tree budget for a vegetation level over <paramref name="km2"/>: the woods' trees at 115 per hectare (its profile) plus the 7 % lone trees.</summary>
    public static int TreesFor(VegetationLevel v, double km2) => (int)Math.Round(WoodsShareOf(v) * km2 * 100.0 * 115.0 / 0.93);

    /// <summary>Villages per km² (each is where a layout puts blocks), and the road scale, per preset.</summary>
    public static (double VillagesPerKm2, double RoadScale) GroundOf(TerrainPreset p) => p switch
    {
        TerrainPreset.Plain => (0.5, 0.2),
        TerrainPreset.OpenCountryside => (0.8, 0.4),
        TerrainPreset.Villages => (1.5, 0.6),
        TerrainPreset.SmallTown => (2.5, 0.8),
        TerrainPreset.TownWithSuburbs => (4.0, 1.0),
        _ => (2.0, 0.7),
    };

    public void Validate()
    {
        if (!double.IsFinite(SizeMetres) || SizeMetres < 200 || SizeMetres > 6000) throw new ArgumentException($"the size must be 200 … 6 000 m, not {SizeMetres}");
    }
}
