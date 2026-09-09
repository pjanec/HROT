using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>Q50</c> OPTION A + <c>Q49</c> OPTION D — ONE PROJECTION, FROM PERSISTED DATA ONLY.</b>
/// 🔒 <b>User, <c>2026-08-22</c>:</b> <i>"i hoped the editor automatically adds the subtree's data, which
/// is likely the option A."</i>
///
/// <para>⭐⭐ <b>The refinement that made A and D the SAME change</b> *(and it is why this type exists
/// rather than an editor-side one)*: 📐 <b>every input is already in the DTO</b> —
/// <list type="bullet">
///   <item><c>SubtreeSyncBindings</c> *(<c>BehaviorTreeAssetDto:354</c>)* — which fields copy in/out;</item>
///   <item><c>BTreeSubtreePayloadDto.SubtreeAssetId</c> **and** <c>SubtreeName</c> *(<c>:231</c>)* — which
///   subtree, and its name;</item>
///   <item>the callee's <b>blackboard type</b> — the ONLY thing not in this file, supplied by a catalog
///   keyed on <c>AssetId</c>.</item>
/// </list>
/// ⇒ ⛔ <b>no editor involvement and NO ORDERING PROBLEM</b>: nothing has to run "after the catalog is
/// populated", because the projection reads a document, not a live object graph.</para>
///
/// <para>⭐⭐⭐ <b>What it produces, and why BOTH halves come from one walk</b> — 📌 ruling 9. They are
/// two views of the same fact *("this node syncs with that subtree")*, and two walks would be two places
/// to disagree about which nodes qualify:
/// <list type="number">
///   <item><see cref="Groups"/> — the Approach-B copy·tick·copy groups the orchestrator emits;</item>
///   <item><see cref="SliceFields"/> — the <c>{SubtreeName}_{DtoTypeName}</c> fields the MASTER
///   blackboard must DECLARE so those groups' <c>ref master.X</c> resolves.</item>
/// </list>
/// ⛔⛔ <b>That second half is <c>BP-342</c> gap ②, and it is why the first half could never ship</b>:
/// <c>BTreeOrchestratorEmitCore:165</c> writes <c>ref var subDto = ref master.{sliceField}</c>, and until
/// now <b>nothing declared that field</b> — the orchestrator referenced a member of a struct that did not
/// have it.</para>
///
/// <para>⭐ <b>The editor's <c>GetAutoAllocatedVariables()</c> is NOT the source of truth</b> — it stays
/// what it always was, the <b>byte-budget DISPLAY</b> *(<c>BlackboardAuthoringWindow:529</c>)*. ⚠ Its
/// <c>DEBT</c> read *"real type resolution requires catalog integration"*; ⇒ ⭐ that integration happens
/// HERE, where the representation is a type <b>NAME</b> and no CLR <c>Type</c> is needed — which is the
/// wall that kept it a <c>typeof(object)</c> placeholder.</para>
/// </summary>
public static class SubtreeSyncProjection
{
    /// <summary>⭐ What the catalog must answer for a called subtree. ⚠ <see langword="null"/> ⇒ the node
    /// is skipped — an unresolvable callee must not produce a half-formed group.</summary>
    /// <param name="assetId">The called subtree's asset id.</param>
    public delegate string? ResolveBlackboardTypeName(Guid assetId);

    /// <summary>⭐ One master-blackboard field the Approach-B body will write through.</summary>
    /// <param name="FieldName">The emitted field name, <c>{SubtreeName}_{DtoTypeName}</c>.</param>
    /// <param name="TypeId">Its CLR type full name — the callee's blackboard type.</param>
    public readonly struct SliceField
    {
        public SliceField(string fieldName, string typeId) { FieldName = fieldName; TypeId = typeId; }
        public string FieldName { get; }
        public string TypeId    { get; }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The single walk.</b> Returns the Approach-B groups and the slice fields they require.
    /// ⛔ A node is included only when <b>all</b> of: it has bindings · it is a Subtree node · its callee
    /// resolves. ⚠ Any missing piece skips that node <b>silently and completely</b> — ⭐ never a group
    /// without its field, which is precisely the state that produced non-compiling output.
    /// </summary>
    public static (IReadOnlyList<OrchestratorSyncGroup> Groups, IReadOnlyList<SliceField> SliceFields)
        Project(BehaviorTreeAssetDto dto, ResolveBlackboardTypeName resolveBlackboardTypeName)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        if (resolveBlackboardTypeName is null) throw new ArgumentNullException(nameof(resolveBlackboardTypeName));

        var groups = new List<OrchestratorSyncGroup>();
        var slices = new List<SliceField>();

        if (dto.SubtreeSyncBindings.Count == 0)
            return (groups, slices);

        // ⭐ Index the subtree nodes once — the bindings dictionary is keyed by node id STRING.
        var subtreeNodes = new Dictionary<Guid, BTreeSubtreePayloadDto>();
        foreach (var node in dto.Nodes)
            if (node is BTreeSubtreeNodeDto sub && sub.Subtree is { } payload)
                subtreeNodes[node.VisualId] = payload;

        foreach (var kv in dto.SubtreeSyncBindings)
        {
            if (!Guid.TryParse(kv.Key, out var nodeId))          continue;
            if (!subtreeNodes.TryGetValue(nodeId, out var payload)) continue;
            if (payload.SubtreeAssetId == Guid.Empty)            continue;

            string? bbTypeName = resolveBlackboardTypeName(payload.SubtreeAssetId);
            if (string.IsNullOrWhiteSpace(bbTypeName)) continue;

            // ⭐ THE SAME derivation the authoring panel and the reload recompute use (ruling 9).
            var (subtreeName, dtoTypeName, dtoTypeNs) =
                SubtreeSyncIdentity.Derive(payload.SubtreeName, bbTypeName!);

            var bindings = new List<OrchestratorSyncBinding>(kv.Value.Count);
            foreach (var b in kv.Value)
                bindings.Add(new OrchestratorSyncBinding(
                    b.FieldName, b.MasterVariableName, b.SyncIn, b.SyncOut));

            groups.Add(new OrchestratorSyncGroup(subtreeName, dtoTypeName, dtoTypeNs, bindings));

            // ⭐⭐ The field name is built the SAME way the emit core builds it — see SliceFieldName.
            slices.Add(new SliceField(SliceFieldName(subtreeName, dtoTypeName), bbTypeName!));
        }

        return (groups, slices);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The field name, in ONE place.</b> ⛔ <c>BTreeOrchestratorEmitCore</c> composes
    /// <c>{SubtreeName}_{DtoTypeName}</c> when it emits the write, and this composes it when it declares
    /// the field. ⚠ <b>They must agree exactly</b> — a one-character divergence is a build break with no
    /// obvious cause, so the string lives here and both sides call it.
    /// </summary>
    public static string SliceFieldName(string subtreeName, string dtoTypeName)
        => subtreeName + "_" + dtoTypeName;
}
