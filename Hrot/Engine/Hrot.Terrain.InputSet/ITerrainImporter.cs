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
