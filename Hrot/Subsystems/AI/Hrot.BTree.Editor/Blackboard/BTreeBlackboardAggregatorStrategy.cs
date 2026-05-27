using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.BTree.Editor.Blackboard;

public sealed class BTreeBlackboardAggregatorStrategy : IBlackboardAggregatorStrategy
{
    // Injected so the strategy can recurse into subtree assets.
    private readonly BlackboardAggregatorService _service;

    public BTreeBlackboardAggregatorStrategy(BlackboardAggregatorService service)
        => _service = service;

    public bool CanHandle(IEditableAsset asset)
        => asset is BehaviorTreeAsset;

    public AggregationResult Aggregate(
        IEditableAsset        asset,
        IActionSchemaExporter schema,
        IAssetCatalog         catalog,
        HashSet<Guid>         visited)
    {
        var btAsset = (BehaviorTreeAsset)asset;
        if (!visited.Add(btAsset.AssetId))
            return AggregationResult.Empty;  // already visited (cycle)

        var requirements = new List<DtoRequirement>();
        var warnings     = new List<AggregationWarning>();

        foreach (var node in btAsset.Nodes)
        {
            // ---- Action / Condition: look up schema by MethodFqn ----
            string? fqn = node.Action?.MethodFqn ?? node.Condition?.MethodFqn;
            if (fqn != null)
            {
                var entry = schema.Lookup(fqn);
                if (entry != null)
                {
                    string path = $"{btAsset.Name} > {node.DisplayLabel} ({fqn})";
                    requirements.Add(new DtoRequirement(
                        entry.DtoType, path,
                        btAsset.AssetId, node.VisualId));
                }
                else
                {
                    warnings.Add(new AggregationWarning(
                        AggregationWarningKind.SchemaEntryNotFound,
                        $"Schema entry not found for FQN '{fqn}' in asset '{btAsset.Name}'.",
                        btAsset.AssetId));
                }
            }

            // ---- Subtree: recurse ----
            if (node.Subtree != null && node.Subtree.SubtreeAssetId != Guid.Empty)
            {
                var childAsset = catalog.FindByAssetId(node.Subtree.SubtreeAssetId);
                if (childAsset == null)
                {
                    warnings.Add(new AggregationWarning(
                        AggregationWarningKind.UnresolvedSubtree,
                        $"Subtree asset '{node.Subtree.SubtreeName}' ({node.Subtree.SubtreeAssetId:D}) not found in catalog.",
                        btAsset.AssetId));
                }
                else if (visited.Contains(childAsset.AssetId))
                {
                    warnings.Add(new AggregationWarning(
                        AggregationWarningKind.Cycle,
                        $"Cycle detected: asset '{childAsset.Name}' ({childAsset.AssetId:D}) already visited.",
                        childAsset.AssetId));
                }
                else
                {
                    var childResult = _service.AggregateInternal(childAsset, visited);
                    requirements.AddRange(childResult.Requirements);
                    warnings.AddRange(childResult.Warnings);
                }
            }
        }

        return new AggregationResult(requirements, warnings);
    }
}
