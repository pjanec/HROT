using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings programme Stage 1 — the side files a terrain world may reference (📄 docs/DESIGN_Building_Interiors.md
    /// §3a, §3c M2): building TEMPLATES (<c>&lt;name&gt;.building.json</c>) and the MATERIAL library.
    /// <para>Lookup, as designed: the terrain's own <c>buildings/</c> folder first, then the shared
    /// <c>{app}/Recipes/Buildings/</c>; materials = the shared library (embedded data) overlaid by the terrain folder's own
    /// <c>materials.json</c>.</para>
    /// </summary>
    public sealed class TerrainAssets
    {
        public const string BuildingsFolderName = "buildings";
        public const string TemplateSuffix = ".building.json";
        public const string MaterialsFileName = "materials.json";

        /// <summary>No side files: the shared materials, no templates.</summary>
        public static TerrainAssets None { get; } = new();

        /// <summary>Where the build ships its shared building templates: <c>{AppContext.BaseDirectory}/Recipes/Buildings</c>.</summary>
        public static string DefaultSharedBuildingsRoot => Path.Combine(AppContext.BaseDirectory, "Recipes", "Buildings");

        public TerrainMaterialLibrary Materials { get; init; } = TerrainMaterialLibrary.Shared;

        /// <summary>Template name → its JSON text, or null when not found.</summary>
        public Func<string, string?>? Templates { get; init; }

        /// <summary>The assets of the terrain folder <paramref name="terrainFolder"/>.</summary>
        public static TerrainAssets ForFolder(string terrainFolder, string? sharedBuildingsRoot = null)
        {
            if (string.IsNullOrWhiteSpace(terrainFolder)) throw new ArgumentException("A terrain folder is required.", nameof(terrainFolder));
            string shared = sharedBuildingsRoot ?? DefaultSharedBuildingsRoot;
            string materialsPath = Path.Combine(terrainFolder, MaterialsFileName);
            return new TerrainAssets
            {
                Materials = File.Exists(materialsPath)
                    ? TerrainMaterialLibrary.Shared.WithOverrides(File.ReadAllText(materialsPath), materialsPath)
                    : TerrainMaterialLibrary.Shared,
                Templates = name =>
                {
                    if (name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains(".."))
                        throw new ArgumentException($"Building template name '{name}' may not contain a path.");
                    foreach (var dir in new[] { Path.Combine(terrainFolder, BuildingsFolderName), shared })
                    {
                        var path = Path.Combine(dir, name + TemplateSuffix);
                        if (File.Exists(path)) return File.ReadAllText(path);
                    }
                    return null;
                },
            };
        }

        /// <summary>The template <paramref name="name"/> as JSON; throws naming <paramref name="where"/> when absent or invalid.</summary>
        internal JsonObject LoadTemplate(string name, string where)
        {
            string? text = Templates?.Invoke(name);
            if (text == null)
                throw new ArgumentException($"Terrain world {where}: building template '{name}' was not found " +
                    $"('{BuildingsFolderName}/{name}{TemplateSuffix}' in the terrain folder, then the shared Recipes/Buildings).");
            try
            {
                return JsonNode.Parse(text) as JsonObject
                    ?? throw new ArgumentException($"Terrain world {where}: building template '{name}' is not a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Terrain world {where}: building template '{name}' is not valid JSON: {ex.Message}", ex);
            }
        }
    }
}
