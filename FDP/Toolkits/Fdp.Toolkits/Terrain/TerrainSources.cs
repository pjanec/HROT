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
        /// <param name="targetRoot">the root an import lands in — asked only when an import is needed (a write probe): on a miss the catalog's import target; on a STALE hit the
        /// machine-wide target when there is one (⭐ <c>CE-11061</c> / <c>CE-5060</c>: a stale copy seeded into a private NAS must not be re-imported there, run after run), else the
        /// root holding <paramref name="existingDefinition"/></param>
        /// <param name="catalogRoots">every root of the catalog (where sibling <c>terrain-input</c> folders may be)</param>
        /// <returns>the <c>terrain.json</c> to use after an import, or null when nothing was imported (no input set, or the existing terrain is current)</returns>
        string? TryImport(string terrainName, string? existingDefinition, System.Func<string> targetRoot, IReadOnlyList<string> catalogRoots, CancellationToken cancellation);

        /// <summary>
        /// ⭐ <c>CE-5060</c> (docs/DESIGN_Load_GeoReference_From_Nodes.md §2) — WITHOUT importing: the definition (name, origin, UTM frame) the import of
        /// <paramref name="terrainName"/>'s committed input set writes — read from its <c>manifest.json</c> only (milliseconds). Null when there is no input
        /// set, or <paramref name="existingDefinition"/> records no input (a hand-made terrain: never touched, its own file is the truth).
        /// </summary>
        TerrainDefinition? TryReadInputDefinition(string terrainName, string? existingDefinition, IReadOnlyList<string> catalogRoots) => null;
    }

    /// <summary>
    /// ⭐ CE-11037 F-1 — the ONE process-wide registration of <see cref="ITerrainSourceImport"/>: set ONCE where an executable starts, consulted INSIDE
    /// <see cref="TerrainCatalog"/> so the dozen sites that build a catalog cannot differ. Null (the default: a tool, a test) ⇒ the catalog behaves as it always did.
    /// </summary>
    public static class TerrainSources
    {
        private static volatile ITerrainSourceImport? _import;

        public static ITerrainSourceImport? Import { get => _import; set => _import = value; }

        private static volatile string? _sharedImportRoot;

        /// <summary>
        /// ⭐ <c>CE-11039</c> (§13 F-2, revised) — the machine-wide terrain folder a first-use import lands in, set ONCE where an executable starts (a dev checkout: the source tree's
        /// <c>assets/terrain</c>, the folder <c>ContentSeed</c> seeds every NAS from; a production install has no source tree and leaves it null, so the NAS is the target). It outlives
        /// a private staging root (every scenario-regression verify child has its own, NAS included) — which is what makes the import happen ONCE PER MACHINE. The catalog also LOOKS
        /// here after its own roots, so a terrain imported by one process is found by the next without a seed in between. Null (the default: a tool, a test) ⇒ the catalog's own roots only.
        /// </summary>
        public static string? SharedImportRoot { get => _sharedImportRoot; set => _sharedImportRoot = string.IsNullOrWhiteSpace(value) ? null : value; }
    }
}
