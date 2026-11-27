using System.Text.Json.Nodes;
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

        /// <summary>
        /// ⭐ O-2 (CE-4416, DESIGN_Terrain_Authoring §18a) — the overlay in <paramref name="overlayFolder"/>: <c>overlay.json</c> and the world file it names, parsed by the
        /// SAME <see cref="TerrainWorldParser"/> as a terrain's world. A building template resolves from the overlay's own <c>buildings/</c>, then from
        /// <paramref name="sharedBuildingsRoot"/> (the base terrain's shared <c>assets/buildings</c> — <see cref="TerrainAssets.RecipesSibling"/> of the base folder).
        /// </summary>
        public static TerrainOverlay ReadOverlay(string overlayFolder, string? terrainName, string? sharedBuildingsRoot = null) => ReadOverlay(overlayFolder, terrainName, sharedBuildingsRoot, groundOf: null);

        /// <summary>⭐ CE-5014 — as above, its pieces standing on <paramref name="groundOf"/> (the base's relief the overlay is merged onto). ⭐ A caller that HAS the base passes it.</summary>
        public static TerrainOverlay ReadOverlay(string overlayFolder, string? terrainName, string? sharedBuildingsRoot, TerrainHeightGrid? groundOf) => ReadOverlay(overlayFolder, terrainName, sharedBuildingsRoot, groundOf, variant: 0);

        /// <summary>⭐ CE-11071 — as above for the run <paramref name="variant"/>: the features that variant leaves out (<see cref="OverlayPresence"/>) are not part of the world. Variant 0 = everything.</summary>
        public static TerrainOverlay ReadOverlay(string overlayFolder, string? terrainName, string? sharedBuildingsRoot, TerrainHeightGrid? groundOf, int variant)
        {
            string folder = Path.GetFullPath(overlayFolder);
            string headerPath = Path.Combine(folder, TerrainOverlayHeader.FileName);
            if (!File.Exists(headerPath))
                throw new FileNotFoundException($"[Terrain] Overlay folder '{folder}' has no '{TerrainOverlayHeader.FileName}'.", headerPath);
            var header = TerrainOverlayHeader.Parse(File.ReadAllText(headerPath), headerPath);
            string worldPath = Path.GetFullPath(Path.Combine(folder, header.World));
            if (!File.Exists(worldPath))
                throw new FileNotFoundException($"[Terrain] Overlay '{folder}' declares world '{header.World}', which does not exist at '{worldPath}'.", worldPath);
            string text = Fdp.Core.ProgressStream.ReadAllText(worldPath);
            if (variant != 0 && OverlayPresence.Mentions(text) && JsonNode.Parse(text) is JsonObject doc && OverlayPresence.Filter(doc, variant) > 0)
                text = doc.ToJsonString();   // ⭐ CE-11071 — this run's absent features dropped before the parse
            var world = TerrainWorldParser.Parse(text, terrainName, TerrainAssets.ForFolder(folder, sharedBuildingsRoot), groundOf);
            return new TerrainOverlay(header, world, folder);
        }
    }
}
