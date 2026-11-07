using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;

namespace Fdp.Toolkit.ReplayBrowser.Search.Conditions
{
    /// <summary>
    /// ⭐ U-14 / C2 (docs/UX/DESIGN_Condition_Editor.md §3) — one friendly fact: its words (positive and negated), the leaf it builds, the leaf shape it
    /// recognises, its glyph key and where it is offered. ⛔ Never a second language: <see cref="Build"/> and <see cref="TryRead"/> are the two halves of ONE
    /// mapping onto <see cref="SearchPredicateDto"/>, and the round-trip rail holds them to each other.
    /// </summary>
    public sealed class FactEntry
    {
        public FactEntry(string id, string iconKey, ConditionContexts contexts, bool worldOnly,
                         Func<FactValues, bool, string> words, Func<FactValues, SearchPredicateDto> build, TryReadLeaf read, bool negatable = true)
        {
            Id = id; IconKey = iconKey; Contexts = contexts; WorldOnly = worldOnly; Words = words; Build = build; Read = read; Negatable = negatable;
        }

        public delegate bool TryReadLeaf(SearchPredicateDto leaf, out FactValues values);

        public string Id { get; }
        /// <summary>A key of the one glyph table (<c>NodeEditor.Core.Widgets.Icons</c>) for the fact chip (§4: icons inside the chip they describe).</summary>
        public string IconKey { get; }
        public ConditionContexts Contexts { get; }
        /// <summary>A fact about the world (time): its row has no WHO and no wrapper.</summary>
        public bool WorldOnly { get; }
        public bool Negatable { get; }
        /// <summary>The IS/HAS part of the sentence for these values, positive or negated: <i>is inside OBJ</i> · <i>strength is below 50 %</i>.</summary>
        public Func<FactValues, bool, string> Words { get; }
        public Func<FactValues, SearchPredicateDto> Build { get; }
        public TryReadLeaf Read { get; }
    }

    /// <summary>
    /// ⭐ U-14 / C2 — THE friendly facts (the ConditionPhrase list of R-4130), as DATA. The editor's add menu, the row's chips, the sentence and the
    /// projection all read this one table. ⚠ The Expert view's kind names are NOT here — they are the types' <c>[EditDisplayName]</c> (U-6, Q-X3).
    /// <para>Not yet: <i>was hit</i> (its event type lives above this assembly — the host registers it through <see cref="Add"/>), <i>phase has begun</i>
    /// (E11), the generic <i>[property] [op] [value]</i> (it needs PropertyNames) — those shapes read back as a Custom row, untouched.</para>
    /// </summary>
    public static class FactCatalogue
    {
        public const string Custom = "custom";

        private static readonly List<FactEntry> _entries = new();
        private static readonly Dictionary<string, FactEntry> _byId = new(StringComparer.Ordinal);

        public static IReadOnlyList<FactEntry> Entries => _entries;
        public static FactEntry? Find(string id) => _byId.TryGetValue(id, out var e) ? e : null;

        /// <summary>Adds a fact (a host's own, e.g. <i>was hit</i>). Its <see cref="FactEntry.Read"/> must not claim another entry's shape.</summary>
        public static void Add(FactEntry entry)
        {
            if (_byId.ContainsKey(entry.Id)) throw new ArgumentException($"condition facts: '{entry.Id}' appears twice.");
            _entries.Add(entry); _byId[entry.Id] = entry;
        }

        /// <summary>The entry whose leaf shape <paramref name="leaf"/> is, with its values; null ⇒ a Custom row.</summary>
        public static FactEntry? Recognise(SearchPredicateDto leaf, out FactValues values)
        {
            if (leaf.Bind is { Count: > 0 }) { values = default; return null; }   // a bound field: the Expert view owns it (its chip lives there)
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Read(leaf, out values)) return _entries[i];
            values = default;
            return null;
        }

