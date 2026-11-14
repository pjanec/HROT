namespace Hrot.Terrain.InputSet;

/// <summary>One stage's progress while an input set is imported.</summary>
/// <param name="Stage">a short stage name ("heights", "roads", "buildings", "vegetation", "navmesh", …)</param>
/// <param name="Fraction">0…1 within that stage</param>
public readonly record struct ImportProgress(string Stage, double Fraction);

/// <summary>
/// THE terrain importer: an input set folder (docs/DESIGN_Terrain_Import_UTM.md §10) → a terrain folder
/// <c>assets/terrain/&lt;name&gt;/</c> the New Scenario picker lists. Implemented by the backend lane (CE-4368); the editor's
/// terrain-from-OSM wizard (CE-4375) calls it as its last step. The contract lives here so both lanes build against it.
/// </summary>
public interface ITerrainImporter
{
    /// <summary>Import <paramref name="inputSetFolder"/> as the terrain <paramref name="terrainName"/>.</summary>
    /// <param name="replace">false ⇒ an existing terrain of that name is refused (an <see cref="InvalidOperationException"/>)</param>
    /// <returns>the written terrain folder</returns>
    Task<string> ImportAsync(string inputSetFolder, string terrainName, bool replace,
                             IProgress<ImportProgress>? progress, CancellationToken cancellation);

    /// <summary>The scenarios that use the terrain <paramref name="terrainName"/> — listed when replacing it.</summary>
    IReadOnlyList<string> ScenariosUsing(string terrainName);
}

/// <summary>What a navmesh prefill did (UTM §12.1).</summary>
/// <param name="Tiles">tiles (each layer counted) the bake visited</param>
/// <param name="Baked">tiles baked (the rest came from an earlier prefill of identical inputs)</param>
/// <param name="Files">files written under <c>navtiles/</c></param>
/// <param name="Bytes">their size</param>
public readonly record struct NavmeshPrefillReport(int Tiles, int Baked, int Files, long Bytes);

/// <summary>
/// ⭐ CE-4393 (docs/DESIGN_Terrain_Import_UTM.md §12.1 N-3) — bakes a WRITTEN terrain folder's whole navmesh into its <c>navtiles/</c>, through
/// the same baker, settings and folder → world step a node's load uses (so the first load of the terrain bakes nothing). Implemented beside
/// the baker (<c>Fdp.Toolkits.Navigation.Recast</c>); an importer front (a CLI, the editor's wizard) hands it to the importer — the importer
/// library itself does not reference the navigation toolkit.
/// </summary>
public interface ITerrainNavmeshPrefill
{
    /// <param name="terrainFolder">a folder holding <c>terrain.json</c> and its world</param>
    /// <param name="progress">0…1 as tiles finish (called from worker threads)</param>
    NavmeshPrefillReport Bake(string terrainFolder, IProgress<double>? progress, CancellationToken cancellation);
}
