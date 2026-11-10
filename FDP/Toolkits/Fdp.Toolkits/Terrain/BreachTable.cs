using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>One row of the <see cref="BreachTable"/>: a scaled-distance band and the breach it makes. 📄 UTM §8.2 B-6.</summary>
    public sealed record BreachRow(string Name, float KgMin, float ZMax, float Width, float Height, bool Passage, bool WholePanel,
        Fdp.Toolkit.Tkb.Domain.FuzeKind[]? Fuzes = null)
    {
        /// <summary>True when the row applies to a warhead with <paramref name="fuze"/> (no filter = every fuze).</summary>
        public bool AppliesTo(Fdp.Toolkit.Tkb.Domain.FuzeKind fuze) => Fuzes == null || System.Array.IndexOf(Fuzes, fuze) >= 0;
    }

    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (UTM §8 D-5, §8.2 B-6) — the DATA of the breach rule: the embedded <c>Terrain/Data/breaches.json</c>, overlaid by a
    /// terrain folder's own <c>breaches.json</c> (a key it names replaces the shared one; <c>rows</c> replaces the whole row list), exactly as
    /// the material library is. ⭐ One table keyed on a warhead's <c>ExplosiveKg</c> covers every present and future weapon — no per-weapon row.
    /// </summary>
    public sealed class BreachTable
    {
        private static readonly Lazy<BreachTable> s_shared = new(LoadShared);

        /// <summary>The table shipped with the engine.</summary>
        public static BreachTable Shared => s_shared.Value;

        private BreachTable(JsonObject doc, string source)
        {
            Doc = doc;
            float F(string key) => doc[key] is JsonNode n ? (float)n.GetValue<double>() : throw new ArgumentException($"Breach table '{source}': '{key}' is missing.");
            MinKg = F("minKg"); ReferenceKg = F("referenceKg"); ReferenceResistance = F("referenceResistanceMmRhaPerM");
            ScaleMin = F("scaleMin"); ScaleMax = F("scaleMax"); FloorSnap = F("floorSnapM");
            PassageMinWidth = F("passageMinWidthM"); PassageMinHeight = F("passageMinHeightM"); MinSize = F("minSizeM");
            CollapseMinKg = F("collapseMinKg"); CollapseZ = F("collapseZ");
            if (ReferenceKg <= 0f || ReferenceResistance <= 0f || ScaleMin <= 0f || ScaleMax < ScaleMin)
                throw new ArgumentException($"Breach table '{source}': referenceKg, referenceResistanceMmRhaPerM, scaleMin and scaleMax must be positive (scaleMax >= scaleMin).");
            if (doc["rows"] is not JsonArray rows || rows.Count == 0) throw new ArgumentException($"Breach table '{source}' has no 'rows'.");
            var list = new List<BreachRow>(rows.Count);
            foreach (var r in rows)
            {
                if (r is not JsonObject o) throw new ArgumentException($"Breach table '{source}': a row is not an object.");
                float G(string key, float dflt) => o[key] is JsonNode n ? (float)n.GetValue<double>() : dflt;
                bool whole = o["wholePanel"]?.GetValue<bool>() ?? false;
                var row = new BreachRow((string?)o["name"] ?? "", G("kgMin", 0f), G("zMax", float.NaN), G("width", 0f), G("height", 0f),
                    o["passage"]?.GetValue<bool>() ?? false, whole, ParseFuzes(o["fuzes"], source));
                if (float.IsNaN(row.ZMax) || row.ZMax <= 0f) throw new ArgumentException($"Breach table '{source}': a row needs a positive zMax.");
                if (!whole && (row.Width <= 0f || row.Height <= 0f)) throw new ArgumentException($"Breach table '{source}': row '{row.Name}' needs a width and a height (or wholePanel).");
                list.Add(row);
                if (row.ZMax > MaxZ) MaxZ = row.ZMax;
            }
            Rows = list;
        }

        private static Fdp.Toolkit.Tkb.Domain.FuzeKind[]? ParseFuzes(JsonNode? n, string source)
        {
            if (n == null) return null;
            if (n is not JsonArray a || a.Count == 0) throw new ArgumentException($"Breach table '{source}': 'fuzes' must be a non-empty array (impact, time, proximity).");
            var r = new Fdp.Toolkit.Tkb.Domain.FuzeKind[a.Count];
            for (int i = 0; i < r.Length; i++)
                r[i] = Enum.TryParse<Fdp.Toolkit.Tkb.Domain.FuzeKind>((string?)a[i], true, out var k) ? k
                    : throw new ArgumentException($"Breach table '{source}': unknown fuze '{(string?)a[i]}' (impact, time, proximity).");
            return r;
        }

        private JsonObject Doc { get; }
        public IReadOnlyList<BreachRow> Rows { get; }
        /// <summary>A warhead below this many kg of explosive never breaches a wall.</summary>
        public float MinKg { get; }
        /// <summary>The charge (kg) the rows' width and height are stated for; sizes scale with the cube root of kg / this.</summary>
        public float ReferenceKg { get; }
        /// <summary>The material resistance (mm RHA per metre) the rows are stated for (concrete).</summary>
        public float ReferenceResistance { get; }
        public float ScaleMin { get; }
        public float ScaleMax { get; }
        /// <summary>A hole whose bottom is within this of the floor is moved onto the floor.</summary>
        public float FloorSnap { get; }
        public float PassageMinWidth { get; }
        public float PassageMinHeight { get; }
        /// <summary>A breach narrower or lower than this (after clipping) is not made.</summary>
        public float MinSize { get; }
        /// <summary>S-2: the charge (kg) from which a burst close enough collapses the whole building. Not acted on in S-1.</summary>
        public float CollapseMinKg { get; }
        /// <summary>S-2: the scaled distance within which a collapse charge collapses. Not acted on in S-1.</summary>
        public float CollapseZ { get; }
        /// <summary>The largest <see cref="BreachRow.ZMax"/>: beyond it × cbrt(kg) nothing breaches.</summary>
        public float MaxZ { get; private set; }

        /// <summary>This table with <paramref name="overrideJson"/>'s keys replacing the shared ones.</summary>
        public BreachTable WithOverrides(string? overrideJson, string source)
        {
            if (string.IsNullOrWhiteSpace(overrideJson)) return this;
            JsonNode? root;
            try { root = JsonNode.Parse(overrideJson); }
            catch (JsonException ex) { throw new ArgumentException($"Breach table '{source}' is not valid JSON: {ex.Message}", ex); }
            if (root is not JsonObject over) throw new ArgumentException($"Breach table '{source}' is not an object.");
            var merged = (JsonObject)Doc.DeepClone();
            foreach (var (k, v) in over) merged[k] = v?.DeepClone();
            return new BreachTable(merged, source);
        }

        /// <summary>A table holding exactly the keys of <paramref name="json"/> (every key is required).</summary>
        public static BreachTable Parse(string json, string source)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (JsonException ex) { throw new ArgumentException($"Breach table '{source}' is not valid JSON: {ex.Message}", ex); }
            return new BreachTable(root as JsonObject ?? throw new ArgumentException($"Breach table '{source}' is not an object."), source);
        }

        private static BreachTable LoadShared()
        {
            using var s = typeof(BreachTable).Assembly.GetManifestResourceStream("Fdp.Toolkit.Terrain.breaches.json")
                ?? throw new InvalidOperationException("Terrain: the embedded breach table Fdp.Toolkit.Terrain.breaches.json is missing.");
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd(), "shared breaches.json");
        }
    }
}
