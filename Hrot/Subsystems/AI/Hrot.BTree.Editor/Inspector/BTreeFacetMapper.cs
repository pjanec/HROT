using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Implements <see cref="IFacetDispatcher"/> for the BTree perspective.
/// Maps <see cref="BTreeNodeSelection"/> sub-selections to the appropriate
/// BTree facet struct, and applies edited facets back to the asset model.
/// Constructed per open asset from the composition root.
/// </summary>
public sealed class BTreeFacetMapper : IFacetDispatcher
{
    private readonly BehaviorTreeAsset _asset;

    public BTreeFacetMapper(BehaviorTreeAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    // ── IFacetDispatcher ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public object? GetFacet(IAssetSubSelection subSelection)
    {
        if (subSelection is not BTreeNodeSelection sel) return null;

        var node = _asset.FindNode(sel.VisualId);
        if (node is null) return null;

        return node.KernelType switch
        {
            NodeType.Action   => BuildActionFacet(node),
            NodeType.Condition => BuildConditionFacet(node),
            NodeType.Wait     => BuildWaitFacet(node),
            NodeType.Sequence => BuildSequenceFacet(node),
            NodeType.Selector => BuildSelectorFacet(node),
            NodeType.ObserverSelector => BuildObserverSelectorFacet(node),
            NodeType.Parallel => BuildParallelFacet(node),
            NodeType.Root     => BuildRootFacet(node),
            NodeType.Subtree  => BuildSubtreeFacet(node),
            _                 => null,
        };
    }

    /// <inheritdoc/>
    public void ApplyFacet(IAssetSubSelection subSelection, object facet)
    {
        if (subSelection is not BTreeNodeSelection sel) return;
        var node = _asset.FindNode(sel.VisualId);
        if (node is null) return;

        switch (facet)
        {
            case BTreeActionFacet af:
                if (node.Action is not null)
                {
                    node.Action.MethodFqn           = af.MethodFqn;
                    node.Action.ExpressionTargetField = af.ExpressionTargetField;
                }
                node.Comment      = af.Comment;
                node.IsBreakpoint = af.IsBreakpoint;
                break;

            case BTreeConditionFacet cf:
                if (node.Condition is not null)
                {
                    node.Condition.MethodFqn             = cf.MethodFqn;
                    node.Condition.ExpressionTargetField = cf.ExpressionTargetField;
                }
                node.Comment      = cf.Comment;
                node.IsBreakpoint = cf.IsBreakpoint;
                break;

            case BTreeWaitFacet wf:
                if (node.Wait is not null)
                    node.Wait.Duration = wf.Duration;
                node.Comment      = wf.Comment;
                node.IsBreakpoint = wf.IsBreakpoint;
                break;

            case BTreeSequenceFacet sf:
                node.Comment      = sf.Comment;
                node.IsBreakpoint = sf.IsBreakpoint;
                break;

            case BTreeSelectorFacet sf:
                node.Comment      = sf.Comment;
                node.IsBreakpoint = sf.IsBreakpoint;
                break;

            case BTreeObserverSelectorFacet osf:
                node.Comment      = osf.Comment;
                node.IsBreakpoint = osf.IsBreakpoint;
                break;

            case BTreeParallelFacet pf:
                node.Comment      = pf.Comment;
                node.IsBreakpoint = pf.IsBreakpoint;
                break;

            case BTreeRootFacet rf:
                node.Comment = rf.Comment;
                break;

            case BTreeSubtreeFacet stf:
                node.Comment      = stf.Comment;
                node.IsBreakpoint = stf.IsBreakpoint;
                break;
        }

        _asset.MarkDirty();
    }

    // ── Private builders ──────────────────────────────────────────────────────

    private static BTreeActionFacet BuildActionFacet(BTreeEditorNode node) =>
        new BTreeActionFacet
        {
            MethodFqn              = node.Action?.MethodFqn ?? string.Empty,
            ExpressionTargetField  = node.Action?.ExpressionTargetField,
            Comment                = node.Comment,
            IsBreakpoint           = node.IsBreakpoint,
            VisualId               = node.VisualId.ToString(),
            LastResult             = string.Empty,
            TickCount              = 0,
        };

    private static BTreeConditionFacet BuildConditionFacet(BTreeEditorNode node) =>
        new BTreeConditionFacet
        {
            MethodFqn              = node.Condition?.MethodFqn ?? string.Empty,
            ExpressionTargetField  = node.Condition?.ExpressionTargetField,
            Comment                = node.Comment,
            IsBreakpoint           = node.IsBreakpoint,
            VisualId               = node.VisualId.ToString(),
            LastResult             = string.Empty,
            TickCount              = 0,
        };

    private static BTreeWaitFacet BuildWaitFacet(BTreeEditorNode node) =>
        new BTreeWaitFacet
        {
            Duration     = node.Wait?.Duration ?? 0f,
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
        };

    private BTreeSequenceFacet BuildSequenceFacet(BTreeEditorNode node) =>
        new BTreeSequenceFacet
        {
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
            ChildCount   = node.ChildVisualIds.Count,
        };

    private BTreeSelectorFacet BuildSelectorFacet(BTreeEditorNode node) =>
        new BTreeSelectorFacet
        {
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
            ChildCount   = node.ChildVisualIds.Count,
        };

    private BTreeObserverSelectorFacet BuildObserverSelectorFacet(BTreeEditorNode node) =>
        new BTreeObserverSelectorFacet
        {
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
            ChildCount   = node.ChildVisualIds.Count,
        };

    private BTreeParallelFacet BuildParallelFacet(BTreeEditorNode node) =>
        new BTreeParallelFacet
        {
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
            ChildCount   = node.ChildVisualIds.Count,
        };

    private static BTreeRootFacet BuildRootFacet(BTreeEditorNode node) =>
        new BTreeRootFacet
        {
            Comment  = node.Comment,
            VisualId = node.VisualId.ToString(),
        };

    private BTreeSubtreeFacet BuildSubtreeFacet(BTreeEditorNode node) =>
        new BTreeSubtreeFacet
        {
            SubtreeName    = node.Subtree?.SubtreeName ?? string.Empty,
            SubtreeAssetId = node.Subtree?.SubtreeAssetId.ToString() ?? string.Empty,
            IsResolved     = node.Subtree?.IsResolved ?? false,
            Comment        = node.Comment,
            IsBreakpoint   = node.IsBreakpoint,
            VisualId       = node.VisualId.ToString(),
        };
}