        static FactCatalogue()
        {
            Add(new FactEntry("inside", "form/pick", ConditionContexts.Everywhere, false,
                (v, neg) => neg ? "is not inside the area" : "is inside the area",
                v => new SpatialBoundingPredicateDto { Bounds = v.Area },
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (d is not SpatialBoundingPredicateDto s || s.PositionComponentType != null || s.TriggerEvent != BoundaryEvent.EntryOrExit) return false;
                    v = new FactValues(Area: s.Bounds);
                    return true;
                }));
            Add(new FactEntry("in-the-fight", "thing/entity", ConditionContexts.Everywhere, false,
                (v, neg) => neg ? "is destroyed" : "is in the fight",
                v => new InTheFightPredicateDto(),
                (SearchPredicateDto d, out FactValues v) => { v = default; return d is InTheFightPredicateDto; }));
            AddInput("strength", "HealthFraction", "strength", "tab/unit");
            AddInput("ammunition", "AmmoFraction", "ammunition", "tab/orders");
            Add(new FactEntry("under-fire", "tab/event", ConditionContexts.Everywhere, false,
                (v, neg) => neg ? "is not under fire" : "is under fire",
                v => new InputAbovePredicateDto { Input = "UnderFire", Threshold = (float)v.Number },
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (d is not InputAbovePredicateDto ia || ia.Input != "UnderFire") return false;
                    v = new FactValues(Number: ia.Threshold);
                    return true;
                }));
            Add(new FactEntry("side", "orbat/command", ConditionContexts.Everywhere, false,
                (v, neg) => (neg ? "side is not " : "side is ") + SideName(v.Side),
                v => Match(typeof(EntityInfo), nameof(EntityInfo.ForceId), (byte)v.Side, (byte)v.Side),
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (!IsMatch(d, typeof(EntityInfo), nameof(EntityInfo.ForceId), out double lo, out double hi) || lo != hi) return false;
                    v = new FactValues(Side: (ForceId)(byte)lo);
                    return true;
                }));
            Add(new FactEntry("objective-state", "thing/objective", ConditionContexts.Everywhere, false,
                (v, neg) => (neg ? "is not " : "is ") + ((ObjectiveState)v.State).ToString().ToLowerInvariant(),
                v => Match(typeof(ObjectiveStatus), nameof(ObjectiveStatus.State), v.State, v.State),
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (!IsMatch(d, typeof(ObjectiveStatus), nameof(ObjectiveStatus.State), out double lo, out double hi) || lo != hi) return false;
                    v = new FactValues(State: (int)lo);
                    return true;
                }));
            Add(new FactEntry("fired", "thing/trigger", ConditionContexts.Everywhere, false,
                (v, neg) => (neg ? "has not fired " : "has fired ") + (v.Number <= 1 ? "" : Num(v.Number) + " times").TrimEnd(),
                v => Match(typeof(ObjectiveStatus), nameof(ObjectiveStatus.FireCount), v.Number, double.MaxValue),
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (!IsMatch(d, typeof(ObjectiveStatus), nameof(ObjectiveStatus.FireCount), out double lo, out double hi) || hi != double.MaxValue) return false;
                    v = new FactValues(Number: lo);
                    return true;
                }));
            Add(new FactEntry("time", "kind/time", ConditionContexts.Everywhere, true,
                (v, neg) => (neg ? "it is not " : "it is ") + TimeWords(v.Number, v.Number2),
                v => new ScenarioTimePredicateDto { FromSeconds = v.Number, ToSeconds = v.Number2 },
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (d is not ScenarioTimePredicateDto t) return false;
                    v = new FactValues(Number: t.FromSeconds, Number2: t.ToSeconds);
                    return true;
                }));
        }

        private static void AddInput(string id, string input, string noun, string icon)
            => Add(new FactEntry(id, icon, ConditionContexts.Everywhere, false,
                (v, neg) => $"{noun} is {(neg ? "below" : "above")} {Num(v.Number * 100)} %",
                v => new InputAbovePredicateDto { Input = input, Threshold = (float)v.Number },
                (SearchPredicateDto d, out FactValues v) =>
                {
                    v = default;
                    if (d is not InputAbovePredicateDto ia || ia.Input != input) return false;
                    v = new FactValues(Number: ia.Threshold);
                    return true;
                }));

        /// <summary>The ONE shape a "field equals / is at least" fact uses: <c>PropertyMatch(Equals, Numeric[lo, hi])</c> (as the rules' own When does).</summary>
        internal static PropertyMatchDto Match(Type component, string path, double lo, double hi)
            => new() { ComponentType = component, PropertyPath = path, Operator = SearchOperator.Equals, Predicate = new NumericPredicateDto { MinValue = lo, MaxValue = hi } };

        internal static bool IsMatch(SearchPredicateDto d, Type component, string path, out double lo, out double hi)
        {
            lo = hi = 0;
            if (d is not PropertyMatchDto pm || pm.ComponentType != component || pm.PropertyPath != path || pm.Operator != SearchOperator.Equals
                || pm.Predicate is not NumericPredicateDto n || pm.Predicate.Bind is { Count: > 0 }) return false;
            lo = n.MinValue; hi = n.MaxValue;
            return true;
        }

        public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static string SideName(ForceId side) => side switch
        {
            ForceId.Friend => "Blue", ForceId.Hostile => "Red", ForceId.Neutral => "Neutral", _ => side.ToString(),
        };

        /// <summary><i>after 1:00</i> · <i>before 2:30</i> · <i>between 1:00 and 2:30</i>.</summary>
        public static string TimeWords(double from, double to)
        {
            bool open = to >= double.MaxValue / 2;
            if (from <= 0 && open) return "any time";
            if (open) return "after " + Clock(from);
            if (from <= 0) return "before " + Clock(to);
            return $"between {Clock(from)} and {Clock(to)}";
        }

        public static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }
    }
}
