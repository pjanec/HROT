using System.Collections.Generic;
using System.IO;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.BTree.Editor.Emit;

/// <summary>
/// Emits a companion <c>{AssetName}.Orchestrators.g.cs</c> file for a BTree master asset that
/// has aliased sub-tree variable bindings.
/// One <c>[BTreeAction]</c> static method is generated per unique (variable, sub-tree) pair.
/// Returns <c>null</c> when the asset has no alias bindings — the caller should skip writing.
///
/// <para>⭐⭐⭐ <b>Batch 92 (<c>92c</c>) — this is now a THIN CALLER.</b> The emit body lives in
/// <see cref="BTreeOrchestratorEmitCore"/>, in the netstandard2.0 persistence assembly, so the
/// generator (<c>92b</c>) and this sidecar path emit from <b>one body</b> — 📌 ruling 9.</para>
///
/// <para>⭐⭐⭐ <b><c>Q49</c> (<c>2026-08-22</c>) — THE IDENTITY IS NO LONGER SESSION-LOCAL HERE.</b>
/// ⛔ <i>(was: "<c>_syncNodeMeta</c> is written only by a UI draw and is deliberately not persisted"
/// — true of the WRITE, and it meant this emitter produced nothing after a reload.)</i>
/// ⭐ <see cref="Emit"/> now takes a <b>required</b> sub-asset resolver and <b>recomputes</b> the
/// identity before reading the groups — 📌 <c>R-126</c>'s pull. Nothing is persisted; the DTO exclusion
/// rail stays correct. 📄 <c>Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md</c>.
/// ⚠ <b>This closes <c>BP-342</c> gap ① for the EDITOR arm only</b> — the generator arm is option D,
/// and <b>gap ②</b> *(the master blackboard does not declare the auto-allocated slice)* still stands.</para>
///
/// <para>⭐⭐ <b><c>WriteOrchestratorFile</c> STAYS</b> — the Category-1 hand-authored path
/// (<c>EditorSubsystem:3136</c>), and ⛔ deliberately unwired to anything new.</para>
/// </summary>
public static class BTreeOrchestratorEmitter
{
    /// <summary>
    /// Generates the orchestrator source text for <paramref name="asset"/>.
    /// Returns <c>null</c> when there is nothing to emit.
    /// </summary>
    /// <remarks>
    /// ⭐ Projects through the SAME <c>ToDto</c> the save path uses — ⛔ not a second projection.
    /// </remarks>
    /// <param name="resolveSubAsset">
    /// ⭐⭐⭐ <b><c>Q49</c> option C, as a PULL.</b> Answers <i>"what are this subtree asset's name and
    /// blackboard type?"</i> — the identity is <b>recomputed from it before the groups are read</b>.
    ///
    /// <para>⛔⛔ <b>REQUIRED, and that is the whole point</b> — 📌 <c>R-126</c>: <i>"no path can forget
    /// to raise what is never raised."</i> ⚠ The defect being fixed is precisely that the identity was
    /// written by <b>one optional caller</b> *(a UI draw)*; an optional parameter here would rebuild
    /// that failure mode one level up. ⇒ ⭐ a caller with no catalog passes <c>_ =&gt; null</c> and says
    /// so — an explicit *"I cannot resolve"*, not a silent default.</para>
    /// </param>
    public static string? Emit(
        BehaviorTreeAsset asset,
        System.Func<System.Guid, (string Name, string BlackboardTypeName)?> resolveSubAsset)
    {
        if (resolveSubAsset is null) throw new System.ArgumentNullException(nameof(resolveSubAsset));

        // ⭐⭐ RECOMPUTE FIRST, then read. ⛔ Reading first would emit the pre-reload (empty) identity,
        //    which is the bug. See BehaviorTreeAsset.RecomputeSubtreeSyncIdentity.
        asset.RecomputeSubtreeSyncIdentity(resolveSubAsset);

        return BTreeOrchestratorEmitCore.Emit(
            BehaviorTreeAssetMapper.ToDto(asset), ApproachBGroupsOf(asset));
    }

    /// <summary>
    /// Maps the editor's <c>ApproachBSyncGroup</c>s onto the core's assembly-neutral shape.
    /// ⛔ The core cannot reference <c>Hrot.Editor.AiShared</c> (net8 + ImGui), so the shapes are
    /// mirrored rather than shared.
    /// </summary>
    private static IReadOnlyList<OrchestratorSyncGroup> ApproachBGroupsOf(BehaviorTreeAsset asset)
    {
        var groups = asset.GetApproachBSyncGroups();
        var result = new List<OrchestratorSyncGroup>(groups.Count);

        foreach (var g in groups)
        {
            var bindings = new List<OrchestratorSyncBinding>(g.Bindings.Count);
            foreach (var b in g.Bindings)
                bindings.Add(new OrchestratorSyncBinding(
                    b.FieldName, b.MasterVariableName, b.SyncIn, b.SyncOut));

            result.Add(new OrchestratorSyncGroup(
                g.SubtreeName, g.SubtreeDtoTypeName, g.SubtreeDtoTypeNs, bindings));
        }

        return result;
    }

    /// <summary>
    /// Writes the sidecar file using atomic write. No-op when <paramref name="sidecarContent"/> is
    /// <c>null</c> (no aliases; existing file is preserved).
    /// </summary>
    public static void WriteOrchestratorFile(BehaviorTreeAsset asset, string? sidecarContent)
    {
        if (sidecarContent is null) return;
        string path = Path.ChangeExtension(asset.SourceFilePath, null) + ".Orchestrators.g.cs";
        FluentCSharpEmitterBase.WriteAtomic(path, sidecarContent);
    }
}
