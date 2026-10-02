using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
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
    // ⭐ CE-439 — the catalogue the subtree pick resolves against (asset id + the child's Inputs contract). ⚠ Optional so a
    //   headless fixture need not supply one; ⛔ a production host HAS one and passes it (AiFacetPickerBinder).
    private readonly Hrot.Editor.AiShared.Catalog.IAssetCatalog? _catalog;

    /// <summary>
    /// ⭐ <c>CE-417</c> slice 4b: no side channel any more — each action/condition facet carries its whole binding, so the
    /// one binding drawer filters the variables by the binding's own method (the retired <c>BTreeFacetFqnContext</c>).
    /// </summary>
    /// <param name="actionSchema">⭐ <c>CE-417</c> slice 4c — resolves a blueprint picked in the inspector to its generated
    /// Params/WorkingState types so the pick COMPOSES its variables (the palette-drop rule). ⚠ Optional for headless fixtures;
    /// ⛔ a production host HAS one and passes it (<c>AiFacetPickerBinder</c>).</param>
    public BTreeFacetMapper(BehaviorTreeAsset asset, Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null,
                            Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? actionSchema = null)
    {
        _asset        = asset ?? throw new ArgumentNullException(nameof(asset));
        _catalog      = catalog;
        _actionSchema = actionSchema;
    }

    private readonly Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? _actionSchema;

    // ── IFacetDispatcher ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public object? GetFacet(IAssetSubSelection subSelection)
    {
        if (subSelection is BTreePillSelection ps)
            return BuildPillFacet(ps);

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
                // ⭐ CE-417 slice 4b — THE shared applier. A BTree node always carries its binding (KeepWhenEmpty).
                if (node.Action is not null)
                {
                    bool hadBlueprint = NamesBlueprint(node.Action);
                    node.Action = BehaviorActionBindingEditor.Apply(node.Action, af.Action, ApplyContext);
                    ApplyShape(node, node.Action, hadBlueprint);
                }
                node.Comment      = af.Comment;
                node.IsBreakpoint = af.IsBreakpoint;
                break;

            case BTreeConditionFacet cf:
                if (node.Condition is not null)
                {
                    bool hadBlueprint = NamesBlueprint(node.Condition);
                    node.Condition = BehaviorActionBindingEditor.Apply(node.Condition, cf.Condition, ApplyContext);
                    ApplyShape(node, node.Condition, hadBlueprint);
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
                ApplySubtreePick(node, stf.SubtreeName);   // ⭐ CE-439
                break;
        }

        _asset.MarkDirty();
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-439</c> (<c>Q76</c> §12.28) — <b>the BTree subtree pick LANDS.</b> 🔴 The facet carried
    /// <c>[AiAssetPicker(BTree)]</c> on <c>SubtreeName</c> and this method wrote back only <c>Comment</c>/<c>IsBreakpoint</c>,
    /// so a pick was discarded; the only writer of the reference was the heal-on-load resolver. ⭐ Same shape as the HSM
    /// state's pick: the Guid captured at pick time (the shared rule), and a NEWLY picked child brings its params variable
    /// (the shared compose step) bound as this site's <c>ParamsVariable</c>.
    /// </summary>
    private void ApplySubtreePick(BTreeEditorNode node, string? pickedName)
    {
        node.Subtree ??= new BTreeSubtreePayload();
        var st = node.Subtree;
        if (string.IsNullOrWhiteSpace(pickedName))
        {
            st.SubtreeName    = string.Empty;
            st.SubtreeAssetId = Guid.Empty;
            st.IsResolved     = false;
            st.ParamsVariable = null;   // no child, no binding (the variable stays — no rush removals)
            return;
        }
        if (string.Equals(st.SubtreeName, pickedName, StringComparison.Ordinal)) return;   // unchanged ⇒ nothing to compose

        st.SubtreeName = pickedName!;
        (st.SubtreeAssetId, st.IsResolved) = Hrot.Editor.AiShared.References.SubtreeReferenceResolver.ResolvePick(
            _catalog, pickedName!, Hrot.Editor.AiShared.AssetKind.BTree, st.SubtreeAssetId);
        st.ParamsVariable = Hrot.Editor.AiShared.Blackboard.AutoManagedVariables.ComposeForSubtree(
            _asset, pickedName!,
            (_catalog?.FindByAssetId(st.SubtreeAssetId) as Hrot.Editor.AiShared.IBehaviorInputsContract)?.InputsTypeId);
    }

    // ── Private builders ──────────────────────────────────────────────────────

    /// <summary>⭐ The BTree apply rules: a node keeps its binding even when empty; a CHANGED blueprint pick composes params
    /// AND working state — the same base names the palette drop uses (<c>ComposeForAiPrimitive</c>'s defaults).</summary>
    private ActionBindingApplyContext ApplyContext => new(
        _asset, _catalog, _actionSchema, ComposeBaseName: "bpParams", KeepWhenEmpty: true, WorkingStateBaseName: "bpWorkingState");

    private static bool NamesBlueprint(Hrot.Editor.AiShared.BehaviorActionBinding? b)
        => b is not null && (b.BlueprintAssetId != Guid.Empty || !string.IsNullOrEmpty(b.BlueprintName));

    /// <summary>
    /// ⭐ <c>CE-417</c> slice 4c (design §5.5 ②) — a blueprint binding is called through its generated TickCore, so the node's
    /// shape is <c>AiPrimitiveTickCore</c>; clearing the blueprint returns the node to the plain shape. ⚠ Only a blueprint
    /// pick moves the shape: a hand-written AiPrimitive-shaped C# method keeps whatever shape the palette gave it.
    /// </summary>
    private static void ApplyShape(BTreeEditorNode node, Hrot.Editor.AiShared.BehaviorActionBinding? b, bool hadBlueprint)
    {
        // ⭐⭐ CE-504 C-1 — a C# pick takes the shape its method's signature implies (a stateful method becomes stateful).
        if (!NamesBlueprint(b) && BTreeCallShapeResolver.ShapeOf(
                b, Hrot.AiEditor.Persistence.Emit.BTreeCallShapes.LoadedAssemblySignatures()) is { } derived)
            node.DelegateShape = derived;
        else if (NamesBlueprint(b)) node.DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore;
        else if (hadBlueprint) node.DelegateShape = BTreeActionDelegateShape.Plain;
    }

    /// <summary>The node's binding as the inspector shows it. ⚠ A param-less (<c>NoParams</c>) method binds no variable, so
    /// the drawer offers none.</summary>
    private static BehaviorActionBindingFacet BindingFacet(BTreeEditorNode node, Hrot.Editor.AiShared.BehaviorActionBinding? binding)
        => BehaviorActionBindingEditor.ToFacet(
               binding, node.VisualId.ToString(),
               targetsWholeBlackboard: binding is not null && node.DelegateShape is BTreeActionDelegateShape.NoParams);

    private static BTreeActionFacet BuildActionFacet(BTreeEditorNode node) =>
        new BTreeActionFacet
        {
            Action       = BindingFacet(node, node.Action),
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
            LastResult   = string.Empty,
            TickCount    = 0,
        };

    private static BTreeConditionFacet BuildConditionFacet(BTreeEditorNode node) =>
        new BTreeConditionFacet
        {
            Condition    = BindingFacet(node, node.Condition),
            Comment      = node.Comment,
            IsBreakpoint = node.IsBreakpoint,
            VisualId     = node.VisualId.ToString(),
            LastResult   = string.Empty,
            TickCount    = 0,
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
