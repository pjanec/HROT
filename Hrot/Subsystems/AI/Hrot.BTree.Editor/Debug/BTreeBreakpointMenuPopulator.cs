using System;
using System.Collections.Generic;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.BTree.Editor.Model;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.BTree.Editor.Debug;

/// <summary>
/// ⭐ <c>CE-1324</c> (Behaviour Debugging §9.4) — a BTree node as a BEHAVIOUR STEP (<see cref="BehaviourStepPredicateDto"/> <c>NodeRuns</c>): the one place an editor gesture turns a node and an
/// outcome into the condition the evaluator matches. The behaviour's name AND the node's id are filled, so the evaluator checks the unit runs THIS behaviour and stops once per arrival.
/// </summary>
public static class BTreeBreakpointSteps
{
    public static BehaviourStepPredicateDto Node(BehaviorTreeAsset asset, BTreeEditorNode node, BehaviourNodeOutcome outcome)
        => new() { Behaviour = asset.Name, Kind = BehaviourStepKind.NodeRuns, Node = node.VisualId, Outcome = outcome, Label = node.DisplayLabel };
}

/// <summary>
/// Populates the right-click context menu for a BTree editor node with breakpoint items. ⭐ <c>CE-1324</c> (DBG-13): ROUTED from the whole-ring
/// <see cref="TraceBufferScanPredicateDto"/> (it refired every tick, checked no behaviour and was reached with a stub node) to
/// <see cref="BehaviourStepPredicateDto"/> — activation = Running, completion = Success / Failure, abort = a scope popped.
/// </summary>
public static class BTreeBreakpointMenuPopulator
{
    /// <summary>Adds breakpoint menu items for <paramref name="node"/> of <paramref name="asset"/>. Called by the canvas right-click handler and the gutter dot.</summary>
    /// <param name="onOpenConditionalInspector">Invoked after a conditional breakpoint is created, so the caller can open the Details Inspector for the user to configure Branch B.</param>
    public static void PopulateMenu(
        BehaviorTreeAsset asset,
        BTreeEditorNode node,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        var sub = builder.BeginSubmenu("Add Breakpoint");

        sub.AddItem("Break on Activation (Enter)", () =>
            manager.AddBreakpoint(BTreeBreakpointSteps.Node(asset, node, BehaviourNodeOutcome.Running),
                displayName: $"BTree Enter: {node.DisplayLabel}", sourceElementId: node.VisualId));

        sub.AddItem("Break on Completion (Exit)", () =>
            manager.AddBreakpoint(BTreeBreakpointSteps.Node(asset, node, BehaviourNodeOutcome.Completes),
                displayName: $"BTree Exit: {node.DisplayLabel}", sourceElementId: node.VisualId));

        sub.AddItem("Break on Interruption (Abort)", () =>
            manager.AddBreakpoint(BTreeBreakpointSteps.Node(asset, node, BehaviourNodeOutcome.Aborted),
                displayName: $"BTree Abort: {node.DisplayLabel}", sourceElementId: node.VisualId));

        sub.EndSubmenu();

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            // Branch A (read-only): the node's activation as a behaviour step; Branch B: an empty BehaviorParam predicate for the user to configure
            var compound = new CompoundPredicateDto
            {
                Operator             = LogicalOperator.And,
                Conditions           = new List<SearchPredicateDto>
                {
                    BTreeBreakpointSteps.Node(asset, node, BehaviourNodeOutcome.Running),
                    new BehaviorParamPredicateDto(),
                },
                ReadOnlyChildIndices = new List<int> { 0 },
            };
            var bpId = manager.AddBreakpoint(compound, displayName: $"BTree Conditional: {node.DisplayLabel}", sourceElementId: node.VisualId);
            onOpenConditionalInspector?.Invoke(bpId, compound);
        });
    }
}
