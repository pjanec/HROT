using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.References;

namespace Hrot.BTree.Editor.Catalog;

/// <summary>
/// Contributes <see cref="SubElementKind.BlackboardVariable"/> sub-elements and
/// references for <see cref="BehaviorTreeAsset"/> instances.
/// Each variable is keyed as "{assetId:D}::{variableName}".
/// Each action/condition node with a non-null ExpressionTargetField produces one reference.
/// </summary>
public sealed class BTreeBlackboardVariableContributor : IReferenceCatalogContributor
{
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset || !btAsset.IsBlackboardEditorManaged)
            return Array.Empty<IAssetSubElement>();

        var result = new List<IAssetSubElement>(btAsset.BlackboardVariables.Count);
        foreach (var v in btAsset.BlackboardVariables)
            result.Add(new BlackboardVariableSubElement(btAsset.AssetId, v.Name));
        return result;
    }

    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset || !btAsset.IsBlackboardEditorManaged)
            return Array.Empty<AssetReference>();

        var result = new List<AssetReference>();
        foreach (var node in btAsset.Nodes)
        {
            string? etf = node.Action?.ExpressionTargetField
                       ?? node.Condition?.ExpressionTargetField;
            if (etf is null) continue;

            result.Add(new AssetReference(
                HostAssetId:     btAsset.AssetId,
                HostKind:        AssetKind.BTree,
                HostElementId:   node.VisualId,
                HostDisplayPath: node.DisplayLabel,
                TargetKey:       BlackboardVariableSubElement.KeyFor(btAsset.AssetId, etf),
                TargetKind:      SubElementKind.BlackboardVariable));
        }
        return result;
    }
}

// ⭐ HSM-017 (2026-10-06): BlackboardVariableSubElement MOVED to Hrot.Editor.AiShared.References so the HSM host
//   can produce byte-identical keys. ⛔ Do not re-declare it here — two spellings of the key format is how a
//   rename comes to report success while leaving the other host's binding dangling.
