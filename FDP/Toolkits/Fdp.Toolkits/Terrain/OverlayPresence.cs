using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-11071</c> (docs/designs/engineering/DESIGN_Breachable_Obstacle_Walls.md §9c) — a scenario terrain FEATURE that stands only in some run variants (<c>"Presence"</c>, optionally
    /// <c>"PresenceWith"</c>: <see cref="ScenarioVariants"/>). The overlay is compiled ONCE at save, before any variant is chosen, so each of the feature's parts carries its chance and the key
    /// it rolls under (<see cref="Stamp"/>); a node loading the overlay for a run drops the parts the run's variant leaves out (<see cref="Keep"/>) — the SAME decision the scenario load makes
    /// for the entity (<see cref="ScenarioVariants.IsPresent"/>, scenario salt), from the variant every node reads, so every node agrees. Variant 0 (as authored) keeps everything.
    /// </summary>
    public static class OverlayPresence
    {
        /// <summary>The part property holding the feature's chance (0..1).</summary>
        public const string PresenceProperty = "presence";
        /// <summary>The part property holding the key the chance is rolled under (the feature's guid, or its <c>PresenceWith</c>).</summary>
        public const string KeyProperty = "presenceKey";

        /// <summary>Stamps every part of feature <paramref name="guid"/> (entity JSON <paramref name="entity"/>) with its <c>Presence</c> and roll key; a feature with no Presence is left alone.</summary>
        public static void Stamp(JsonObject entity, string guid, IReadOnlyList<JsonObject> parts)
        {
            if (entity[ScenarioVariants.PresenceKey] is not JsonValue pv || !pv.TryGetValue<double>(out double p) || p >= 1.0) return;
            string key = entity[ScenarioVariants.PresenceWithKey] is JsonValue wv && wv.TryGetValue<string>(out var w) && !string.IsNullOrEmpty(w) ? w : guid;
            foreach (var part in parts)
            {
                if (part["properties"] is not JsonObject props) continue;
                props[PresenceProperty] = p;
                props[KeyProperty] = key;
            }
        }

        /// <summary>True when the part with <paramref name="props"/> stands in <paramref name="variant"/> (a part with no chance always does).</summary>
        public static bool Keep(JsonObject? props, int variant)
        {
            if (variant == 0 || props == null || props[PresenceProperty] is not JsonValue pv || !pv.TryGetValue<double>(out double p)) return true;
            string key = (string?)props[KeyProperty] ?? "";
            return ScenarioVariants.IsPresent(variant, Guid.Empty, key, p);
        }

        /// <summary>A cheap pre-test on the overlay world's TEXT: false ⇒ no part carries a chance (nothing to filter, nothing variant-dependent).</summary>
        public static bool Mentions(string worldText) => worldText.Contains("\"" + KeyProperty + "\"", StringComparison.Ordinal);

        /// <summary>
        /// ⭐ The run-dependent part of an overlay's identity: the sorted roll keys of the features <paramref name="variant"/> leaves out of the overlay world at
        /// <paramref name="worldPath"/> — empty when nothing is left out (every overlay with no chances, and variant 0). A resident terrain is "unchanged" only for the same overlay hash AND this.
        /// </summary>
        public static string AbsentKeys(string? worldPath, int variant)
        {
            if (variant == 0 || string.IsNullOrEmpty(worldPath) || !System.IO.File.Exists(worldPath)) return "";
            string text = System.IO.File.ReadAllText(worldPath);
            if (!Mentions(text) || JsonNode.Parse(text) is not JsonObject doc || doc["features"] is not JsonArray features) return "";
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var f in features)
                if (f is JsonObject o && o["properties"] is JsonObject props && !Keep(props, variant)) keys.Add((string?)props[KeyProperty] ?? "");
            return string.Join(",", keys);
        }

        /// <summary>
        /// Removes from the overlay world document <paramref name="world"/> (a GeoJSON FeatureCollection) every part the variant leaves out; returns how many were removed. ⭐ Cold (a load).
        /// </summary>
        public static int Filter(JsonObject world, int variant)
        {
            if (variant == 0 || world["features"] is not JsonArray features) return 0;
            int removed = 0;
            for (int i = features.Count - 1; i >= 0; i--)
                if (features[i] is JsonObject f && !Keep(f["properties"] as JsonObject, variant)) { features.RemoveAt(i); removed++; }
            return removed;
        }
    }
}
