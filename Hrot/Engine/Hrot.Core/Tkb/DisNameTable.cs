using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Fdp.Core;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-1017</c> S0 — names for DIS entity type levels, read from the data file <c>DisNames.json</c> (embedded).
    /// The Add Entity picker groups types by <see cref="Path"/>. 📄 docs/DESIGN_Add_Entity_Picker.md D2, D2b.
    /// <para>⛔ Names live in DATA, not in code <c>switch</c>es (D2b: those rot silently). An unknown number reads
    /// <c>"Category 7"</c>; a 0 level is unknown and is skipped by <see cref="Path"/>.</para>
    /// </summary>
    public sealed class DisNameTable
    {
        private static readonly Lazy<DisNameTable> s_default = new(LoadEmbedded);

        private readonly Dictionary<int, string> _kinds = new();
        private readonly Dictionary<(int Kind, int Domain), string> _domains = new();
        private readonly Dictionary<int, string> _countries = new();
        private readonly Dictionary<(int Kind, int Domain, int Category), string> _categories = new();

        /// <summary>The table shipped with the engine.</summary>
        public static DisNameTable Default => s_default.Value;

        /// <summary>Parses a table in the <c>DisNames.json</c> shape (kinds, domains per kind, countries, categories per "kind.domain").</summary>
        public static DisNameTable Parse(string json)
        {
            var t = new DisNameTable();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("kinds", out var kinds))
                foreach (var p in kinds.EnumerateObject()) t._kinds[Num(p.Name)] = p.Value.GetString()!;
            if (root.TryGetProperty("domains", out var domains))
                foreach (var k in domains.EnumerateObject())
                    foreach (var d in k.Value.EnumerateObject()) t._domains[(Num(k.Name), Num(d.Name))] = d.Value.GetString()!;
            if (root.TryGetProperty("countries", out var countries))
                foreach (var p in countries.EnumerateObject()) t._countries[Num(p.Name)] = p.Value.GetString()!;
            if (root.TryGetProperty("categories", out var categories))
                foreach (var kd in categories.EnumerateObject())
                {
                    var parts = kd.Name.Split('.');
                    if (parts.Length != 2) throw new FormatException($"DIS names: category key '{kd.Name}' is not 'kind.domain'.");
                    int kind = Num(parts[0]), domain = Num(parts[1]);
                    foreach (var c in kd.Value.EnumerateObject()) t._categories[(kind, domain, Num(c.Name))] = c.Value.GetString()!;
                }
            return t;
        }

        public string KindName(byte kind) => _kinds.TryGetValue(kind, out var n) ? n : $"Kind {kind}";
        public string DomainName(byte kind, byte domain) => _domains.TryGetValue((kind, domain), out var n) ? n : $"Domain {domain}";
        public string CountryName(ushort country) => _countries.TryGetValue(country, out var n) ? n : $"Country {country}";
        public string CategoryName(byte kind, byte domain, byte category)
            => _categories.TryGetValue((kind, domain, category), out var n) ? n : $"Category {category}";

        /// <summary>
        /// The grouping path <c>Kind › Domain › Country › Category</c>, each level named, a 0 level skipped (D2: an unknown
        /// level adds no folder). An all-zero type ⇒ empty (the picker then falls back to the template's category path).
        /// </summary>
        public IReadOnlyList<string> Path(DISEntityType t)
        {
            var path = new List<string>(4);
            if (t.Kind != 0) path.Add(KindName(t.Kind));
            if (t.Domain != 0) path.Add(DomainName(t.Kind, t.Domain));
            if (t.Country != 0) path.Add(CountryName(t.Country));
            if (t.Category != 0) path.Add(CategoryName(t.Kind, t.Domain, t.Category));
            return path;
        }

        private static int Num(string s) => int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);

        private static DisNameTable LoadEmbedded()
        {
            var asm = typeof(DisNameTable).Assembly;
            using var stream = asm.GetManifestResourceStream("Hrot.Core.Tkb.DisNames.json")
                ?? throw new InvalidOperationException("DIS names: the embedded resource Hrot.Core.Tkb.DisNames.json is missing.");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
    }
}
