using System;
using System.IO;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-4393 (docs/DESIGN_Terrain_Import_UTM.md §12.1 N-2) — THE one step from a terrain folder's <c>terrain.json</c> to its definition and
    /// world: the node's load (<c>TerrainResidency.Prepare</c>) and the importer's navmesh prefill both read through it, so the prefill bakes
    /// exactly the world a load bakes and their tile keys agree by construction (⛔ a second reader would drift, and every key would miss).
    /// </summary>
    public static class TerrainFolderWorld
    {
        /// <summary>The definition at <paramref name="definitionPath"/>; null when it has no world file.</summary>
        public static TerrainDefinition ReadDefinition(string definitionPath)
            => TerrainDefinitionParser.Parse(Fdp.Core.ProgressStream.ReadAllText(definitionPath));   // ⭐ CE-2417 — the read reports (§12)

        /// <summary>
        /// The world <paramref name="definition"/> declares, read with its folder's side files (building templates, material and breach
        /// overrides); null when it declares none. Throws when the declared world file is missing.
        /// </summary>
        public static TerrainWorld? ReadWorld(string definitionPath, TerrainDefinition definition, string? terrainName)
        {
            if (string.IsNullOrEmpty(definition.World)) return null;
            string folder = Path.GetDirectoryName(Path.GetFullPath(definitionPath))!;
            string worldPath = Path.IsPathRooted(definition.World) ? definition.World : Path.GetFullPath(Path.Combine(folder, definition.World));
            if (!File.Exists(worldPath))
                throw new FileNotFoundException(
                    $"[Terrain] Terrain '{terrainName}' declares world '{definition.World}', which does not exist at '{worldPath}'.", worldPath);
            return TerrainWorldParser.Parse(Fdp.Core.ProgressStream.ReadAllText(worldPath), terrainName, TerrainAssets.ForFolder(folder));
        }
    }
}
