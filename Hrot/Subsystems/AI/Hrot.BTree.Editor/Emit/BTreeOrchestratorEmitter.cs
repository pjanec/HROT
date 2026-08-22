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
/// <para>⭐⭐ <b>The Approach-B groups are supplied HERE and nowhere else</b>, because they are
/// session-local: <c>_syncNodeMeta</c> is written only by <c>InspectorWindow:194</c> and is
/// deliberately not persisted. 📄 The measurement, and why widening the DTO would not be enough, is
/// on <see cref="BTreeOrchestratorEmitCore"/>.</para>
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
    public static string? Emit(BehaviorTreeAsset asset)
        => BTreeOrchestratorEmitCore.Emit(
            BehaviorTreeAssetMapper.ToDto(asset), ApproachBGroupsOf(asset));

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
