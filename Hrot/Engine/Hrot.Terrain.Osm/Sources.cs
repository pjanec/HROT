using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

// ⭐ The two seams between the pipeline (CE-4373) and its heavy outside dependencies (CE-4374) — docs/DESIGN_Terrain_From_OSM.md §14.2.
// Fixed FIRST so both halves build at once. Network sources live behind these; tests pass offline doubles.

/// <summary>Elevation for an area: fetch what covers it once, then sample by latitude / longitude.</summary>
public interface IDemSource
{
    /// <summary>The source's true resolution in metres (30 for Copernicus GLO-30) — written as <c>heights.json sourceResolution</c> (F-9).</summary>
    double ResolutionMetres { get; }

    /// <summary>"Copernicus DEM GLO-30" — for the manifest's source and the attribution.</summary>
    string Name { get; }

    /// <summary>Fetches (or finds in <paramref name="cacheFolder"/>) everything covering <paramref name="area"/>; call before sampling.</summary>
    Task PrepareAsync(GeoBox area, string cacheFolder, IProgress<OsmProgress>? progress, CancellationToken cancellation);

    /// <summary>The ground height (m) at a point inside the prepared area, bicubic between the source's posts.</summary>
    float HeightAt(double latDeg, double lonDeg);
}

/// <summary>Turns the OSM file into <c>roads/roads.net.xml</c> in the terrain's local metres (net == local, anchor [0, 0]).</summary>
public interface IRoadBuilder
{
    /// <summary>Writes <c>roads.net.xml</c> into <paramref name="roadsFolder"/>. Throws with the tool's message when it fails (roads are not optional, §7).</summary>
    Task BuildAsync(string osmFile, UtmFrame frame, string roadsFolder, CancellationToken cancellation);
}
