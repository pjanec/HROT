namespace Hrot.Terrain.InputSet;

// ⭐ The input set's MODEL (docs/DESIGN_Terrain_Import_UTM.md §10, §10.6, §10.9) — shared by every producer and the importer.
// Coordinates are local metres of the terrain's frame: x east, y north, z up (absolute); headings compass degrees, 0 = north, clockwise.

/// <summary>The terrain's frame: a UTM zone and the local origin (metres) — the manifest's <c>frame.utm</c>.</summary>
public readonly record struct UtmFrame(int Zone, bool South, double E0, double N0);

/// <summary>A door or a window: its reference point, the outward compass heading and its storey (0-based).</summary>
public sealed record Opening(string Type, double X, double Y, double Z, double Heading, int Storey);

/// <summary>One building (R-4191: every building its own exact template): a closed counter-clockwise footprint (first == last).</summary>
public sealed record Building(
    string Id,
    IReadOnlyList<(double X, double Y)> Footprint,
    double BaseZ,
    double Height,
    string Roof,
    double? RidgeZ,
    IReadOnlyList<double> StoreyZ,
    IReadOnlyList<Opening> Openings,
    string? Material = null,
    bool Enterable = true);

/// <summary>A polygon of an optional file (water, scrub, woods): a closed counter-clockwise ring and its single property.</summary>
public sealed record AreaFeature(IReadOnlyList<(double X, double Y)> Ring, string? Kind = null, double? Density = null);

/// <summary>A wall, fence or hedge: a line with its height and thickness.</summary>
public sealed record WallFeature(IReadOnlyList<(double X, double Y)> Line, double Height, double Thickness, string Kind);

/// <summary>The height grid's description — <c>heights/heights.json</c>.</summary>
public sealed record HeightsInfo(double OriginX, double OriginY, double CellSize, int Cols, int Rows, UtmFrame Frame)
{
    public const float NoData = -9999f;
    /// <summary>The true resolution of the source data (m), when coarser than <see cref="CellSize"/> (e.g. a 30 m DEM resampled to 1 m).</summary>
    public double? SourceResolution { get; init; }
}

/// <summary>The manifest's fixed head: what every producer writes before its own section.</summary>
public sealed record ManifestHead(string Name, UtmFrame Frame, double MinX, double MinY, double MaxX, double MaxY, double AnchorX = 0, double AnchorY = 0);
