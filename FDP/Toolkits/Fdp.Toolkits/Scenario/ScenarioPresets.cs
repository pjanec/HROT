using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ CE-3375 (📄 docs/UX/DESIGN_Scenario_Presets.md §1, §4) — the ONE resolver of a scenario's presets, for every surface (HTTP, the load dialog, the split ▶):
    /// the list IN ORDER with the implicit <b>Default</b> first, and a preset's load args by name. ⛔ No surface reads <c>header.presets</c> itself.
    /// </summary>
    public static class ScenarioPresets
    {
        /// <summary>The presets a header offers: Default (as authored, no args) first, then the stored ones in order.</summary>
        public static IReadOnlyList<ScenarioPreset> Of(ScenarioHeader? header)
        {
            var list = new List<ScenarioPreset> { new(ScenarioPreset.DefaultName, PresetVariant.AsAuthored) };
            if (header?.Presets is { } stored) list.AddRange(stored);
            return list;
        }

        /// <summary>The header of the scenario file at <paramref name="scenarioJsonPath"/>, or null (missing / unreadable).</summary>
        public static ScenarioHeader? HeaderOf(string scenarioJsonPath)
        {
            try { return File.Exists(scenarioJsonPath) && JsonNode.Parse(File.ReadAllText(scenarioJsonPath)) is JsonObject dom ? ScenarioHeaderJson.Read(dom) : null; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// The load args of preset <paramref name="name"/> (case-insensitive): null for Default; a preset's overrides plus <c>$variant</c>. False + <paramref name="error"/>
        /// naming the presets there are, when no such preset exists.
        /// </summary>
        public static bool TryArgs(ScenarioHeader? header, string name, Func<int> random, out string? argsJson, out string? error)
        {
            argsJson = null; error = null;
            var all = Of(header);
            foreach (var p in all)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    argsJson = string.Equals(p.Name, ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase) ? null : p.ArgsJson(random);
                    return true;
                }
            var names = new List<string>(all.Count);
            foreach (var p in all) names.Add(p.Name);
            error = "no preset '" + name + "' — this scenario has: " + string.Join(", ", names);
            return false;
        }

        /// <summary>A fresh non-zero variant for a "Random each run" preset.</summary>
        public static int RandomVariant() => Random.Shared.Next(1, 1_000_000);

        /// <summary>A preset as JSON for a surface: <c>{"name", "variant": "AsAuthored"|"Random"|n, "saveable", "changed": [param names]}</c>.</summary>
        public static JsonObject ToJson(ScenarioPreset p)
        {
            var changed = new JsonArray();
            if (p.Args != null) foreach (var (k, _) in p.Args) changed.Add(k);
            return new JsonObject
            {
                ["name"] = p.Name,
                ["variant"] = p.Variant.Kind switch { PresetVariantKind.Random => JsonValue.Create("Random"), PresetVariantKind.Number => JsonValue.Create(p.Variant.Number), _ => JsonValue.Create("AsAuthored") },
                ["saveable"] = p.Variant.Saveable,
                ["changed"] = changed,
            };
        }
    }
}
