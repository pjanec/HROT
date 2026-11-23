using System.Collections.Generic;
using System.Threading;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-11037 (docs/DESIGN_Terrain_Import_UTM.md §13 F-1) — what turns a terrain's committed INPUT (<c>assets/terrain-input/&lt;name&gt;/</c>) into its terrain
    /// folder on first use. The importer lives in <c>Hrot.Terrain.Import</c>, which <c>Fdp.Toolkits</c> cannot reference, so the catalog sees only this seam.
    /// </summary>
    public interface ITerrainSourceImport
    {
        /// <summary>
        /// Called by <see cref="TerrainCatalog.ResolveDefinition(string)"/> (never by a host directly).
        /// </summary>
        /// <param name="terrainName">the terrain's name as resolved (may carry a sub-folder; the hook answers null for a name it has no input for)</param>
        /// <param name="existingDefinition">the <c>terrain.json</c> a root already holds, or null on a miss</param>
        /// <param name="targetRoot">the root an import lands in: the root holding <paramref name="existingDefinition"/>, else the first writable root</param>
        /// <param name="catalogRoots">every root of the catalog (where sibling <c>terrain-input</c> folders may be)</param>
        /// <returns>the <c>terrain.json</c> to use after an import, or null when nothing was imported (no input set, or the existing terrain is current)</returns>
        string? TryImport(string terrainName, string? existingDefinition, string targetRoot, IReadOnlyList<string> catalogRoots, CancellationToken cancellation);
    }

    /// <summary>
    /// ⭐ CE-11037 F-1 — the ONE process-wide registration of <see cref="ITerrainSourceImport"/>: set ONCE where an executable starts, consulted INSIDE
    /// <see cref="TerrainCatalog"/> so the dozen sites that build a catalog cannot differ. Null (the default: a tool, a test) ⇒ the catalog behaves as it always did.
    /// </summary>
    public static class TerrainSources
    {
        private static volatile ITerrainSourceImport? _import;

        public static ITerrainSourceImport? Import { get => _import; set => _import = value; }
    }
}
