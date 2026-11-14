using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario;

/// <summary>One node's contribution to a distributed save. <paramref name="Dom"/> is the parsed
/// scenario DOM for a FORMAT-COMPATIBLE slice; it is <c>null</c> for a foreign slice (foreign files
/// are routed by tag, never parsed — see <see cref="ScenarioMergeCore"/>).</summary>
public sealed record ScenarioSlice(NodeId OriginNodeId, string DocType, JsonObject? Dom);

/// <summary>A pulled slice whose <c>$meta.docType</c> is not ours; kept verbatim and pushed back to
/// its origin node on load (§4c). The merge never opens it.</summary>
public sealed record ForeignSlice(NodeId OriginNodeId, string DocType);

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
///   <item><b>I2</b> entity keys never collide — ⭐ CE-4153 (rev 3, restated): the DOM key is the entity's STABLE <c>EntityGuid</c> (minted ONCE, replicated by <c>dtEntityGuid</c>, never regenerated per save), and I1 puts each entity in exactly ONE slice, so the keys are disjoint; a key in two slices means a broken save or two entities sharing a GUID (a copy that kept it);</item>
///   <item><b>I3</b> references stay intra-slice (parts ride the parent's owner) ⇒ no renumbering;</item>
///   <item><b>I4</b> globals have one source — <c>TerrainName</c> cluster-wide identical. ⛔ <c>R-4018</c>: a
///     slice's legacy <c>Header.TkbName</c> is IGNORED — a scenario never names a TKB — and is never written.
///     ⚠ I4 also covered a brain-only <c>Zones</c> SECTION; that section is retired (F2) — a zone is an
///     ordinary authored entity and merges through the <c>Entities</c> union like any other.</item>
/// </list>
/// ⇒ ⭐ CE-4210 MA-4: the header also carries the MACRO fields (<c>Kind</c>, <c>Params</c>, <c>Anchor</c>, <c>Placement</c>, <c>Relocatable</c>) of the first slice that has any — before, a merged save dropped them and turned a macro into a plain scenario.
/// the merge is <c>{ $meta, Header, ⋃ Entities, ⋃ TerrainObjects }</c> (5e: a door's state is saved by its owner only). It NEVER remaps ids or fixes references, and it
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
        JsonObject? world = null;   // ⭐ CE-4154 — Header.World: ONE source (the Brain's slice, I4)
        ScenarioHeader? macroHeader = null;   // ⭐ CE-4210 MA-4 — the macro fields (Kind, Params, Anchor, Placement, Relocatable): the first slice that has any
        string? macroHeaderJson = null;

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

            // ⭐ CE-4154 / SR-71 — the World Environment section: globals have one source (I4); the entity has one owner, so a second slice carrying it is a broken save.
            if (ScenarioWorldSection.Of(dom) is { } sliceWorld)
            {
                if (world != null)
                    throw new InvalidOperationException($"Header.World appears in two slices (node {s.OriginNodeId}) — the World Environment is saved by its owner only (I4); refusing to pick one.");
                world = (JsonObject)sliceWorld.DeepClone();
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

            // ⭐ CE-4210 MA-4 — the macro header fields ride the slice that WROTE them (every node saves them from its resident, so normally every slice
            //   carries the same). The Description rule (CE-3288): the first slice that has any is kept; a different one is a WARNING, not a failure.
            if (ScenarioHeaderJson.Read(dom) is { } sliceHeader && ScenarioHeaderJson.HasAny(sliceHeader))
            {
                string sliceJson = ScenarioDescription.HeaderJsonOf(sliceHeader);
                if (macroHeader == null) { macroHeader = sliceHeader; macroHeaderJson = sliceJson; }
                else if (!string.Equals(macroHeaderJson, sliceJson, StringComparison.Ordinal))
                    Fdp.Core.Logging.FdpLog<ScenarioSlice>.Warn(
                        "[ScenarioMerge] the macro header fields (Kind/Params/Anchor/…) differ on node {0}; the first slice's are kept.", s.OriginNodeId);
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
                            $"Entity GUID '{kv.Key}' appears in two slices (node {s.OriginNodeId}) — I2 (stable EntityGuid, one slice per entity) says this is impossible; the save is broken or two entities share a GUID, refusing to overwrite.");
                    mergedEntities[kv.Key] = kv.Value?.DeepClone();
                }
            }

            if (Fdp.Toolkit.Terrain.TerrainObjectsSection.Of(dom) is { } objects)
            {
                foreach (var kv in objects)
                {
                    // ⭐ CE-4391 S-4 (B-2) — a building hit by two deciding nodes is saved by both: the union of breaches, collapsed OR-ed
                    if (mergedTerrainObjects[kv.Key] is JsonObject have && kv.Value is JsonObject mine
                        && Fdp.Toolkit.Terrain.TerrainObjectsSection.IsBuildingDamageEntry(have) && Fdp.Toolkit.Terrain.TerrainObjectsSection.IsBuildingDamageEntry(mine))
                    {
                        mergedTerrainObjects[kv.Key] = Fdp.Toolkit.Terrain.TerrainObjectsSection.MergeBuildingDamage(kv.Key, have, mine);
                        continue;
                    }
                    // ⭐ CE-4591 (P-4) — the craters entry: each slice saved the craters its node decided; the union
                    if (mergedTerrainObjects[kv.Key] is JsonObject haveG && kv.Value is JsonObject mineG
                        && Fdp.Toolkit.Terrain.TerrainObjectsSection.IsCraterEntry(haveG) && Fdp.Toolkit.Terrain.TerrainObjectsSection.IsCraterEntry(mineG))
                    {
                        mergedTerrainObjects[kv.Key] = Fdp.Toolkit.Terrain.TerrainObjectsSection.MergeCraters(haveG, mineG);
                        continue;
                    }
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
        if (terrainName is not null || description is not null || world is not null || macroHeader is not null)
        {
            var header = new JsonObject();
            if (terrainName is not null) header["TerrainName"] = JsonValue.Create(terrainName);
            if (description is not null) header["Description"] = JsonValue.Create(description);   // ⭐ CE-3288
            if (macroHeader is not null) ScenarioHeaderJson.Write(header, macroHeader);   // ⭐ CE-4210 MA-4 — the ONE writer of these fields
            if (world is not null) header[ScenarioWorldSection.SectionName] = world;   // ⭐ CE-4154
            canonical["Header"] = header;
        }
        if (mergedTerrainObjects.Count > 0) canonical[Fdp.Toolkit.Terrain.TerrainObjectsSection.Name] = mergedTerrainObjects;
        JsonEnvelope.Write(canonical, new DocumentMeta(canonicalDocType, schemaVersion!.Value));

        return new ScenarioMergeResult(canonical, foreign);
    }

}
