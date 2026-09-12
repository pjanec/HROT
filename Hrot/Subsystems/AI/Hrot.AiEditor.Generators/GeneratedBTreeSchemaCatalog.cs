using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐⭐⭐ <b><c>Q49</c> OPTION D — WHAT A SIBLING <c>*.btree.json</c> DECLARES, at generation time.</b>
/// 📄 <c>Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md</c>, approved by the user
/// <c>2026-08-22</c>.
///
/// <para>⛔⛔ <b>Why a catalog and not a lookup.</b> A source generator <b>cannot load assets</b> — so
/// <c>Q49</c>'s option <b>C</b> *(the editor's catalog resolver)* has no counterpart here, and a
/// master tree cannot ask *"what blackboard type does the subtree I call declare?"* by any means the
/// editor uses. ⇒ ⭐ it reads the sibling's <b>JSON</b>, which is the same source of truth.</para>
///
/// <para>⭐⭐ <b>The precedent is shipped, and this follows it deliberately:</b>
/// <see cref="GeneratedBlueprintSchemaCatalog"/> collects the sibling <c>*.bp.json</c> AdditionalTexts
/// for exactly this reason *(<c>"Option A"</c>, <c>BTreeJsonGenerator:45</c>)*. ⚠ <b>And this generator
/// ALREADY receives every <c>*.btree.json</c></b> — they are its own input ⇒ ⭐ this is a <b>second
/// projection of texts already in hand</b>, not new plumbing.</para>
///
/// <para>⭐⭐⭐ <b>ONE difference from the blueprint precedent, and it is an improvement.</b>
/// ⛔ That catalog is a <b>SECOND, independent parser</b> of <c>*.bp.json</c> — its own header records
/// how that bit it: the corpus moved to a v2 shape and the catalog silently returned <b>zero</b>
/// parameters. ⚠ This one does <b>not</b> repeat that: it deserialises through
/// <c>BTreeJsonServices</c>, the <b>same</b> path the generator uses for the asset it is generating ⇒
/// 📌 ruling 9 — a schema change cannot desynchronise two readers, because there is one reader.</para>
/// </summary>
internal static class GeneratedBTreeSchemaCatalog
{
    /// <summary>⭐ What one sibling tree contributes: its identity and the blackboard type it declares.
    /// ⚠ Only what a CALLER needs — ⛔ deliberately not the whole DTO, which would invite reading things
    /// that belong to the callee's own generation pass.</summary>
    internal readonly struct Entry
    {
        internal Entry(string name, string blackboardTypeName)
        { Name = name; BlackboardTypeName = blackboardTypeName; }

        internal string Name               { get; }
        internal string BlackboardTypeName { get; }
    }

    /// <summary>
    /// ⭐ Parse every <c>*.btree.json</c> into <c>AssetId → Entry</c>.
    /// ⚠ Malformed or unmanaged files are <b>skipped, never thrown</b> — 📌 the same best-effort contract
    /// as the blueprint catalog: a broken asset is already reported by its own generation pass, and a
    /// caller must not fail because a sibling is mid-edit.
    /// </summary>
    internal static IReadOnlyDictionary<Guid, Entry> Parse(
        ImmutableArray<(string Path, string Text)> btreeJsonFiles)
    {
        var result = new Dictionary<Guid, Entry>();

        foreach (var file in btreeJsonFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Text)) continue;

            BehaviorTreeAssetDto? dto;
            try { dto = BTreeJsonServices.Deserialize(file.Text); }
            catch { continue; }

            if (dto is null || dto.AssetId == Guid.Empty) continue;

            // ⭐⭐⭐ THE ASSET-LEVEL BlackboardTypeName (:342), and the choice is load-bearing.
            //    ⛔ NOT dto.Blackboard.TypeName (:59) — that is the BLOCK's name, a different field.
            //    ⭐ Option C reads subAsset.BlackboardTypeName (IBlackboardManagedAsset →
            //      BehaviorTreeAsset:266, the asset-level one). ⇒ the two arms MUST read the same
            //      property or they would derive different identities for the same subtree, which is
            //      exactly the silent divergence SubtreeSyncIdentity exists to prevent.
            //    ⚠ A tree that declares no blackboard type is skipped — the caller's node is skipped
            //      with it, which is the honest outcome (never a half-formed group).
            string typeName = dto.BlackboardTypeName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName)) continue;

            result[dto.AssetId] = new Entry(dto.Name ?? string.Empty, typeName);
        }

        return result;
    }
}
