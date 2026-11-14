using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings programme Stage 1 — what a wall/fence panel is made of: the ONE place the sight, fire and sound solvers
    /// and the navmesh read per-surface numbers from. 📄 docs/DESIGN_Building_Interiors.md §3c (M1–M7), §3d P2.
    /// </summary>
    public sealed record TerrainMaterial(
        string Name,
        float SightTransmittance,
        float ResistanceMmRhaPerMetre,
        float SoundAttenuationDb,
        bool BlocksMovement);

    /// <summary>
    /// ⭐ The material library: the shared data file (embedded <c>Terrain/Data/materials.json</c>, §3c's starter table)
    /// overlaid by a terrain folder's own <c>materials.json</c>. ⛔ An unknown material name fails the load loudly (M2).
    /// </summary>
    public sealed class TerrainMaterialLibrary
    {
        /// <summary>A panel that names no material is concrete (M7) — every wall before Stage 1 behaves as it did.</summary>
        public const string DefaultMaterial = "concrete";

        /// <summary>What a <c>fence</c> feature defaults to (M1: <c>fence</c> = <c>wall</c> with this material).</summary>
        public const string DefaultFenceMaterial = "fence-wood";

        private static readonly Lazy<TerrainMaterialLibrary> s_shared = new(LoadShared);
        private readonly Dictionary<string, TerrainMaterial> _byName;

        private TerrainMaterialLibrary(Dictionary<string, TerrainMaterial> byName) => _byName = byName;

        /// <summary>The shared library shipped with the engine.</summary>
        public static TerrainMaterialLibrary Shared => s_shared.Value;

        public IReadOnlyCollection<TerrainMaterial> All => _byName.Values;

        /// <summary>The material called <paramref name="name"/>; throws naming <paramref name="where"/> when unknown.</summary>
        public TerrainMaterial Get(string name, string where)
            => _byName.TryGetValue(name, out var m)
                ? m
                : throw new ArgumentException(
                    $"Terrain world {where}: unknown material '{name}' (known: {string.Join(", ", _byName.Keys)}).");

        public bool TryGet(string name, out TerrainMaterial material) => _byName.TryGetValue(name, out material!);

        /// <summary>This library with <paramref name="overrideJson"/>'s entries added or replacing by name.</summary>
        public TerrainMaterialLibrary WithOverrides(string? overrideJson, string source)
        {
            if (string.IsNullOrWhiteSpace(overrideJson)) return this;
            var merged = new Dictionary<string, TerrainMaterial>(_byName, StringComparer.Ordinal);
            foreach (var m in ParseEntries(overrideJson, source)) merged[m.Name] = m;
            return new TerrainMaterialLibrary(merged);
        }

        /// <summary>A library holding exactly the entries of <paramref name="json"/>.</summary>
        public static TerrainMaterialLibrary Parse(string json, string source)
        {
            var d = new Dictionary<string, TerrainMaterial>(StringComparer.Ordinal);
            foreach (var m in ParseEntries(json, source)) d[m.Name] = m;
            return new TerrainMaterialLibrary(d);
        }

        private static IEnumerable<TerrainMaterial> ParseEntries(string json, string source)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (JsonException ex) { throw new ArgumentException($"Material library '{source}' is not valid JSON: {ex.Message}", ex); }
            if (root?["materials"] is not JsonObject materials)
                throw new ArgumentException($"Material library '{source}' has no 'materials' object.");
            foreach (var (name, node) in materials)
            {
                if (node is not JsonObject o) throw new ArgumentException($"Material library '{source}': '{name}' is not an object.");
                float F(string key, float dflt) => o[key] is JsonNode n ? (float)n.GetValue<double>() : dflt;
                float sight = F("sight", 0f);
                if (sight < 0f || sight > 1f)
                    throw new ArgumentException($"Material library '{source}': '{name}'.sight must be 0..1 (got {sight.ToString(CultureInfo.InvariantCulture)}).");
                yield return new TerrainMaterial(name, sight, F("resistanceMmRhaPerM", 0f), F("soundDb", 0f),
                    o["blocksMovement"]?.GetValue<bool>() ?? true);
            }
        }

        private static TerrainMaterialLibrary LoadShared()
        {
            using var s = typeof(TerrainMaterialLibrary).Assembly.GetManifestResourceStream("Fdp.Toolkit.Terrain.materials.json")
                ?? throw new InvalidOperationException("Terrain: the embedded material library Fdp.Toolkit.Terrain.materials.json is missing.");
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd(), "shared materials.json");
        }
    }
}
