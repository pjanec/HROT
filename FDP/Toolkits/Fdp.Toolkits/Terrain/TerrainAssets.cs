using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings programme Stage 1 — the side files a terrain world may reference (📄 docs/DESIGN_Building_Interiors.md
    /// §3a, §3c M2): building TEMPLATES (<c>&lt;name&gt;.building.json</c>) and the MATERIAL library.
    /// <para>Lookup, as designed: the terrain's own <c>buildings/</c> folder first, then the shared
    /// <c>assets/buildings/</c> — ⭐ UI-1: the one beside the terrain's <c>assets/terrain</c> (<see cref="RecipesSibling"/>), then
    /// the NAS's <c>assets/buildings/</c> (<c>CE-4089</c>, <see cref="ContentLayout.BuildingAssets"/>); materials = the shared library (embedded data) overlaid by the terrain folder's own
    /// <c>materials.json</c>.</para>
    /// </summary>
    public sealed class TerrainAssets
    {
        public const string BuildingsFolderName = "buildings";
        public const string TemplateSuffix = ".building.json";
        public const string MaterialsFileName = "materials.json";

        /// <summary>No side files: the shared materials, no templates.</summary>
        public static TerrainAssets None { get; } = new();

        /// <summary>⭐ <c>CE-4089</c> — where the shared building templates live: the NAS's <c>assets/buildings</c> (nothing under the exe folder).</summary>
        public static string DefaultSharedBuildingsRoot => ContentLayout.BuildingAssets(ContentLayout.NasRoot);

        public TerrainMaterialLibrary Materials { get; init; } = TerrainMaterialLibrary.Shared;

        /// <summary>Template name → its JSON text, or null when not found.</summary>
        public Func<string, string?>? Templates { get; init; }

        /// <summary>⭐ CE-1034 H1 — reads a side file of the terrain folder by bare name (the height grid), or null when it is not there.</summary>
        public Func<string, string?>? ReadFile { get; init; }

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
                ReadFile = name =>
                {
                    if (name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains(".."))
                        throw new ArgumentException($"Terrain side file name '{name}' may not contain a path.");
                    var path = Path.Combine(terrainFolder, name);
                    return File.Exists(path) ? File.ReadAllText(path) : null;
                },
                Templates = name =>
                {
                    if (name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains(".."))
                        throw new ArgumentException($"Building template name '{name}' may not contain a path.");
                    foreach (var dir in new[] { Path.Combine(terrainFolder, BuildingsFolderName), RecipesSibling(terrainFolder), shared })
                    {
                        var path = Path.Combine(dir, name + TemplateSuffix);
                        if (File.Exists(path)) return File.ReadAllText(path);
                    }
                    return null;
                },
            };
        }

        /// <summary>
        /// ⭐ UI-1 (platoon demos hand-off) — the shared <c>buildings</c> folder of the <c>assets</c> tree a terrain lives in:
        /// <c>assets/terrain/&lt;name&gt;/</c> → <c>assets/buildings/</c> (<c>CE-4089</c>: was <c>Recipes/Terrain</c> → <c>Recipes/Buildings</c>).
        /// On the NAS it IS <see cref="DefaultSharedBuildingsRoot"/>; in the SOURCE tree (the unit suites read it) it is the folder the dev seed
        /// copies from, so a terrain finds the shared templates wherever it is read from. ⚠ The name is historical (it was the Recipes sibling).
        /// </summary>
        public static string RecipesSibling(string terrainFolder)
        {
            var terrains = Path.GetDirectoryName(Path.GetFullPath(terrainFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var recipes = terrains == null ? null : Path.GetDirectoryName(terrains);
            return recipes == null ? Path.Combine(terrainFolder, BuildingsFolderName) : Path.Combine(recipes, ContentLayout.BuildingsDirectoryName);
        }

        /// <summary>The template <paramref name="name"/> as JSON; throws naming <paramref name="where"/> when absent or invalid.</summary>
        internal JsonObject LoadTemplate(string name, string where)
        {
            string? text = Templates?.Invoke(name);
            if (text == null)
                throw new ArgumentException($"Terrain world {where}: building template '{name}' was not found " +
                    $"('{BuildingsFolderName}/{name}{TemplateSuffix}' in the terrain folder, then the shared assets/buildings).");
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
