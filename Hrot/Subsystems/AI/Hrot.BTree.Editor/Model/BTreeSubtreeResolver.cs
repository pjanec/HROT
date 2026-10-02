using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.BTree.Editor.Model;

/// <summary>
/// ⭐ Resolves subtree node references in a <see cref="BehaviorTreeAsset"/> against the asset
/// catalogue. Call after projection or after a hot reload.
/// 📄 <c>BTree_Editor_NodeEditor_Host_Design.md</c> §S1 ②.
///
/// <para>🔴🔴 <b>WHAT THIS USED TO DO, and why it was a defect.</b> When the name did not resolve it
/// ran <c>payload.SubtreeAssetId = Guid.Empty</c> — ⛔ <b>it erased the persisted Guid.</b> ⚠ A
/// missed name <b>IS</b> the rename case, so the one field that could still identify the asset was
/// destroyed at exactly the moment it was needed, and the reference dangled for ever even though
/// <c>BTreeSubtreePayloadDto</c> had the Guid on disk all along.</para>
///
/// <para>⭐ Now it applies the shared heal rule — name, else Guid ⇒ heal the name, else keep
/// <b>both</b> — via <see cref="SubtreeReferenceResolver"/>, the same decision the HSM side makes
/// (📄 <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a). 🔒 User: *"no differences, consistency."*</para>
/// </summary>
public static class BTreeSubtreeResolver
{
    /// <summary>
    /// ⭐ Resolves every Subtree node in place.
    /// </summary>
    /// <returns>
    /// ⭐⭐ How many names were <b>healed</b> from their Guid. ⚠ Non-zero ⇒ <b>the caller must mark
    /// the asset dirty</b>; an unsaved heal is redone on every load and the file keeps the stale name.
    /// </returns>
    public static int Resolve(BehaviorTreeAsset asset, IAssetCatalog catalog)
    {
        int healed = 0;

        foreach (var node in asset.Nodes)
        {
            if (node.KernelType != NodeType.Subtree) continue;
            var payload = node.Subtree;
            if (payload is null) continue;

            var r = SubtreeReferenceResolver.Resolve(
                catalog, payload.SubtreeName, payload.SubtreeAssetId, AssetKind.BTree);

            payload.SubtreeName    = r.Name;
            payload.SubtreeAssetId = r.AssetId;   // ⛔ NEVER Guid.Empty on a miss — see the remarks.
            payload.IsResolved     = r.IsResolved;

            if (r.Healed) healed++;
        }

        return healed;
    }
}
