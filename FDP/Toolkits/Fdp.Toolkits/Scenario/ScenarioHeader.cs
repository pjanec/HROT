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

        /// <summary>⭐ P-7 / F-27 (DESIGN_Node_Features §11) — the scenario's feature PRESET: feature id → partial config JSON (<c>"on"</c> / <c>"off"</c> in the file are the short forms of <c>{"Enabled":true}</c> / <c>{"Enabled":false}</c>); null = none.</summary>
        public IReadOnlyDictionary<string, string>? Features { get; init; }

        /// <summary><see cref="MacroKind"/> for a macro file; null for a scenario.</summary>
        public string? Kind { get; init; }

        /// <summary>⭐ SR-44 — the declared params (the <c>.params.json</c> row shape); null = none.</summary>
        public IReadOnlyList<ScenarioParamDecl>? Params { get; init; }

        /// <summary>⭐ SR-152 — where the macro's content is authored: the point and heading the instance's placement maps onto; null = none.</summary>
        public MacroAnchor? Anchor { get; init; }

        /// <summary>⭐ SR-182 — <c>"Placement": {"MaxShift": m}</c>, the farthest the expander may shift a relocated placement; null = <see cref="DefaultMaxShift"/>.</summary>
        public float? MaxShift { get; init; }

        /// <summary>⭐ CE-3375 (📄 docs/UX/DESIGN_Scenario_Presets.md D1) — the scenario's named RUN SETUPS (<c>"Presets": [{"Name", "Variant", "Args"}]</c>), in order;
        /// <b>Default</b> is NOT stored (it is the declarations' defaults, as authored, always first). Null = none.</summary>
        public IReadOnlyList<ScenarioPreset>? Presets { get; init; }

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

    /// <summary>⭐ CE-3375 (presets D4) — how a preset picks the run's variant: <see cref="AsAuthored"/> (variant 0 — the world is editable and saveable, the
    /// pen) · <see cref="Random"/> (a new variant each run) · <see cref="Number"/> (that variant). Anything but as-authored is preview only (the eye).</summary>
    public enum PresetVariantKind { AsAuthored = 0, Random = 1, Number = 2 }

    /// <summary>⭐ CE-3375 — a preset's variant choice; <paramref name="Number"/> matters only for <see cref="PresetVariantKind.Number"/>.</summary>
    public readonly record struct PresetVariant(PresetVariantKind Kind, int Number = 0)
    {
        public static PresetVariant AsAuthored => new(PresetVariantKind.AsAuthored);
        public static PresetVariant RandomEachRun => new(PresetVariantKind.Random);

        /// <summary>True when a world loaded with this choice can be saved (variant 0 — presets §2).</summary>
        public bool Saveable => Kind == PresetVariantKind.AsAuthored || (Kind == PresetVariantKind.Number && Number == 0);

        /// <summary>The variant a load uses: 0 · <paramref name="random"/> (a fresh non-zero number, the caller's) · the number.</summary>
        public int Resolve(Func<int> random) => Kind switch { PresetVariantKind.Random => random(), PresetVariantKind.Number => Number, _ => 0 };
    }

    /// <summary>
    /// ⭐⭐ CE-3375 (📄 docs/UX/DESIGN_Scenario_Presets.md §1, D1) — a NAMED RUN SETUP kept in the scenario file: a variant choice and the params it CHANGES
    /// (<paramref name="Args"/>: name → value, only the overrides — a param not listed takes its declared default). What <c>RunSetup</c> holds, with a name.
    /// </summary>
    public sealed record ScenarioPreset(string Name, PresetVariant Variant, JsonObject? Args = null)
    {
        /// <summary>The name of the implicit first preset (the declarations' defaults, as authored) — never stored.</summary>
        public const string DefaultName = "Default";

        /// <summary>The scenario args a load with this preset applies: its overrides plus <c>"$variant"</c> when not as authored (presets §3 — the load path every
        /// node already reads). <paramref name="random"/> draws a variant for <see cref="PresetVariantKind.Random"/>.</summary>
        public string ArgsJson(Func<int> random)
        {
            var o = Args?.DeepClone() as JsonObject ?? new JsonObject();
            int v = Variant.Resolve(random);
            if (v != 0) o[ScenarioVariants.ArgName] = v;
            return o.ToJsonString();
        }
    }

    /// <summary>⭐ SR-152 — a macro's authoring anchor: the <paramref name="Position"/> and <paramref name="Heading"/> its content is authored around.</summary>
    public readonly record struct MacroAnchor(WorldPoint Position, Bearing Heading);

    /// <summary>
    /// ⭐ Macros S3a ② — the ONE place the macro-related header fields are written to and read from the <c>Header</c> JSON node (PascalCase,
    /// read either case). <see cref="ScenarioSerializer.Serialize"/> writes through <see cref="Write"/>; any reader of a file's header uses <see cref="Read"/>.
    /// </summary>
    public static class ScenarioHeaderJson
    {
        public static bool HasAny(ScenarioHeader h)
            => h.Kind != null || h.Params is { Count: > 0 } || h.Anchor != null || h.MaxShift != null || !h.Relocatable || h.Features is { Count: > 0 } || h.Presets is { Count: > 0 };

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
            if (h.Features is { Count: > 0 })   // ⭐ P-7 F-27 — back to the short form where it is exactly {"Enabled": bool}
            {
                var f = new JsonObject();
                foreach (var kv in h.Features)
                {
                    JsonNode? v;
                    try { v = JsonNode.Parse(kv.Value); } catch (System.Text.Json.JsonException) { v = JsonValue.Create(kv.Value); }
                    if (v is JsonObject o && o.Count == 1 && o["Enabled"] is JsonValue ev && ev.TryGetValue(out bool en)) v = JsonValue.Create(en ? "on" : "off");
                    f[kv.Key] = v;
                }
                headerNode["Features"] = f;
            }
            if (h.Presets is { Count: > 0 })   // ⭐ CE-3375 — "Variant": "AsAuthored" | "Random" | n
            {
                var arr = new JsonArray();
                foreach (var p in h.Presets)
                {
                    var row = new JsonObject { ["Name"] = p.Name };
                    row["Variant"] = p.Variant.Kind switch
                    {
                        PresetVariantKind.Random => JsonValue.Create("Random"),
                        PresetVariantKind.Number => JsonValue.Create(p.Variant.Number),
                        _ => JsonValue.Create("AsAuthored"),
                    };
                    if (p.Args is { Count: > 0 }) row["Args"] = p.Args.DeepClone();
                    arr.Add(row);
                }
                headerNode["Presets"] = arr;
            }
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

            Dictionary<string, string>? features = null;
            if (Get(h, "Features") is JsonObject fo)
            {
                features = new Dictionary<string, string>(fo.Count, StringComparer.Ordinal);
                foreach (var kv in fo)
                {
                    if (kv.Value is JsonValue sv && sv.TryGetValue(out string? word))
                        features[kv.Key] = string.Equals(word, "on", StringComparison.OrdinalIgnoreCase) ? "{\"Enabled\":true}"
                                         : string.Equals(word, "off", StringComparison.OrdinalIgnoreCase) ? "{\"Enabled\":false}" : word ?? "{}";   // anything else is passed on and reported by the node (F-30)
                    else if (kv.Value is JsonObject fobj) features[kv.Key] = fobj.ToJsonString();
                }
            }

            List<ScenarioPreset>? presets = null;
            if (Get(h, "Presets") is JsonArray prows)
            {
                presets = new List<ScenarioPreset>(prows.Count);
                foreach (var r in prows)
                {
                    if (r is not JsonObject ro) continue;
                    var name = Str(ro, "Name");
                    if (string.IsNullOrWhiteSpace(name) || string.Equals(name, ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase)) continue;   // Default is implicit
                    var variant = Get(ro, "Variant") switch
                    {
                        JsonValue nv when nv.TryGetValue(out int n) => new PresetVariant(PresetVariantKind.Number, n),
                        JsonValue sv when sv.TryGetValue(out string? w) && string.Equals(w, "Random", StringComparison.OrdinalIgnoreCase) => PresetVariant.RandomEachRun,
                        _ => PresetVariant.AsAuthored,
                    };
                    presets.Add(new ScenarioPreset(name, variant, Get(ro, "Args") is JsonObject ao2 ? (JsonObject)ao2.DeepClone() : null));
                }
            }

            return new ScenarioHeader(Str(h, "SubsystemType") ?? subsystemType, Str(h, "TerrainName"), Str(h, "Description"))
            {
                Kind = Str(h, "Kind"),
                Params = decls,
                Anchor = anchor,
                MaxShift = maxShift,
                Relocatable = relocatable,
                Features = features,
                Presets = presets,
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
