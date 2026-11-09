using System.Numerics;

namespace Hrot.Terrain.Osm;

// ⭐ THE CONTRACT between the OSM pipeline (coordinator lane, CE-4373 / CE-4374) and the editor's wizard (ui lane, CE-4375).
// docs/DESIGN_Terrain_From_OSM.md §4a. The wizard builds against these types with a fake pipeline; the real one implements
// IOsmTerrainPipeline. Geometry in the preview is in the terrain's LOCAL metres (x east, y north) of the frame it carries.

/// <summary>The area an author picks, in WGS84 degrees.</summary>
public readonly record struct GeoBox(double South, double West, double North, double East);

/// <summary>The terrain's frame: a UTM zone and the local origin (metres) — the input set's manifest <c>frame.utm</c>.</summary>
public readonly record struct UtmFrame(int Zone, bool South, double E0, double N0);

/// <summary>What the author chose in the Options step (OSM design §6 step 2).</summary>
public sealed record OsmImportOptions
{
    public required string Name { get; init; }
    public bool Buildings { get; init; } = true;
    public bool Trees { get; init; } = true;
    public bool Walls { get; init; } = true;
    public bool Water { get; init; } = true;
    /// <summary>Floor-to-floor height (m) where OSM gives no height.</summary>
    public double StoreyHeight { get; init; } = 3.0;
    /// <summary>Storeys of a building OSM gives neither levels, height nor a known type for.</summary>
    public int DefaultStoreys { get; init; } = 2;
}

public enum OsmStage { Fetch, Parse, Heights, Roads, Buildings, Vegetation, Write }

/// <param name="Fraction">0…1 within <paramref name="Stage"/></param>
public readonly record struct OsmProgress(OsmStage Stage, double Fraction, string Message);

/// <summary>The content budget's verdict (OSM design §5.7): warn above 2×, refuse above 3× the tuned load.</summary>
public enum BudgetVerdict { Ok, Heavy, Refused }

public sealed record OsmCounts(int Buildings, int Doors, int Windows, int LoneTrees, double WoodsAreaM2,
                               double EstimatedTrees, double RoadKm, int Walls, int WaterAreas);

public sealed record PreviewRoad(string Class, Vector2[] Points);
public sealed record PreviewBuilding(string Id, Vector2[] Footprint, int Storeys, bool Enterable);
/// <param name="Kind">"woods", "scrub" or "water"</param>
public sealed record PreviewArea(string Kind, Vector2[] Ring);
/// <param name="Kind">"wall", "fence", "hedge", "retaining_wall" or "tree_row"</param>
public sealed record PreviewLine(string Kind, Vector2[] Points);

/// <summary>Everything the wizard's Preview step draws and reports.</summary>
public sealed record OsmPreview
{
    public required UtmFrame Frame { get; init; }
    /// <summary>[minX, minY, maxX, maxY] in local metres.</summary>
    public required double[] Bounds { get; init; }
    public required OsmCounts Counts { get; init; }
    public required BudgetVerdict Verdict { get; init; }
    /// <summary>Why the verdict is not Ok, in words the author reads ("5 900 buildings — HROT is tuned for 1 800").</summary>
    public string? VerdictReason { get; init; }
    /// <summary>Heights inferred, shapes dropped, holes split, … — one line each.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<PreviewRoad> Roads { get; init; }
    public required IReadOnlyList<PreviewBuilding> Buildings { get; init; }
    public required IReadOnlyList<PreviewArea> Areas { get; init; }
    public required IReadOnlyList<PreviewLine> Lines { get; init; }
    /// <summary>"© OpenStreetMap contributors · Copernicus DEM GLO-30" — shown in the wizard and written with the terrain.</summary>
    public required string Attribution { get; init; }
}

/// <summary>The pipeline the wizard drives (OSM design §6). Downloads are cached per area, so Preview then Write fetch once.</summary>
public interface IOsmTerrainPipeline
{
    /// <summary>The largest side the area may have (m) — 6 000 (Q-O3).</summary>
    double MaxSideMetres { get; }

    /// <summary>The area's size in metres, or the reason it is refused (too large, inverted) — before any download.</summary>
    (double WidthM, double HeightM, string? Refusal) Measure(GeoBox area);

    /// <summary>Fetch + parse + build the preview (no files written).</summary>
    Task<OsmPreview> PreviewAsync(GeoBox area, OsmImportOptions options, IProgress<OsmProgress>? progress, CancellationToken cancellation);

    /// <summary>Write the input set into <paramref name="outputFolder"/> (temp folder, moved on success); refused when the
    /// preview's verdict is <see cref="BudgetVerdict.Refused"/>.</summary>
    /// <returns>the input set folder</returns>
    Task<string> WriteInputSetAsync(GeoBox area, OsmImportOptions options, string outputFolder,
                                    IProgress<OsmProgress>? progress, CancellationToken cancellation);
}

/// <summary>Map tiles for the wizard's Area step (OSM design §6 step 1). The real source fetches OSM tiles with attribution and a
/// real User-Agent; tests use a fake.</summary>
public interface ITileSource
{
    string Attribution { get; }
    /// <summary>A 256 px PNG for the XYZ tile, or null when unavailable.</summary>
    Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellation);
}

/// <summary>Place-name search for the Area step (Nominatim by default).</summary>
public interface IPlaceSearch
{
    Task<IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)>> SearchAsync(string query, CancellationToken cancellation);
}
