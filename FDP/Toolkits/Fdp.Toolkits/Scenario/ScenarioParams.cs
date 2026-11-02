using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3a ⑦ (docs/DESIGN_Scenario_Macros.md SR-41, SR-28, SR-149, SR-161) — THE one place a scenario-shaped file's params become
    /// values: the header's declared params' defaults ▸ the caller's args (a macro instance's, or a scenario load's), an arg the file does not
    /// declare refused, and the file's own <see cref="ParamBindings"/> (③) applied to its entity JSON. ⭐ ONE principle at every level: the
    /// macro expander and the scenario load both call this, then extract with <c>new ParamBind(source)</c> for the <c>"^name"</c> forms.
    /// </summary>
    public static class ScenarioParams
    {
        /// <summary>The bound params of a prepared file: the source to extract with, and the effective values as JSON (what a recording keeps).</summary>
        public sealed record Prepared(JsonParamSource Source, string EffectiveJson);

        /// <summary>Thrown when the args do not fit the file's declared params.</summary>
        public sealed class ArgsRefused : Exception { public ArgsRefused(string message) : base(message) { } }

        /// <summary>
        /// Reads <paramref name="header"/>'s params, overlays <paramref name="argsJson"/> (a JSON object, name → value; null = none), refuses an
        /// undeclared arg, and applies the file's <see cref="ParamBindings"/> to every entity in <paramref name="dom"/> IN PLACE.
        /// <paramref name="outside"/> resolves an arg's reference (SR-58); null = args hold none. <paramref name="here"/> names the file in errors.
        /// </summary>
        public static Prepared Prepare(JsonObject dom, ScenarioHeader? header, string? argsJson, LeafApplier? outside, string here)
        {
            var declared = (header?.Params ?? Array.Empty<ScenarioParamDecl>())
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
            var args = ParseArgs(argsJson, here);
            foreach (var name in args.Keys)
                if (!declared.ContainsKey(name))
                    throw new ArgsRefused($"{here}: arg '{name}' is not a param (declared: {string.Join(", ", declared.Keys)})");

            var values = JsonParamSource.Overlay(declared.Values.Select(p => new KeyValuePair<string, JsonNode?>(p.Name, p.Default)), args);
            var source = new JsonParamSource(values, outside, here);

            if ((dom["entities"] ?? dom["Entities"]) is JsonObject entities)
                foreach (var (_, e) in entities)
                    if (e is JsonObject entity) ParamBinder.ApplyBindings(entity, source);

            var effective = new JsonObject();
            foreach (var (k, v) in values) effective[k] = v?.DeepClone();
            return new Prepared(source, effective.ToJsonString());
        }

        /// <summary><c>true</c> when the file declares params or the caller passed args — otherwise nothing is bound (today's load, unchanged).</summary>
        public static bool Applies(ScenarioHeader? header, string? argsJson)
            => header?.Params is { Count: > 0 } || !string.IsNullOrWhiteSpace(argsJson);

        private static Dictionary<string, JsonNode?> ParseArgs(string? argsJson, string here)
        {
            var d = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(argsJson)) return d;
            JsonNode? node;
            try { node = JsonNode.Parse(argsJson); }
            catch (JsonException ex) { throw new ArgsRefused($"{here}: the args are not JSON ({ex.Message})"); }
            if (node is not JsonObject o) throw new ArgsRefused($"{here}: the args are not a JSON object");
            foreach (var (k, v) in o) d[k] = v?.DeepClone();
            return d;
        }
    }
}
