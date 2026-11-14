using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-1017</c> S0 — names for DIS entity type levels; the Add Entity picker groups types by <see cref="Path"/>.
    /// 📄 docs/DESIGN_Add_Entity_Picker.md D2.
    /// <para>⭐ <c>CE-2138</c> (C2, R-280 (behaviors)) — BUILT FROM <see cref="EntityClassTable"/> and <see cref="CountryTable"/>:
    /// a name for a DIS level IS a class that pins exactly that level (<c>Tank = 1.1.*.1</c>), so the picker, the 3-D model
    /// and the behaviours read one table. ⛔ The <c>DisNames.json</c> data file and its D2b lean are superseded: no names in
    /// data. An unknown number reads <c>"Category 7"</c>; a 0 level is unknown and is skipped by <see cref="Path"/>.</para>
    /// </summary>
    public sealed class DisNameTable
    {
        private readonly Dictionary<int, string> _kinds = new();
        private readonly Dictionary<(int Kind, int Domain), string> _domains = new();
        private readonly Dictionary<(int Kind, int Domain, int Category), string> _categories = new();

        /// <summary>The table shipped with the engine, from <see cref="EntityClassTable.Default"/>.</summary>
        public static DisNameTable Default { get; } = new(EntityClassTable.Default);

        /// <summary>Names every level that a class of <paramref name="classes"/> pins exactly (<see cref="EntityClass.NamesALevel"/>).</summary>
        public DisNameTable(EntityClassTable classes)
        {
            foreach (var c in classes.Classes)
            {
                if (!c.NamesALevel) continue;
                var m = c.Mask;
                int kind = m.ExactValue(0)!.Value;
                switch (m.DeepestPinned)
                {
                    case 0: _kinds[kind] = c.DisplayName; break;
                    case 1: _domains[(kind, m.ExactValue(1)!.Value)] = c.DisplayName; break;
                    default: _categories[(kind, m.ExactValue(1)!.Value, m.ExactValue(3)!.Value)] = c.DisplayName; break;
                }
            }
        }

        public string KindName(byte kind) => _kinds.TryGetValue(kind, out var n) ? n : $"Kind {kind}";
        public string DomainName(byte kind, byte domain) => _domains.TryGetValue((kind, domain), out var n) ? n : $"Domain {domain}";
        public string CountryName(ushort country) => CountryTable.NameOf(country) ?? $"Country {country}";
        public string CategoryName(byte kind, byte domain, byte category)
            => _categories.TryGetValue((kind, domain, category), out var n) ? n : $"Category {category}";

        /// <summary>
        /// The grouping path <c>Kind › Domain › Country › Category</c>, each level named, a 0 level skipped (D2: an unknown
        /// level adds no folder). An all-zero type ⇒ empty (the picker then falls back to the template's category path).
        /// <para>⭐ The class levels are the type's class chain filtered to the classes that name a level; the country sits
        /// between Domain and Category because a country is not a class.</para>
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
    }
}
