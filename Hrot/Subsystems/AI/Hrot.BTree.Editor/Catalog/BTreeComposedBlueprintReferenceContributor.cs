using System;
using System.Collections.Generic;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;

namespace Hrot.BTree.Editor.Catalog;

/// <summary>
/// Phase C (AIE-053): contributes cross-asset <see cref="SubElementKind.ActionFqn"/>/
/// <see cref="SubElementKind.ConditionFqn"/> references for <see cref="BehaviorTreeAsset"/> nodes
/// composed onto a Blueprint-compiled AiPrimitive (<c>DelegateShape == AiPrimitiveTickCore</c>).
/// <para>
/// Identity is by FQN string, not a persisted AssetId (see
/// <see cref="ComposedBlueprintResolver"/>): the reference's <c>TargetKey</c> is derived purely
/// from the composed node's <c>MethodFqn</c> via <see cref="ComposedBlueprintResolver.ReferenceKeyFor"/>,
/// which matches the element key the Blueprint subsystem exposes for the owning asset (see
/// <c>BlueprintReferenceContributor</c> in <c>Hrot.Blueprints.Editor</c>).
/// </para>
/// <para>
/// This contributor exposes no elements of its own — a composed BTree node is never itself a
/// reference target — it only produces references.
/// </para>
/// </summary>
public sealed class BTreeComposedBlueprintReferenceContributor : IReferenceCatalogContributor
{
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset) =>
        Array.Empty<IAssetSubElement>();

    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset)
            return Array.Empty<AssetReference>();

        var result = new List<AssetReference>();
        foreach (var node in btAsset.Nodes)
        {
            string? methodFqn;
            SubElementKind targetKind;

            if (node.KernelType == NodeType.Action &&
                node.Action?.DelegateShape == BTreeActionDelegateShape.AiPrimitiveTickCore)
            {
                methodFqn  = node.Action.MethodFqn;
                targetKind = SubElementKind.ActionFqn;
            }
            else if (node.KernelType == NodeType.Condition &&
                     node.Condition?.DelegateShape == BTreeActionDelegateShape.AiPrimitiveTickCore)
            {
                methodFqn  = node.Condition.MethodFqn;
                targetKind = SubElementKind.ConditionFqn;
            }
            else
            {
                continue;
            }

            var targetKey = ComposedBlueprintResolver.ReferenceKeyFor(methodFqn);
            if (targetKey is null)
                continue; // Composed shape but MethodFqn doesn't match the generated pattern — skip.

            result.Add(new AssetReference(
                HostAssetId:     btAsset.AssetId,
                HostKind:        AssetKind.BTree,
                HostElementId:   node.VisualId,
                HostDisplayPath: node.DisplayLabel,
                TargetKey:       targetKey,
                TargetKind:      targetKind));
        }
        return result;
    }
}
