using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ <c>CE-4154</c> / SR-71 — the World Environment is SAVED IN THE HEADER (<c>Header.World</c>), not as an entity: one entry per component, by the
    /// SAME auto-serializer, and the entity is skipped on save and created from the header on load (no section ⇒ the C-3 defaults). At RUNTIME it stays an
    /// entity (Weather W-0). Precedent for a non-entity file section: <c>TerrainObjectsSection</c>.
    /// <para>⭐ The clock's FILE form is ONE ISO-8601 string with its offset — <c>"ScenarioClock": { "Start": "2025-06-21T08:00:00+02:00" }</c> — carrying
    /// <c>StartUtcTicks</c> (the UTC instant) and <c>DisplayUtcOffsetMinutes</c> (the offset). Only the FILE form changes: the runtime component and the wire do not.
    /// ⛔ A string with no offset is ambiguous and is refused loudly (<c>Z</c> is accepted, = offset 0).</para>
    /// 📄 docs/DESIGN_Scenario_Rules.md SR-71.
    /// </summary>
    public static class ScenarioWorldSection
    {
        public const string SectionName = "World";
        private const string ClockKey = "ScenarioClock";

        /// <summary>The file key of the entity a load builds from the section: fixed, so the entity's stable identity repeats across saves.</summary>
        public const string EntityKey = "00000000-0000-4000-8000-000000008810";

        // components that are the ENTITY's identity, not world state: the load re-creates them
        private static readonly string[] IdentityComponents = { "TkbIdentity", "NetworkIdentity" };

        private static readonly Regex IsoWithOffset = new(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant);

        // ── the clock's file form ───────────────────────────────────────────

        /// <summary>The ISO-8601 string (with offset) of a clock.</summary>
        public static string ClockToString(long startUtcTicks, short offsetMinutes)
            => new DateTimeOffset(new DateTime(startUtcTicks, DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(offsetMinutes))
                .ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

        /// <summary>The clock a string carries; THROWS <see cref="FormatException"/> on a string without an offset or one that is not ISO-8601.</summary>
        public static (long StartUtcTicks, short OffsetMinutes) ClockFromString(string text)
        {
            if (text == null || !IsoWithOffset.IsMatch(text.Trim()))
                throw new FormatException($"ScenarioClock.Start '{text}' is not an ISO-8601 date-time WITH an offset (e.g. 2025-06-21T08:00:00+02:00, or Z) — a local time without one is ambiguous (CE-4154).");
            if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                throw new FormatException($"ScenarioClock.Start '{text}' is not a valid date-time (CE-4154).");
            double minutes = at.Offset.TotalMinutes;
            if (minutes != Math.Floor(minutes) || Math.Abs(minutes) > short.MaxValue)
                throw new FormatException($"ScenarioClock.Start '{text}': the offset must be whole minutes.");
            return (at.UtcDateTime.Ticks, (short)minutes);
        }

        // ── save ────────────────────────────────────────────────────────────

        /// <summary>
        /// Takes the World Environment entity (the one carrying a <c>ScenarioClock</c>) OUT of the saved <paramref name="entities"/> and returns its components as the
        /// header section (identity components dropped, the clock in its file form); null when the scenario has none.
        /// </summary>
        public static JsonObject? TakeFrom(JsonObject entities)
        {
            string? key = entities.Where(kv => kv.Value is JsonObject o && o.ContainsKey(ClockKey)).Select(kv => kv.Key).FirstOrDefault();
            if (key == null) return null;
            var entity = (JsonObject)entities[key]!;
            entities.Remove(key);
            var world = new JsonObject();
            foreach (var kv in entity.ToList())
            {
                if (IdentityComponents.Contains(kv.Key)) continue;
                var node = kv.Value?.DeepClone();
                if (kv.Key == ClockKey && node is JsonObject clock)
                    node = new JsonObject
                    {
                        ["Start"] = ClockToString(clock["StartUtcTicks"]?.GetValue<long>() ?? 0, (short)(clock["DisplayUtcOffsetMinutes"]?.GetValue<int>() ?? 0)),
                    };
                world[kv.Key] = node;
            }
            return world;
        }

        // ── load ────────────────────────────────────────────────────────────

        /// <summary>The <c>Header.World</c> of a scenario DOM (either case), or null.</summary>
        public static JsonObject? Of(JsonObject? dom)
        {
            var header = (dom?["Header"] ?? dom?["header"]) as JsonObject;
            return (header?[SectionName] ?? header?["world"]) as JsonObject;
        }

        /// <summary>
        /// A scenario JSON whose <c>Header.World</c> has been turned into the World Environment ENTITY the entity extractor already understands
        /// (<c>TkbIdentity</c> = World Environment, the components as stored, the clock back in its runtime form); the text unchanged when there is no section.
        /// THROWS <see cref="FormatException"/> on an offset-less clock — the load fails loudly.
        /// </summary>
        public static string InjectEntity(string json, long worldEnvironmentTkbType)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            JsonObject? dom;
            try { dom = JsonNode.Parse(json) as JsonObject; }
            catch (System.Text.Json.JsonException) { return json; }   // the loader reports a broken file
            if (Of(dom) is not { } world) return json;

            var entity = new JsonObject { ["TkbIdentity"] = new JsonObject { ["TkbType"] = worldEnvironmentTkbType } };
            foreach (var kv in world)
            {
                if (kv.Key == ClockKey && kv.Value is JsonObject clock)
                {
                    var (ticks, offset) = ClockFromString(clock["Start"]?.GetValue<string>() ?? throw new FormatException("ScenarioClock in Header.World has no Start (CE-4154)."));
                    entity[ClockKey] = new JsonObject { ["StartUtcTicks"] = ticks, ["DisplayUtcOffsetMinutes"] = offset };
                }
                else entity[kv.Key] = kv.Value?.DeepClone();
            }
            var entities = (dom!["Entities"] ?? dom["entities"]) as JsonObject;
            entities ??= new JsonObject();
            // FIRST: the header is the authority — a stray World Environment entity in an older or hand-made file is the one C-3 ignores.
            var ordered = new JsonObject { [EntityKey] = entity };
            foreach (var kv in entities.ToList()) { entities.Remove(kv.Key); ordered[kv.Key] = kv.Value; }
            if (dom.ContainsKey("Entities")) dom["Entities"] = ordered; else dom["entities"] = ordered;
            return dom.ToJsonString();
        }
    }
}
