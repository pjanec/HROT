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
        bool BlocksMovement,
        string? ThermalClass = null);   // ⭐ CE-4662 (Thermal Drivers TD-9) — one of TerrainThermalClass.All: what an image generator's thermal model makes of this surface

    /// <summary>
    /// ⭐⭐ CE-4662 (📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md TD-9) — the FIXED list of thermal classes a terrain material or surface names
    /// (<c>materials.json</c> <c>thermalClass</c>, <c>surfaceThermalClasses</c>). A DRIVER, never a temperature (TD-1): the image generator solves the
    /// temperature from the class, the sun and the weather. ⛔ A name outside the list fails the load.
    /// </summary>
    public static class TerrainThermalClass
    {
        public static readonly string[] All =
            { "asphalt", "concrete", "brick", "metal", "glass", "wood", "soil-dry", "soil-wet", "sand", "grass", "dense-vegetation", "water", "snow" };

        public static bool IsKnown(string? name) => name != null && Array.IndexOf(All, name) >= 0;
    }

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

        /// <summary>
        /// ⭐ CE-4662 (TD-9, "per terrain material AND surface") — the thermal class of a ground SURFACE (<see cref="TerrainSurfaceType"/> by its lower-case
        /// name, plus <c>ground</c> for bare ground with no surface): <c>materials.json</c> <c>surfaceThermalClasses</c>. Null when the library names none.
        /// </summary>
        public string? SurfaceThermalClass(string surface) => _surfaceClasses.TryGetValue(surface, out var c) ? c : null;

        /// <summary>Every surface → thermal class pair the library names.</summary>
        public IReadOnlyDictionary<string, string> SurfaceThermalClasses => _surfaceClasses;
        private Dictionary<string, string> _surfaceClasses = new(StringComparer.Ordinal);

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
            var surfaces = new Dictionary<string, string>(_surfaceClasses, StringComparer.Ordinal);
            foreach (var (k, v) in ParseSurfaces(overrideJson, source)) surfaces[k] = v;
            return new TerrainMaterialLibrary(merged) { _surfaceClasses = surfaces };
        }

        /// <summary>A library holding exactly the entries of <paramref name="json"/>.</summary>
        public static TerrainMaterialLibrary Parse(string json, string source)
        {
            var d = new Dictionary<string, TerrainMaterial>(StringComparer.Ordinal);
            foreach (var m in ParseEntries(json, source)) d[m.Name] = m;
            var surfaces = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in ParseSurfaces(json, source)) surfaces[k] = v;
            return new TerrainMaterialLibrary(d) { _surfaceClasses = surfaces };
        }

        private static IEnumerable<(string, string)> ParseSurfaces(string json, string source)
        {
            if (JsonNode.Parse(json)?["surfaceThermalClasses"] is not JsonObject o) yield break;
            foreach (var (surface, node) in o)
            {
                if (surface.StartsWith('$')) continue;
                var c = node?.GetValue<string>();
                if (!TerrainThermalClass.IsKnown(c))
                    throw new ArgumentException($"Material library '{source}': surfaceThermalClasses '{surface}' = '{c}' is not one of {string.Join(", ", TerrainThermalClass.All)}.");
                yield return (surface, c!);
            }
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
                string? thermal = o["thermalClass"]?.GetValue<string>();
                if (thermal != null && !TerrainThermalClass.IsKnown(thermal))
                    throw new ArgumentException($"Material library '{source}': '{name}'.thermalClass '{thermal}' is not one of {string.Join(", ", TerrainThermalClass.All)}.");
                yield return new TerrainMaterial(name, sight, F("resistanceMmRhaPerM", 0f), F("soundDb", 0f),
                    o["blocksMovement"]?.GetValue<bool>() ?? true, thermal);
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
