using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3b B2 (📄 docs/DESIGN_Scenario_Macros.md §17, SR-101) — RUN VARIANTS: the scenario-level random choices, decided by ONE integer
    /// chosen at start (the run's <c>variant</c>) and nothing else, so the same variant is the identical run (replay, restore, "Tuesday's variant") and
    /// a new variant a different exercise from the same file.
    /// <list type="bullet">
    ///   <item>⭐ the variant travels as the reserved scenario arg <see cref="ArgName"/> — the route every node's load already reads, recorded with the
    ///     run (<c>RecordingMetadata.ScenarioArgs</c>); ⛔ never a declared param.</item>
    ///   <item><c>"Presence": 0.6</c> on an entity (a unit, a prop, a macro instance) — kept when its roll is below it.</item>
    ///   <item><c>"MacroInstance": {"Macro": ["forces/RedQRF", {"macro": "forces/RedArmour", "weight": 2}]}</c> — ONE of N, weighted.</item>
    ///   <item>⭐ variant 0 is AS AUTHORED: every entity present, the FIRST choice — what the editor shows.</item>
    ///   <item>⭐ each roll is <see cref="Roll"/>(variant, salt, the entity's key): the salt is the instance GUID inside a macro (two instances of one
    ///     macro roll apart), <see cref="Guid.Empty"/> for the scenario — ⛔ never an index, so the choice survives a reordered file.</item>
    /// </list>
    /// Applied by <see cref="ScenarioParams.Prepare"/>, the ONE routine a load and the macro expander both call. ⛔ Behaviour-level randomness is
    /// untouched (the user's ruling: <c>SimRng</c> stays the same every run).
    /// </summary>
    public static class ScenarioVariants
    {
        /// <summary>The reserved scenario arg that carries the run's variant.</summary>
        public const string ArgName = "$variant";
        /// <summary>The entity-level key of an optional entity's chance (0..1).</summary>
        public const string PresenceKey = "Presence";

        /// <summary>The run's variant in <paramref name="argsJson"/> (0 = as authored, also when absent or unreadable).</summary>
        public static int VariantOf(string? argsJson)
        {
            if (string.IsNullOrWhiteSpace(argsJson)) return 0;
            try
            {
                if (JsonNode.Parse(argsJson) is JsonObject o)
                    foreach (var (k, v) in o)
                        if (string.Equals(k, ArgName, StringComparison.Ordinal) && v is JsonValue jv)
                        {
                            if (jv.TryGetValue<int>(out int i)) return i;
                            if (jv.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
                        }
            }
            catch (System.Text.Json.JsonException) { }
            return 0;
        }

        /// <summary>A uniform [0, 1) draw that depends ONLY on its inputs (stable across processes — <see cref="StableHash"/>).</summary>
        public static double Roll(int variant, Guid salt, string key, string what)
            => StableHash.Fnv1a32($"{variant}|{salt:N}|{key}|{what}") / 4294967296.0;

        /// <summary>The macro names an instance's <c>Macro</c> value offers (one string, or a list of strings / <c>{macro, weight}</c>).</summary>
        public static List<(string Macro, double Weight)> ChoicesOf(JsonNode? macro)
        {
            var list = new List<(string, double)>();
            switch (macro)
            {
                case JsonValue v when v.TryGetValue<string>(out var s):
                    if (!string.IsNullOrWhiteSpace(s)) list.Add((s, 1));
                    break;
                case JsonArray a:
                    foreach (var item in a)
                    {
                        if (item is JsonValue iv && iv.TryGetValue<string>(out var name)) { if (!string.IsNullOrWhiteSpace(name)) list.Add((name, 1)); }
                        else if (item is JsonObject io)
                        {
                            string? m = null; double w = 1;
                            foreach (var (k, val) in io)
                            {
                                if (k.Equals("macro", StringComparison.OrdinalIgnoreCase) && val is JsonValue mv && mv.TryGetValue<string>(out var ms)) m = ms;
                                else if (k.Equals("weight", StringComparison.OrdinalIgnoreCase) && val is JsonValue wv && wv.TryGetValue<double>(out var wd)) w = wd;
                            }
                            if (!string.IsNullOrWhiteSpace(m)) list.Add((m!, w));
                        }
                    }
                    break;
            }
            return list;
        }

        /// <summary>
        /// Applies the variant to the file's entities IN PLACE: an entity whose <see cref="PresenceKey"/> roll fails is removed, every
        /// <see cref="PresenceKey"/> is stripped, and every one-of-N <c>MacroInstance.Macro</c> becomes its chosen name. ⛔ Throws
        /// <see cref="ScenarioParams.ArgsRefused"/> (naming <paramref name="here"/>) on a malformed presence or an empty choice list.
        /// </summary>
        public static void Apply(JsonObject dom, int variant, Guid salt, string here)
        {
            if ((dom["entities"] ?? dom["Entities"]) is not JsonObject entities) return;
            var absent = new List<string>();
            foreach (var (key, node) in entities)
            {
                if (node is not JsonObject entity) continue;
                string? pk = FindKey(entity, PresenceKey);
                if (pk != null)
                {
                    double p = entity[pk] is JsonValue pv && pv.TryGetValue<double>(out var pd) ? pd
                             : throw new ScenarioParams.ArgsRefused($"{here}: entity {key}'s Presence must be a number 0..1");
                    if (p < 0 || p > 1) throw new ScenarioParams.ArgsRefused($"{here}: entity {key}'s Presence {p} is not within 0..1");
                    entity.Remove(pk);
                    if (variant != 0 && Roll(variant, salt, key, "presence") >= p) { absent.Add(key); continue; }
                }
                string? mk = FindKey(entity, MacroInstance.ComponentName);
                if (mk != null && entity[mk] is JsonObject mi && FindKey(mi, "Macro") is { } macroKey && mi[macroKey] is JsonArray)
                {
                    var choices = ChoicesOf(mi[macroKey]);
                    if (choices.Count == 0) throw new ScenarioParams.ArgsRefused($"{here}: entity {key}'s macro choice list is empty");
                    mi[macroKey] = Choose(choices, variant, salt, key);
                }
            }
            foreach (var key in absent) entities.Remove(key);
        }

        /// <summary><c>true</c> when the file uses a variant construct (so a load with no params and no args still has to prepare it).</summary>
        public static bool Uses(JsonObject dom)
        {
            if ((dom["entities"] ?? dom["Entities"]) is not JsonObject entities) return false;
            foreach (var (_, node) in entities)
                if (node is JsonObject e && (FindKey(e, PresenceKey) != null
                    || (FindKey(e, MacroInstance.ComponentName) is { } mk && e[mk] is JsonObject mi && FindKey(mi, "Macro") is { } k && mi[k] is JsonArray)))
                    return true;
            return false;
        }

        /// <summary>Variant 0 ⇒ the first; otherwise a weighted pick by <see cref="Roll"/>.</summary>
        public static string Choose(IReadOnlyList<(string Macro, double Weight)> choices, int variant, Guid salt, string key)
        {
            if (variant == 0 || choices.Count == 1) return choices[0].Macro;
            double total = 0; foreach (var c in choices) total += Math.Max(0, c.Weight);
            if (total <= 0) return choices[0].Macro;
            double r = Roll(variant, salt, key, "macro") * total;
            foreach (var c in choices) { r -= Math.Max(0, c.Weight); if (r < 0) return c.Macro; }
            return choices[^1].Macro;
        }

        private static string? FindKey(JsonObject o, string name)
        {
            foreach (var (k, _) in o) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }
    }
}
