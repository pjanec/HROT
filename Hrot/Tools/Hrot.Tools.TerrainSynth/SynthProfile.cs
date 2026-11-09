namespace Hrot.Tools.TerrainSynth;

/// <summary>An axis-aligned rectangle in local metres.</summary>
public sealed record Rect(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Aggregate statistics of a terrain to synthesize (spec section 2). Local metres, x east, y north.</summary>
public sealed record SynthProfile(
    string Name,
    double MinX, double MinY, double MaxX, double MaxY,
    int UtmZone, bool NorthernHemisphere, double E0, double N0,
    double HeightSpacing, double ZMin, double ZMax,
    IReadOnlyList<Rect> NodataRects,
    int BuildingCount,
    double FootprintAreaP10, double FootprintAreaP50, double FootprintAreaP90,
    double RectShare,
    // index 0 = 1 storey ... index 5 = 6 storeys
    IReadOnlyList<double> StoreyShares,
    double FloorToFloorMin, double FloorToFloorMax, double FloorToFloorMedian,
    double DoorsPerHouse, double WindowsPerHouse,
    double PitchedShare, double RidgeRise, double UnassignedPerHouse,
    int TreeCount, double WoodsShare, double WoodDensityPerHa, double WoodNearestNeighbourMedian, double LoneShare,
    IReadOnlyDictionary<string, double> RoadKm)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public static SynthProfile Full { get; } = new(
        "full",
        -2750, -2750, 2750, 2750,
        32, true, 500000, 5600000,
        0.48828125, 450, 980,
        new[] { new Rect(2250, -1000, 2750, -500), new Rect(2250, 500, 2750, 1000) },
        1800,
        64, 144, 384,
        0.93,
        new[] { 0.17, 0.40, 0.32, 0.06, 0.05, 0.003 },
        2.6, 3.6, 2.95,
        2.6, 37,
        0.73, 4.0, 1.0,
        176000, 0.45, 65, 6, 0.07,
        new Dictionary<string, double>
        {
            ["residential"] = 57,
            ["unclassified"] = 20,
            ["secondary"] = 20,
            ["primary"] = 16,
            ["motorway"] = 12,
            ["tertiary"] = 9,
            ["trunk"] = 7.5,
            ["rail"] = 8.7,
        });

    public static SynthProfile Tiny { get; } = Full with
    {
        Name = "tiny",
        MinX = -250, MinY = -250, MaxX = 250, MaxY = 250,
        NodataRects = new[] { new Rect(100, 100, 120, 120) },
        BuildingCount = 40,
        TreeCount = 1500,
        // a street grid plus one primary road, about 3 km
        RoadKm = new Dictionary<string, double> { ["residential"] = 2.0, ["primary"] = 1.0 },
    };
}
