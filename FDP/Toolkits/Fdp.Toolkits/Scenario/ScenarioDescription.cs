using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ <c>CE-3288</c> — the loaded scenario's description, held by the world that loaded it (a managed singleton), so a save
    /// writes it back. The terrain name's route (<c>CE-3015</c>) with the terrain taken out: a scenario may have no terrain.
    /// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4d.
    /// <para>⚠ NoScenario | NoReplay, as <c>TerrainDefinition</c>: the text rides the HEADER, never the entity save, and a recording does
    /// not need it every frame. The policy applies only through <c>RegisterManagedComponent</c> (<c>HrotSharedComponentRegistry</c>).</para>
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.ScenarioDescriptionResident)]
    public sealed class ScenarioDescriptionResident
    {
        /// <summary>The header's <c>Description</c> as loaded; empty when the scenario has none.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// ⭐ <c>CE-3288</c> — reads a scenario file's <c>Header.Description</c>, stamps it resident at load, and hands it back to a
    /// save. ⭐ One string, by convention <i>"What: … · Judge: …"</i> — what the scenario shows and how to tell that it worked.
    /// </summary>
    public static class ScenarioDescription
    {
        /// <summary>The <c>Header.Description</c> of a scenario JSON, either case (<c>Header</c>/<c>header</c>,
        /// <c>Description</c>/<c>description</c> — the shipped files are camelCase, a save writes PascalCase); null when absent,
        /// blank, or the JSON is not an object.</summary>
        public static string? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return OfDom(JsonNode.Parse(json) as JsonObject);
            }
            catch (JsonException)
            {
                return null;   // ⭐ the loader reports a broken file; the description is not the place to fail it
            }
        }

        /// <summary>The <c>Header.Description</c> of a parsed scenario DOM (see <see cref="Read"/>).</summary>
        public static string? OfDom(JsonObject? dom)
        {
            if (dom == null) return null;
            var header = (dom["Header"] ?? dom["header"]) as JsonObject;
            var node = header?["Description"] ?? header?["description"];
            if (node is not JsonValue v || !v.TryGetValue(out string? text)) return null;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>The description resident in <paramref name="world"/> — what a save writes — or null when none is.</summary>
        public static string? ResidentOf(EntityRepository? world)
        {
            if (world == null || !world.HasSingletonManaged<ScenarioDescriptionResident>()) return null;
            var held = world.GetSingletonManaged<ScenarioDescriptionResident>();
            return string.IsNullOrEmpty(held?.Text) ? null : held!.Text;
        }

        /// <summary>Makes <paramref name="text"/> the loaded scenario's description in <paramref name="world"/>; null clears it
        /// (a new scenario, or one without a description, must not keep the previous one's).</summary>
        public static void Stamp(EntityRepository world, string? text)
        {
            if (world == null) return;
            if (string.IsNullOrWhiteSpace(text) && !world.HasSingletonManaged<ScenarioDescriptionResident>()) return;
            world.SetSingletonManaged(new ScenarioDescriptionResident { Text = text ?? string.Empty });
        }
    }
}
