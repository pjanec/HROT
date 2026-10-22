using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §3) — one cue of a scenario's checks: a take anchor (<see cref="Spec"/>, judged by
    /// <see cref="AnchorBook.HoldsFor"/> for the state cues) or one of the two the verifier adds — <c>field</c> (a component value by
    /// <see cref="Path"/>) and a <c>shot</c> / <c>blast</c> row filtered by <see cref="Where"/>.
    /// </summary>
    public sealed record CheckCue
    {
        public AnchorSpec Spec { get; init; } = new();
        /// <summary><c>shot</c> / <c>blast</c>: the row's fields that must match (<c>"penetration": [">", 400]</c> or <c>"field": value</c>).</summary>
        public IReadOnlyList<(string Field, string Op, JsonNode? Value)> Where { get; init; } = Array.Empty<(string, string, JsonNode?)>();
        /// <summary><c>shot</c>: the round STRUCK this unit.</summary>
        public string? Hit { get; init; }
        /// <summary><c>field</c>: <c>Component.Field[.Sub]</c> in the entity dump's <c>Components</c>.</summary>
        public string? Path { get; init; }
        public string Op { get; init; } = "==";
        /// <summary><c>field</c>: the value compared with; <c>"$now"</c> is the sim time of the sample.</summary>
        public JsonNode? Value { get; init; }
        /// <summary><c>field</c>: <c>any</c> (default) · <c>all</c> · <c>atLeast:n</c> of the units <see cref="AnchorSpec.Who"/> names.</summary>
        public string Quantifier { get; init; } = "any";

        public string On => Spec.On;

        /// <summary>The cues a check may use: the take's nine and <c>field</c>.</summary>
        public static readonly IReadOnlyList<string> Kinds = AnchorSpec.Kinds.Concat(new[] { "field" }).ToArray();

        public static readonly IReadOnlyList<string> Ops = new[] { "==", "!=", "<", "<=", ">", ">=", "in", "contains" };

        public IEnumerable<string> Names() => Spec.Names().Concat(Hit != null ? new[] { Hit } : Array.Empty<string>());
    }

    /// <summary>⭐ One expectation: the cue holds once in [<see cref="After"/>, <see cref="Before"/>], NEVER in it, or at the end.</summary>
    public sealed record Expectation
    {
        public string Name { get; init; } = "";
        public CheckCue Cue { get; init; } = new();
        public bool Never { get; init; }
        public bool AtEnd { get; init; }
        public AtExpr? After { get; init; }
        public AtExpr? Before { get; init; }
    }

    /// <summary>⭐ A change the check makes to the world at <see cref="At"/> (the old live checks' hand edits — wound a man, set an attribute).</summary>
    public sealed record CheckAction
    {
        public string? Name { get; init; }
        public AtExpr At { get; init; }
        public string Who { get; init; } = "";
        public string Component { get; init; } = "";
        public JsonObject Patch { get; init; } = new();
    }

    /// <summary>⭐ <c>CE-4042</c> — <c>scenarios/&lt;s&gt;/checks.json</c>: what a scenario must do, as data (§3).</summary>
    public sealed record ScenarioChecks
    {
        public string Scenario { get; init; } = "";
        public double RunFor { get; init; } = 60d;
        /// <summary>Seconds between golden snapshots; null = no golden (a demo, §5).</summary>
        public double? GoldenEvery { get; init; }
        /// <summary>How often the world is read for the cues (sim seconds; actions still land on their exact step).</summary>
        public double SampleEvery { get; init; } = 0.25d;
        public IReadOnlyDictionary<string, CheckCue> Anchors { get; init; } = new Dictionary<string, CheckCue>(StringComparer.Ordinal);
        public IReadOnlyList<CheckAction> Do { get; init; } = Array.Empty<CheckAction>();
        public IReadOnlyList<Expectation> Expect { get; init; } = Array.Empty<Expectation>();

        /// <summary>Every unit a cue or an action names (to validate against the scenario's cast).</summary>
        public IEnumerable<string> Names() =>
            Anchors.Values.SelectMany(a => a.Names())
                .Concat(Expect.SelectMany(e => e.Cue.Names()))
                .Concat(Do.Select(a => a.Who))
                .Distinct(StringComparer.Ordinal);
    }

    /// <summary>⭐ <c>CE-4042</c> — reads and writes <see cref="ScenarioChecks"/>; every mistake is named, never guessed.</summary>
    public static class ChecksJson
    {
        public const string FileName = "checks.json";

        public static ScenarioChecks Parse(JsonObject json, string scenario, List<string> problems)
        {
            foreach (var (k, _) in json)
                if (k is not ("$doc" or "runFor" or "golden" or "sampleEvery" or "anchors" or "do" or "expect"))
                    problems.Add($"unknown key '{k}'");

            double? every = null;
            switch (json["golden"])
            {
                case null: break;
                case JsonValue v when v.TryGetValue<bool>(out var b): if (b) every = 10d; break;
                case JsonObject g:
                    every = TakeJson.Double(g["every"]) ?? 10d;
                    if (!(every > 0)) problems.Add("golden.every must be > 0");
                    break;
                default: problems.Add("golden is {\"every\": seconds} or false"); break;
            }

            var anchors = new Dictionary<string, CheckCue>(StringComparer.Ordinal);
            if (json["anchors"] is JsonObject ao)
                foreach (var (k, v) in ao)
                    if (v is JsonObject o) anchors[k] = ParseCue(o, $"anchor '{k}'", problems, extra: Array.Empty<string>());
                    else problems.Add($"anchor '{k}' is not an object");

            var actions = new List<CheckAction>();
            if (json["do"] is JsonArray da)
                for (int i = 0; i < da.Count; i++)
                {
                    var where = $"do[{i}]";
                    if (da[i] is not JsonObject a) { problems.Add($"{where} is not an object"); continue; }
                    foreach (var (k, _) in a)
                        if (k is not ("name" or "at" or "set")) problems.Add($"{where}: unknown key '{k}'");
                    if (a["set"] is not JsonObject set || set["who"] is null || set["component"] is null || set["patch"] is not JsonObject patch)
                    { problems.Add($"{where}: set is {{who, component, patch}}"); continue; }
                    var at = Time(a["at"], where + " at", problems);
                    if (at is null) { problems.Add($"{where}: at is required"); continue; }
                    actions.Add(new CheckAction
                    {
                        Name = a["name"]?.ToString(), At = at.Value, Who = set["who"]!.ToString(), Component = set["component"]!.ToString(),
                        Patch = (JsonObject)patch.DeepClone(),
                    });
                }

            var expect = new List<Expectation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (json["expect"] is JsonArray ea)
                for (int i = 0; i < ea.Count; i++)
                {
                    if (ea[i] is not JsonObject e) { problems.Add($"expect[{i}] is not an object"); continue; }
                    var name = e["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) { problems.Add($"expect[{i}]: name is required"); continue; }
                    if (!seen.Add(name)) problems.Add($"expect '{name}': the name is used twice");
                    var where = $"expect '{name}'";
                    bool never = e["never"] is JsonObject;
                    bool atEnd = e["atEnd"] is JsonValue ev && ev.TryGetValue<bool>(out var ae) && ae;
                    CheckCue cue = never
                        ? ParseCue((JsonObject)e["never"]!, where + " never", problems, Array.Empty<string>())
                        : ParseCue(e, where, problems, new[] { "name", "after", "before", "atEnd", "never" });
                    if (never)
                        foreach (var (k, _) in e)
                            if (k is not ("name" or "never" or "after" or "before")) problems.Add($"{where}: unknown key '{k}' beside never");
                    if (never && atEnd) problems.Add($"{where}: never and atEnd together");
                    expect.Add(new Expectation
                    {
                        Name = name!, Cue = cue, Never = never, AtEnd = atEnd,
                        After = Time(e["after"], where + " after", problems), Before = Time(e["before"], where + " before", problems),
                    });
                }

            if (expect.Count == 0) problems.Add("expect is empty — a check with nothing to judge");
            double runFor = TakeJson.Double(json["runFor"]) ?? 60d;
            if (!(runFor > 0)) problems.Add("runFor must be > 0");
            double sample = TakeJson.Double(json["sampleEvery"]) ?? 0.25d;
            if (!(sample > 0)) problems.Add("sampleEvery must be > 0");

            // ⭐ every name a time refers to must be an anchor, an action or another expectation
            var known = new HashSet<string>(anchors.Keys.Concat(actions.Where(a => a.Name != null).Select(a => a.Name!)).Concat(seen), StringComparer.Ordinal);
            foreach (var t in actions.Select(a => (AtExpr?)a.At).Concat(expect.SelectMany(e => new[] { e.After, e.Before })))
                if (t is { Anchor: { } n } && !known.Contains(n)) problems.Add($"a time names '{n}', which is no anchor, action or expectation");

            return new ScenarioChecks
            {
                Scenario = scenario, RunFor = runFor, GoldenEvery = every, SampleEvery = sample,
                Anchors = anchors, Do = actions, Expect = expect,
            };
        }

        private static AtExpr? Time(JsonNode? n, string where, List<string> problems)
        {
            if (n is null) return null;
            try { return AtExpr.Parse(n); }
            catch (FormatException ex) { problems.Add($"{where}: {ex.Message}"); return null; }
        }

        private static CheckCue ParseCue(JsonObject o, string where, List<string> problems, IReadOnlyCollection<string> extra)
        {
            var on = o["on"]?.ToString() ?? "";
            if (!CheckCue.Kinds.Contains(on)) problems.Add($"{where}: cue '{on}' is not one of {string.Join(", ", CheckCue.Kinds)}");
            var spec = new JsonObject();
            var where_ = new List<(string, string, JsonNode?)>();
            string? hit = null, path = null, op = "==", quantifier = "any";
            JsonNode? value = null;
            foreach (var (k, v) in o)
            {
                if (extra.Contains(k)) continue;
                switch (k)
                {
                    case "on" or "who" or "by" or "target" or "is" or "n" or "point" or "within" or "below":
                        spec[k] = v?.DeepClone(); break;
                    case "at": spec["target"] = v?.DeepClone(); break;          // a shot's target: "at" reads as the sentence says
                    case "hit": hit = v?.ToString(); break;
                    case "path": path = v?.ToString(); break;
                    case "op": op = v?.ToString() ?? "=="; break;
                    case "value": value = v?.DeepClone(); break;
                    case "quantifier": quantifier = v?.ToString() ?? "any"; break;
                    case "where":
                        if (v is not JsonObject w) { problems.Add($"{where}: where is an object"); break; }
                        foreach (var (f, c) in w)
                            if (c is JsonArray pair && pair.Count == 2 && pair[0] is JsonValue pv && CheckCue.Ops.Contains(pv.ToString()))
                                where_.Add((f, pv.ToString(), pair[1]?.DeepClone()));
                            else where_.Add((f, "==", c?.DeepClone()));
                        break;
                    default: problems.Add($"{where}: unknown key '{k}'"); break;
                }
            }
            // a take anchor's parser checks the take keys; "field" is the verifier's own, so it is checked here
            var anchorProblems = new List<string>();
            var parsed = TakeJson.ParseAnchor(spec, where, anchorProblems);
            problems.AddRange(anchorProblems.Where(p => on != "field" || !p.Contains("is not one of", StringComparison.Ordinal)));
            parsed = parsed with { On = on };
            if (on == "field")
            {
                if (string.IsNullOrWhiteSpace(path)) problems.Add($"{where}: a field cue needs path (Component.Field)");
                if (parsed.Who == null) problems.Add($"{where}: a field cue needs who");
            }
            if (!CheckCue.Ops.Contains(op)) problems.Add($"{where}: op '{op}' is not one of {string.Join(" ", CheckCue.Ops)}");
            if (quantifier is not ("any" or "all") && !(quantifier.StartsWith("atLeast:", StringComparison.Ordinal)
                && int.TryParse(quantifier[8..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                problems.Add($"{where}: quantifier is any, all or atLeast:n");
            if (where_.Count > 0 && on is not ("shot" or "blast")) problems.Add($"{where}: where filters a shot or a blast row");
            if (hit != null && on != "shot") problems.Add($"{where}: hit belongs to a shot cue");
            return new CheckCue { Spec = parsed, Where = where_, Hit = hit, Path = path, Op = op, Value = value, Quantifier = quantifier };
        }
    }
}
