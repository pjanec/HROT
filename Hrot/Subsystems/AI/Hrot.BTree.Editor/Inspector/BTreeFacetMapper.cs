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
    private readonly BehaviorTreeAsset    _asset;
    private readonly BTreeFacetFqnContext? _fqnContext;

    public BTreeFacetMapper(BehaviorTreeAsset asset)
        : this(asset, null)
    {
    }

    /// <summary>
    /// Constructs a mapper that writes the current action/condition FQN to
    /// <paramref name="fqnContext"/> before returning each facet, so the
    /// <see cref="BlackboardFieldPickerDrawer"/> can filter variables by DtoType.
    /// </summary>
    public BTreeFacetMapper(BehaviorTreeAsset asset, BTreeFacetFqnContext? fqnContext)
    {
        _asset      = asset      ?? throw new ArgumentNullException(nameof(asset));
        _fqnContext = fqnContext;
    }

    // ── IFacetDispatcher ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public object? GetFacet(IAssetSubSelection subSelection)
    {
        if (subSelection is BTreePillSelection ps)
            return BuildPillFacet(ps);

        if (subSelection is not BTreeNodeSelection sel) return null;

        var node = _asset.FindNode(sel.VisualId);
        if (node is null) return null;

        // Clear the FQN context for non-action/condition nodes so the blackboard picker
        // shows all variables when a composite or wait node is selected.
        if (node.KernelType != NodeType.Action && node.KernelType != NodeType.Condition)
        {
            if (_fqnContext is not null)
            {
                _fqnContext.CurrentActionFqn    = null;
                _fqnContext.CurrentNodeVisualId = null;
            }
        }

        return node.KernelType switch
        {
            NodeType.Action   => BuildActionFacet(node, _fqnContext),
            NodeType.Condition => BuildConditionFacet(node, _fqnContext),
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
        if (subSelection is BTreePillSelection ps)
        {
            ApplyPillFacet(ps, facet);
            return;
        }

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

    private static BTreeActionFacet BuildActionFacet(BTreeEditorNode node, BTreeFacetFqnContext? ctx)
    {
        var fqn = node.Action?.MethodFqn ?? string.Empty;
        if (ctx is not null)
        {
            ctx.CurrentActionFqn   = string.IsNullOrEmpty(fqn) ? null : fqn;
            ctx.CurrentNodeVisualId = node.VisualId.ToString();
        }
        return new BTreeActionFacet
        {
            MethodFqn              = fqn,
            ExpressionTargetField  = node.Action?.ExpressionTargetField,
            Comment                = node.Comment,
            IsBreakpoint           = node.IsBreakpoint,
            VisualId               = node.VisualId.ToString(),
            LastResult             = string.Empty,
            TickCount              = 0,
        };
    }

    private static BTreeConditionFacet BuildConditionFacet(BTreeEditorNode node, BTreeFacetFqnContext? ctx)
    {
        var fqn = node.Condition?.MethodFqn ?? string.Empty;
        if (ctx is not null)
        {
            ctx.CurrentActionFqn   = string.IsNullOrEmpty(fqn) ? null : fqn;
            ctx.CurrentNodeVisualId = node.VisualId.ToString();
        }
        return new BTreeConditionFacet
        {
            MethodFqn              = fqn,
            ExpressionTargetField  = node.Condition?.ExpressionTargetField,
            Comment                = node.Comment,
            IsBreakpoint           = node.IsBreakpoint,
            VisualId               = node.VisualId.ToString(),
            LastResult             = string.Empty,
            TickCount              = 0,
        };
    }

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

    // ── Pill facet helpers ────────────────────────────────────────────────────

    private object? BuildPillFacet(BTreePillSelection ps)
    {
        var pill = _asset.FindPill(ps.PillVisualId);
        if (pill is null) return null;
        return pill.DecoratorType switch
        {
            NodeType.Repeater     => new BTreeRepeaterFacet
                { Count = pill.IntParam ?? 1, Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            NodeType.Cooldown     => new BTreeCooldownFacet
                { Duration = pill.FloatParam ?? 1f, Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            NodeType.Inverter     => new BTreeInverterFacet
                { Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            NodeType.ForceSuccess => new BTreeForceSuccessFacet
                { Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            NodeType.ForceFailure => new BTreeForceFailureFacet
                { Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            NodeType.UntilSuccess => new BTreeUntilSuccessFacet
                { Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            NodeType.UntilFailure => new BTreeUntilFailureFacet
                { Comment = pill.Comment, VisualId = pill.VisualId.ToString() },
            _                     => null,
        };
    }

    private void ApplyPillFacet(BTreePillSelection ps, object facet)
    {
        var pill = _asset.FindPill(ps.PillVisualId);
        if (pill is null) return;
        switch (facet)
        {
            case BTreeRepeaterFacet rf:
                pill.IntParam = rf.Count;
                pill.Comment  = rf.Comment;
                break;
            case BTreeCooldownFacet cf:
                pill.FloatParam = cf.Duration;
                pill.Comment    = cf.Comment;
                break;
            case BTreeInverterFacet inf:
                pill.Comment = inf.Comment;
                break;
            case BTreeForceSuccessFacet fsf:
                pill.Comment = fsf.Comment;
                break;
            case BTreeForceFailureFacet fff:
                pill.Comment = fff.Comment;
                break;
            case BTreeUntilSuccessFacet usf:
                pill.Comment = usf.Comment;
                break;
            case BTreeUntilFailureFacet uff:
                pill.Comment = uff.Comment;
                break;
        }
        _asset.MarkDirty();
    }
}
