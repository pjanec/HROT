using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Fdp.Toolkit.Scenario;

/// <summary>One node's contribution to a distributed save. <paramref name="Dom"/> is the parsed
/// scenario DOM for a FORMAT-COMPATIBLE slice; it is <c>null</c> for a foreign slice (foreign files
/// are routed by tag, never parsed — see <see cref="ScenarioMergeCore"/>).</summary>
public sealed record ScenarioSlice(int OriginNodeId, string DocType, JsonObject? Dom);

/// <summary>A pulled slice whose <c>$meta.docType</c> is not ours; kept verbatim and pushed back to
/// its origin node on load (§4c). The merge never opens it.</summary>
public sealed record ForeignSlice(int OriginNodeId, string DocType);

/// <summary>Result of a distributed-save merge: the ONE brain-canonical DOM (or <c>null</c> if every
/// slice was foreign) plus the set of foreign slices to keep separate.</summary>
public sealed record ScenarioMergeResult(JsonObject? CanonicalDom, IReadOnlyList<ForeignSlice> Foreign);

/// <summary>
/// ⭐⭐⭐ <b>CE-277(c2) — the distributed-save merge. Host-neutral, pure JSON (no ECS, no engine),
/// beside <see cref="ScenarioSaveCore"/>.</b>
///
/// <para>Combines the per-node scenario slices pulled to the NAS into ONE brain-canonical
/// <c>scenario.json</c> — "as if the brain owned all and saved" (R-A, user ruling <c>2026-09-14</c>) —
/// and sets the format-incompatible slices aside for push-back to their origin nodes.</para>
///
/// <para>⭐ <b>Why this is a disjoint dict-union, not a reconciliation</b> (the four invariants proven in
/// 📄 <c>docs/DESIGN_Distributed_Scenario_Persistence.md</c> §4b):
/// <list type="bullet">
///   <item><b>I1</b> one entity → one slice (the save gate keys on the single primary owner);</item>
///   <item><b>I2</b> entity keys never collide — the DOM key is a fresh <c>Guid.NewGuid()</c> per save;</item>
///   <item><b>I3</b> references stay intra-slice (parts ride the parent's owner) ⇒ no renumbering;</item>
///   <item><b>I4</b> globals have one source — <c>TkbName</c> and <c>TerrainName</c> cluster-wide identical.
///     ⚠ I4 also covered a brain-only <c>Zones</c> SECTION; that section is retired (F2) — a zone is an
///     ordinary authored entity and merges through the <c>Entities</c> union like any other.</item>
/// </list>
/// ⇒ the merge is <c>{ $meta, Header, ⋃ Entities }</c>. It NEVER remaps ids or fixes references, and it
/// <b>fails loud</b> rather than silently corrupt — a schema/TkbName/TerrainName mismatch and a key collision
/// (I2 impossible) each throw.</para>
///
/// <para>⛔ Foreign slices (<c>$meta.docType</c> ≠ ours) are identified by their MANIFEST-reported tag and are
/// never parsed — their <see cref="ScenarioSlice.Dom"/> is <c>null</c> here (§4c: ExCon writes an
/// intentionally-incompatible slice).</para>
/// </summary>
public static class ScenarioMergeCore
{
    /// <summary>
    /// Merges the compatible slices into one canonical DOM and lists the foreign ones.
    /// </summary>
    /// <param name="slices">Per-node contributions. A slice whose <see cref="ScenarioSlice.DocType"/> equals
    ///   <paramref name="canonicalDocType"/> MUST carry a non-null <see cref="ScenarioSlice.Dom"/>.</param>
    /// <param name="canonicalDocType">Our scenario format tag, e.g. <c>"Hrot.Scenario"</c>.</param>
    /// <exception cref="ArgumentException">a compatible slice has a null Dom.</exception>
    /// <exception cref="InvalidOperationException">a fail-loud guard tripped (schema, TkbName or TerrainName
    ///   mismatch, or a GUID key collision).</exception>
    public static ScenarioMergeResult Merge(IReadOnlyList<ScenarioSlice> slices, string canonicalDocType)
    {
        ArgumentNullException.ThrowIfNull(slices);
        if (string.IsNullOrWhiteSpace(canonicalDocType))
            throw new ArgumentException("canonicalDocType is required.", nameof(canonicalDocType));

        var compatible = new List<ScenarioSlice>();
        var foreign    = new List<ForeignSlice>();

        foreach (var s in slices)
        {
            if (string.Equals(s.DocType, canonicalDocType, StringComparison.Ordinal))
            {
                if (s.Dom is null)
                    throw new ArgumentException(
                        $"Compatible slice from node {s.OriginNodeId} ({s.DocType}) has a null Dom.", nameof(slices));
                compatible.Add(s);
            }
            else
            {
                // ⛔ Never parsed — routed by tag (§4c).
                foreign.Add(new ForeignSlice(s.OriginNodeId, s.DocType));
            }
        }

        if (compatible.Count == 0)
            return new ScenarioMergeResult(CanonicalDom: null, Foreign: foreign);

        // ── Globals (I4): one authoritative source, asserted ────────────────────────────
        int?     schemaVersion = null;
        string?  tkbName       = null;
        bool     tkbSeen       = false;
        string?  terrainName   = null;
        bool     terrainSeen   = false;

        // ── Entities (I1+I2): disjoint union ─────────────────────────────────────────────
        var mergedEntities = new JsonObject();

        foreach (var s in compatible)
        {
            var dom = s.Dom!;

            // schemaVersion must agree across compatible slices.
            var meta = JsonEnvelope.Read(dom);
            if (schemaVersion is null) schemaVersion = meta.SchemaVersion;
            else if (schemaVersion.Value != meta.SchemaVersion)
                throw new InvalidOperationException(
                    $"schemaVersion mismatch across slices ({schemaVersion} vs {meta.SchemaVersion}, node {s.OriginNodeId}) — a version skew needs migration, not a blind union.");

            // TkbName must agree across compatible slices.
            if (dom["Header"] is JsonObject header && header["TkbName"] is JsonNode tkbNode)
            {
                var v = tkbNode.GetValue<string>();
                if (!tkbSeen) { tkbName = v; tkbSeen = true; }
                else if (!string.Equals(tkbName, v, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Header.TkbName mismatch across slices ('{tkbName}' vs '{v}', node {s.OriginNodeId}) — a cluster runs ONE TKB.");
            }

            // ⭐ TerrainName must agree too, for the same reason and by the same rule: terrain is a
            //   GLOBAL fact about the exercise, in the same class as $meta and Header.TkbName. Two nodes
            //   disagreeing about which terrain they are on is not a merge to reconcile — it is a
            //   misconfiguration, and it must fail loudly rather than silently pick one.
            if (dom["Header"] is JsonObject th && th["TerrainName"] is JsonNode terrainNode)
            {
                var v = terrainNode.GetValue<string>();
                if (!terrainSeen) { terrainName = v; terrainSeen = true; }
                else if (!string.Equals(terrainName, v, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Header.TerrainName mismatch across slices ('{terrainName}' vs '{v}', node {s.OriginNodeId}) — a cluster runs ONE terrain.");
            }

            // ⛔⛔ F2 — THE `Zones` ARM IS GONE, and its absence is the point.
            //
            //   I4 said "Zones are brain-only", so the merge asserted that at most one slice carried a
            //   `Zones` section. That guard existed because zones were an EMBEDDED SECTION — a second
            //   place the truth could live, which is exactly the two-producers-for-one-slot hazard.
            //   A zone is now an ordinary authored ENTITY, so it merges through the `Entities` union
            //   above with every other entity, and the GUID-collision guard there covers it.
            //
            //   ⚠ Nothing replaces this check, and nothing needs to: the failure it caught cannot be
            //   constructed any more. Two brains authoring the same zone would now collide on the
            //   entity GUID and fail there, with a better message.
            //   📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6 (retirement).

            // Entities: disjoint union, guarded against the (I2-impossible) key collision.
            if (dom["Entities"] is JsonObject ents)
            {
                foreach (var kv in ents)
                {
                    if (mergedEntities.ContainsKey(kv.Key))
                        throw new InvalidOperationException(
                            $"Entity GUID '{kv.Key}' appears in two slices (node {s.OriginNodeId}) — I2 says this is impossible; the save is broken, refusing to overwrite.");
                    mergedEntities[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }

        // ── Assemble: same shape/order as a single-node save (ScenarioSerializer + ScenarioSaveCore) ──
        var canonical = new JsonObject { ["Entities"] = mergedEntities };
        // ⚠ Each header field is independently optional — rebuilding the node from TkbName alone would
        //   silently DROP the terrain name from every merged save.
        if (tkbName is not null || terrainName is not null)
        {
            var headerNode = new JsonObject();
            if (tkbName is not null)     headerNode["TkbName"]     = JsonValue.Create(tkbName);
            if (terrainName is not null) headerNode["TerrainName"] = JsonValue.Create(terrainName);
            canonical["Header"] = headerNode;
        }
        JsonEnvelope.Write(canonical, new DocumentMeta(canonicalDocType, schemaVersion!.Value));

        return new ScenarioMergeResult(canonical, foreign);
    }

}
