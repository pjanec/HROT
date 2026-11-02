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
    ///   <item>⭐ SR-101 ④ (showcase scenarios, <c>2026-11-02</c>) — <c>"Positions": [[x, y, z], {"at": [x, y, z], "heading": 90, "weight": 2}]</c> on an
    ///     entity (a unit, a marker, a macro instance — whose children follow it, the expander relocates them) — it STANDS at ONE of N places, weighted;
    ///     <c>heading</c> (compass °) turns it too, else its authored rotation is kept.</item>
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
        /// <summary>⭐ SR-101 ④ — the entity-level key of a one-of-N place (<see cref="PositionsOf"/>).</summary>
        public const string PositionsKey = "Positions";

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

        /// <summary>⭐ SR-101 ④ — the places a <see cref="PositionsKey"/> value offers: each <c>[x, y, z]</c> (z optional) or
        /// <c>{"at": [x, y, z], "heading": compass °, "weight": w}</c>. <c>null</c> when an item is malformed.</summary>
        public static List<(double[] At, double? Heading, double Weight)>? PositionsOf(JsonNode? value)
        {
            if (value is not JsonArray a) return null;
            var list = new List<(double[], double?, double)>();
            foreach (var item in a)
            {
                double[]? at = null; double? heading = null; double w = 1;
                if (item is JsonArray) at = Point(item);
                else if (item is JsonObject o)
                    foreach (var (k, v) in o)
                    {
                        if (k.Equals("at", StringComparison.OrdinalIgnoreCase)) at = Point(v);
                        else if (k.Equals("heading", StringComparison.OrdinalIgnoreCase) && v is JsonValue hv && hv.TryGetValue<double>(out var h)) heading = h;
                        else if (k.Equals("weight", StringComparison.OrdinalIgnoreCase) && v is JsonValue wv && wv.TryGetValue<double>(out var wd)) w = wd;
                    }
                if (at == null) return null;
                list.Add((at, heading, w));
            }
            return list;

            static double[]? Point(JsonNode? n)
            {
                if (n is not JsonArray p || p.Count < 2 || p.Count > 3) return null;
                var r = new double[3];
                for (int i = 0; i < p.Count; i++)
                    if (p[i] is JsonValue v && v.TryGetValue<double>(out var d)) r[i] = d; else return null;
                return r;
            }
        }

        /// <summary>
        /// Applies the variant to the file's entities IN PLACE: an entity whose <see cref="PresenceKey"/> roll fails is removed, every
        /// <see cref="PresenceKey"/> is stripped, every <see cref="PositionsKey"/> becomes the chosen place in its <c>SimTransform</c>, and every
        /// one-of-N <c>MacroInstance.Macro</c> becomes its chosen name. ⛔ Throws
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
                string? posKey = FindKey(entity, PositionsKey);
                if (posKey != null)
                {
                    var places = PositionsOf(entity[posKey]);
                    if (places == null || places.Count == 0)
                        throw new ScenarioParams.ArgsRefused($"{here}: entity {key}'s Positions must be a non-empty list of [x, y, z] or {{\"at\": [x, y, z], \"heading\", \"weight\"}}");
                    entity.Remove(posKey);
                    var place = places[Pick(places.ConvertAll(p => p.Weight), variant, salt, key, "position")];
                    string tk = FindKey(entity, "SimTransform") ?? "SimTransform";
                    if (entity[tk] is not JsonObject t) entity[tk] = t = new JsonObject { ["Rotation"] = new JsonArray(0, 0, 0, 1) };
                    t[FindKey(t, "Position") ?? "Position"] = new JsonArray(place.At[0], place.At[1], place.At[2]);
                    if (place.Heading is double h)
                    {
                        var q = WorldRotation.HeadingDegToQuaternion((float)h);
                        t[FindKey(t, "Rotation") ?? "Rotation"] = new JsonArray(q.X, q.Y, q.Z, q.W);
                    }
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
                if (node is JsonObject e && (FindKey(e, PresenceKey) != null || FindKey(e, PositionsKey) != null
                    || (FindKey(e, MacroInstance.ComponentName) is { } mk && e[mk] is JsonObject mi && FindKey(mi, "Macro") is { } k && mi[k] is JsonArray)))
                    return true;
            return false;
        }

        /// <summary>Variant 0 ⇒ the first; otherwise a weighted pick by <see cref="Roll"/>.</summary>
        public static string Choose(IReadOnlyList<(string Macro, double Weight)> choices, int variant, Guid salt, string key)
        {
            var weights = new List<double>(choices.Count);
            foreach (var c in choices) weights.Add(c.Weight);
            return choices[Pick(weights, variant, salt, key, "macro")].Macro;
        }

        /// <summary>The index of a weighted one-of-N pick — variant 0 ⇒ the first; <paramref name="what"/> keeps two choices on one entity apart.</summary>
        public static int Pick(IReadOnlyList<double> weights, int variant, Guid salt, string key, string what)
        {
            if (variant == 0 || weights.Count == 1) return 0;
            double total = 0; foreach (var w in weights) total += Math.Max(0, w);
            if (total <= 0) return 0;
            double r = Roll(variant, salt, key, what) * total;
            for (int i = 0; i < weights.Count; i++) { r -= Math.Max(0, weights[i]); if (r < 0) return i; }
            return weights.Count - 1;
        }

        private static string? FindKey(JsonObject o, string name)
        {
            foreach (var (k, _) in o) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }
    }
}
