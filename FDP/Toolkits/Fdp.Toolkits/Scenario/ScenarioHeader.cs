using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// Immutable header block written as the first entry in every scenario JSON file.
    /// Used by each subsystem's Cluster handler to filter files intended for it before
    /// performing a full DOM parse.
    /// </summary>
    /// <param name="SubsystemType">
    /// Human-readable identifier of the subsystem that produced this file
    /// (e.g. <c>"Hrot.CGF"</c>, <c>"Hrot.SimHost"</c>). Written to <c>$meta.docType</c>
    /// in Phase 2 format.
    /// </param>
    /// <param name="TerrainName">
    /// Optional TERRAIN name required by this scenario, with an optional subfolder path
    /// (e.g. <c>"desert/kandahar"</c>). Null means no opinion, and such a scenario still loads.
    ///
    /// <para><b>⭐ A NAME, and nothing else.</b> Road networks, terrain DB, heightmaps and built-in
    /// buildings are internal data of the terrain asset and are declared in the terrain's own definition
    /// file — ⛔ never in the scenario: a name that resolves to an artifact, with the artifact's contents
    /// living in the artifact.</para>
    ///
    /// <para>⛔ Do NOT grow this into an identity-plus-asset-reference block. A prior design draft did
    /// and it was retracted: an asset list here would be a second place the truth can live, and it would
    /// re-create the embedded content bundle that zones-as-entities exists to retire.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①.
    /// </param>
    /// <remarks>⛔⛔ <b>No TKB name — <c>R-4018</c>.</b> A scenario never names a TKB: there is one TKB per
    /// deployment, synced from its NAS master copy (R-4018). An older file's <c>Header.TkbName</c> is ignored on read and never
    /// written back. 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §0.</remarks>
    /// <param name="Description">⭐ <c>CE-3288</c> — what the scenario shows and how to judge that it worked (one string, by
    /// convention <i>"What: … · Judge: …"</i>); null = none. Metadata, not an asset reference. 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4d.</param>
    public record ScenarioHeader(
        string SubsystemType,
        string? TerrainName = null,
        string? Description = null)
    {
        /// <summary>⭐ Macros S3a ② — <c>header.kind</c> of a macro file (SR-173 / §14.1 ①).</summary>
        public const string MacroKind = "macro";

        /// <summary>⭐ SR-182 — the default <see cref="MaxShift"/> in metres when the header gives none.</summary>
        public const float DefaultMaxShift = 30f;

        /// <summary><see cref="MacroKind"/> for a macro file; null for a scenario.</summary>
        public string? Kind { get; init; }

        /// <summary>⭐ SR-44 — the declared params (the <c>.params.json</c> row shape); null = none.</summary>
        public IReadOnlyList<ScenarioParamDecl>? Params { get; init; }

        /// <summary>⭐ SR-152 — where the macro's content is authored: the point and heading the instance's placement maps onto; null = none.</summary>
        public MacroAnchor? Anchor { get; init; }

        /// <summary>⭐ SR-182 — <c>"Placement": {"MaxShift": m}</c>, the farthest the expander may shift a relocated placement; null = <see cref="DefaultMaxShift"/>.</summary>
        public float? MaxShift { get; init; }

        /// <summary>⭐ SR-156 — <c>false</c> = the macro's content is pinned to its authored place (it is not relocated). Written only when false.</summary>
        public bool Relocatable { get; init; } = true;

        /// <summary><c>true</c> for a macro file.</summary>
        public bool IsMacro => string.Equals(Kind, MacroKind, StringComparison.OrdinalIgnoreCase);

        /// <summary><see cref="MaxShift"/> or its default.</summary>
        public float EffectiveMaxShift => MaxShift ?? DefaultMaxShift;
    }

    /// <summary>⭐ SR-44 — one declared param: its <paramref name="Name"/>, its <paramref name="TypeId"/> (e.g. <c>ForceId</c>, <c>float</c>), the
    /// <paramref name="Default"/> value (JSON; null = none) and a <paramref name="Comment"/>.</summary>
    public sealed record ScenarioParamDecl(string Name, string TypeId, JsonNode? Default = null, string? Comment = null);

    /// <summary>⭐ SR-152 — a macro's authoring anchor: the <paramref name="Position"/> and <paramref name="Heading"/> its content is authored around.</summary>
    public readonly record struct MacroAnchor(WorldPoint Position, Bearing Heading);

    /// <summary>
    /// ⭐ Macros S3a ② — the ONE place the macro-related header fields are written to and read from the <c>Header</c> JSON node (PascalCase,
    /// read either case). <see cref="ScenarioSerializer.Serialize"/> writes through <see cref="Write"/>; any reader of a file's header uses <see cref="Read"/>.
    /// </summary>
    public static class ScenarioHeaderJson
    {
        public static bool HasAny(ScenarioHeader h)
            => h.Kind != null || h.Params is { Count: > 0 } || h.Anchor != null || h.MaxShift != null || !h.Relocatable;

        public static void Write(JsonObject headerNode, ScenarioHeader h)
        {
            if (h.Kind != null) headerNode["Kind"] = JsonValue.Create(h.Kind);
            if (h.Params is { Count: > 0 })
            {
                var arr = new JsonArray();
                foreach (var p in h.Params)
                {
                    var row = new JsonObject { ["Name"] = p.Name, ["TypeId"] = p.TypeId };
                    if (p.Default != null) row["Default"] = p.Default.DeepClone();
                    if (!string.IsNullOrEmpty(p.Comment)) row["Comment"] = p.Comment;
                    arr.Add(row);
                }
                headerNode["Params"] = arr;
            }
            if (h.Anchor is { } a)
                headerNode["Anchor"] = new JsonObject
                {
                    ["Position"] = new JsonArray(a.Position.X, a.Position.Y, a.Position.Z),
                    ["Heading"] = a.Heading.Degrees,
                };
            if (h.MaxShift is { } m) headerNode["Placement"] = new JsonObject { ["MaxShift"] = m };
            if (!h.Relocatable) headerNode["Relocatable"] = false;
        }

        /// <summary>Reads a scenario DOM's <c>Header</c> (<c>Header</c>/<c>header</c>, either key case) into a <see cref="ScenarioHeader"/>; null when the DOM has no header.
        /// <see cref="ScenarioHeader.SubsystemType"/> comes from the header's own <c>SubsystemType</c> when it has one, else <paramref name="subsystemType"/>.</summary>
        public static ScenarioHeader? Read(JsonObject? dom, string subsystemType = "")
        {
            if (dom == null) return null;
            if ((dom["Header"] ?? dom["header"]) is not JsonObject h) return null;

            List<ScenarioParamDecl>? decls = null;
            if (Get(h, "Params") is JsonArray rows)
            {
                decls = new List<ScenarioParamDecl>(rows.Count);
                foreach (var r in rows)
                {
                    if (r is not JsonObject ro) continue;
                    var name = Str(ro, "Name");
                    if (string.IsNullOrEmpty(name)) continue;
                    decls.Add(new ScenarioParamDecl(name, Str(ro, "TypeId") ?? string.Empty, Get(ro, "Default")?.DeepClone(), Str(ro, "Comment")));
                }
            }

            MacroAnchor? anchor = null;
            if (Get(h, "Anchor") is JsonObject ao && Get(ao, "Position") is JsonArray pos && pos.Count == 3)
                anchor = new MacroAnchor(new WorldPoint(Num(pos[0]), Num(pos[1]), Num(pos[2])), new Bearing(Num(Get(ao, "Heading"))));

            float? maxShift = null;
            if (Get(h, "Placement") is JsonObject po && Get(po, "MaxShift") is JsonValue mv && mv.TryGetValue(out float m)) maxShift = m;

            bool relocatable = !(Get(h, "Relocatable") is JsonValue rv && rv.TryGetValue(out bool rb) && !rb);

            return new ScenarioHeader(Str(h, "SubsystemType") ?? subsystemType, Str(h, "TerrainName"), Str(h, "Description"))
            {
                Kind = Str(h, "Kind"),
                Params = decls,
                Anchor = anchor,
                MaxShift = maxShift,
                Relocatable = relocatable,
            };

            static string? Str(JsonObject o, string key) => Get(o, key) is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        }

        private static JsonNode? Get(JsonObject o, string key)
        {
            foreach (var kv in o)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        private static float Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out float f) ? f : 0f;
    }
}
