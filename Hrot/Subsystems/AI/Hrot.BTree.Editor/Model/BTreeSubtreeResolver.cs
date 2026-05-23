using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.BTree.Editor.Model;

// Resolves subtree node references in a BehaviorTreeAsset against the asset catalog.
// Call after projection or after a hot reload to update IsResolved flags.
public static class BTreeSubtreeResolver
{
    // Walks all nodes in the asset and resolves each Subtree node's SubtreeName
    // against the given catalog.
    // Updates SubtreeAssetId and IsResolved on each BTreeSubtreePayload in place.
    public static void Resolve(BehaviorTreeAsset asset, IAssetCatalog catalog)
    {
        foreach (var node in asset.Nodes)
        {
            if (node.KernelType != NodeType.Subtree) continue;
            var payload = node.Subtree;
            if (payload is null) continue;

            var referenced = catalog.FindByName(payload.SubtreeName);
            if (referenced != null && referenced.Kind == AssetKind.BTree)
            {
                payload.SubtreeAssetId = referenced.AssetId;
                payload.IsResolved = true;
            }
            else
            {
                payload.SubtreeAssetId = Guid.Empty;
                payload.IsResolved = false;
            }
        }
    }
}
