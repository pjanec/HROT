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
///   <item><b>I4</b> globals have one source — <c>TerrainName</c> cluster-wide identical. ⛔ <c>R-4018</c>: a
///     slice's legacy <c>Header.TkbName</c> is IGNORED — a scenario never names a TKB — and is never written.
///     ⚠ I4 also covered a brain-only <c>Zones</c> SECTION; that section is retired (F2) — a zone is an
///     ordinary authored entity and merges through the <c>Entities</c> union like any other.</item>
/// </list>
/// ⇒ the merge is <c>{ $meta, Header, ⋃ Entities, ⋃ TerrainObjects }</c> (5e: a door's state is saved by its owner only). It NEVER remaps ids or fixes references, and it
/// <b>fails loud</b> rather than silently corrupt — a schema/TerrainName mismatch and a key collision
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
    /// <exception cref="InvalidOperationException">a fail-loud guard tripped (schema or TerrainName
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
        string?  terrainName   = null;
        bool     terrainSeen   = false;
        string?  description   = null;   // ⭐ CE-3288 — the Brain's slice carries it (§4d)

        // ── Entities (I1+I2): disjoint union ─────────────────────────────────────────────
        var mergedEntities = new JsonObject();
        // ── ⭐ Buildings 5e — TerrainObjects: disjoint union too (a door's state is written only by its owner, I1) ──
        var mergedTerrainObjects = new JsonObject();

        foreach (var s in compatible)
        {
            var dom = s.Dom!;

            // schemaVersion must agree across compatible slices.
            var meta = JsonEnvelope.Read(dom);
            if (schemaVersion is null) schemaVersion = meta.SchemaVersion;
            else if (schemaVersion.Value != meta.SchemaVersion)
                throw new InvalidOperationException(
                    $"schemaVersion mismatch across slices ({schemaVersion} vs {meta.SchemaVersion}, node {s.OriginNodeId}) — a version skew needs migration, not a blind union.");

            // ⭐ TerrainName must agree across compatible slices: terrain is a
            //   GLOBAL fact about the exercise, in the same class as $meta. Two nodes
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

            // ⭐ CE-3288 (§4d) — the scenario's description: only the Brain loads and saves the scenario as a whole, so normally ONE
            //   slice carries it. ⚠ Two different texts are a WARNING, not a failure — unlike the terrain (I4) they do not make two exercises.
            if (ScenarioDescription.OfDom(dom) is { } text)
            {
                if (description == null) description = text;
                else if (!string.Equals(description, text, StringComparison.Ordinal))
                    Fdp.Core.Logging.FdpLog<ScenarioSlice>.Warn(
                        "[ScenarioMerge] Header.Description differs on node {0}; the first slice's is kept.", s.OriginNodeId);
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

            if (Fdp.Toolkit.Terrain.TerrainObjectsSection.Of(dom) is { } objects)
            {
                foreach (var kv in objects)
                {
                    if (mergedTerrainObjects.ContainsKey(kv.Key))
                        throw new InvalidOperationException(
                            $"Terrain object '{kv.Key}' appears in two slices (node {s.OriginNodeId}) — a door's state is saved only by its owner; refusing to pick one.");
                    mergedTerrainObjects[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }

        // ── Assemble: same shape/order as a single-node save (ScenarioSerializer + ScenarioSaveCore) ──
        var canonical = new JsonObject { ["Entities"] = mergedEntities };
        // ⛔⛔ R-4018 — the header carries the terrain name only; a legacy slice's TkbName is not carried over.
        if (terrainName is not null || description is not null)
        {
            var header = new JsonObject();
            if (terrainName is not null) header["TerrainName"] = JsonValue.Create(terrainName);
            if (description is not null) header["Description"] = JsonValue.Create(description);   // ⭐ CE-3288
            canonical["Header"] = header;
        }
        if (mergedTerrainObjects.Count > 0) canonical[Fdp.Toolkit.Terrain.TerrainObjectsSection.Name] = mergedTerrainObjects;
        JsonEnvelope.Write(canonical, new DocumentMeta(canonicalDocType, schemaVersion!.Value));

        return new ScenarioMergeResult(canonical, foreign);
    }

}
