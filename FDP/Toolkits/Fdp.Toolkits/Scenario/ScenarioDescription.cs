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

        /// <summary>⭐ <c>CE-4058</c> ② — the loaded scenario's NAME (its id); empty for a new scenario.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>⭐ <c>CE-4058</c> ② — the hash of the scenario as loaded (<see cref="ScenarioDescription.HashOf"/>); empty when none.</summary>
        public string Hash { get; set; } = string.Empty;

        /// <summary>⭐ Macros S3a ⑦ (SR-176) — the scenario args the load APPLIED (a JSON object text: the header's defaults ▸ the run's args);
        /// empty when the scenario declares no params. A recording keeps it (<c>RecordingMetadata.ScenarioArgs</c>).</summary>
        public string Args { get; set; } = string.Empty;

        /// <summary>⭐ CE-4210 MA-4 — the loaded file's MACRO header fields (<c>Kind</c>, <c>Params</c>, <c>Anchor</c>, <c>Placement.MaxShift</c>, <c>Relocatable</c>) as the JSON
        /// OBJECT <see cref="ScenarioHeaderJson.Write"/> produces for them; empty = none. ONE flat string (no managed list on the resident); a save reads it back
        /// (<see cref="ScenarioDescription.HeaderOf"/>), so opening a macro and saving it no longer turns it into a plain scenario. A later edit REPLACES the whole
        /// resident — never a field mutated in place.</summary>
        public string HeaderJson { get; set; } = string.Empty;
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

        /// <summary>Makes <paramref name="text"/> the loaded scenario's description in <paramref name="world"/>, with its
        /// <paramref name="name"/> and <paramref name="hash"/> (CE-4058 ②); null clears each (a new scenario, or one without a
        /// description, must not keep the previous one's).</summary>
        public static void Stamp(EntityRepository world, string? text, string? name = null, string? hash = null, string? args = null, string? headerJson = null)
        {
            if (world == null) return;
            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(headerJson) && !world.HasSingletonManaged<ScenarioDescriptionResident>()) return;
            world.SetSingletonManaged(new ScenarioDescriptionResident
            {
                Text = text ?? string.Empty, Name = name ?? string.Empty, Hash = hash ?? string.Empty, Args = args ?? string.Empty,
                HeaderJson = headerJson ?? string.Empty,
            });
        }

        /// <summary>⭐ CE-4210 MA-4 — the macro fields of a header as the resident's string (<see cref="ScenarioDescriptionResident.HeaderJson"/>): exactly the object
        /// <see cref="ScenarioHeaderJson.Write"/> produces; empty when the header has none (a plain scenario).</summary>
        public static string HeaderJsonOf(ScenarioHeader? header)
        {
            if (header == null || !ScenarioHeaderJson.HasAny(header)) return string.Empty;
            var node = new JsonObject();
            ScenarioHeaderJson.Write(node, header);
            return node.ToJsonString();
        }

        /// <summary>⭐ CE-4210 MA-4 — the macro fields of a scenario JSON's <c>Header</c> (see <see cref="HeaderJsonOf(ScenarioHeader?)"/>); empty when the JSON is not an object or has none.</summary>
        public static string HeaderJsonOf(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            try { return HeaderJsonOf(ScenarioHeaderJson.Read(JsonNode.Parse(json) as JsonObject)); }
            catch (JsonException) { return string.Empty; }
        }

        /// <summary>⭐ CE-4210 MA-4 — the macro fields resident in <paramref name="world"/> (what a save writes back), as a <see cref="ScenarioHeader"/> whose
        /// <see cref="ScenarioHeader.SubsystemType"/> is empty and whose terrain and description are null (the save fills those); null when none are.</summary>
        public static ScenarioHeader? HeaderOf(EntityRepository? world)
        {
            if (world == null || !world.HasSingletonManaged<ScenarioDescriptionResident>()) return null;
            var json = world.GetSingletonManaged<ScenarioDescriptionResident>()?.HeaderJson;
            if (string.IsNullOrEmpty(json)) return null;
            try { return ScenarioHeaderJson.Read(new JsonObject { ["Header"] = JsonNode.Parse(json) }); }
            catch (JsonException) { return null; }
        }

        /// <summary>⭐ Macros S3a ⑦ (SR-176) — the scenario args the resident load applied, or null.</summary>
        public static string? ArgsOf(EntityRepository? world)
        {
            if (world == null || !world.HasSingletonManaged<ScenarioDescriptionResident>()) return null;
            var held = world.GetSingletonManaged<ScenarioDescriptionResident>();
            return string.IsNullOrEmpty(held?.Args) ? null : held!.Args;
        }

        /// <summary>⭐ <c>CE-4058</c> ② — the loaded scenario's name and hash in <paramref name="world"/>, or nulls.</summary>
        public static (string? Name, string? Hash) IdentityOf(EntityRepository? world)
        {
            if (world == null || !world.HasSingletonManaged<ScenarioDescriptionResident>()) return (null, null);
            var held = world.GetSingletonManaged<ScenarioDescriptionResident>();
            return (string.IsNullOrEmpty(held?.Name) ? null : held!.Name, string.IsNullOrEmpty(held?.Hash) ? null : held!.Hash);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4058</c> ② — THE scenario hash: the first 16 hex digits (lower case) of the SHA-256 of the scenario's JSON text (UTF-8).
        /// ONE rule for every reader — a recording's meta, the director's takes (<c>TakeStore.ScenarioHash</c>) — so "the same scenario" means
        /// one thing. Empty for no text.
        /// </summary>
        public static string HashOf(string? json)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;
            byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
            return System.Convert.ToHexString(digest)[..16].ToLowerInvariant();
        }
    }
}
