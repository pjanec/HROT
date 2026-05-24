using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.BTree.Editor.Model;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.BTree.Editor.Debug;

/// <summary>
/// Populates the right-click context menu for a BTree editor node with
/// data-breakpoint items that synthesise <see cref="TraceBufferScanPredicateDto"/> and
/// <see cref="CompoundPredicateDto"/> conditions registered via
/// <see cref="IDataBreakpointManager.AddBreakpoint"/>.
/// </summary>
public static class BTreeBreakpointMenuPopulator
{
    /// <summary>
    /// Adds breakpoint menu items to <paramref name="builder"/> for the given
    /// <paramref name="node"/>. Called by the canvas right-click handler.
    /// </summary>
    /// <param name="node">The node that was right-clicked.</param>
    /// <param name="builder">The context menu builder.</param>
    /// <param name="manager">The data-breakpoint manager to register breakpoints on.</param>
    /// <param name="onOpenConditionalInspector">
    /// Optional callback invoked after a conditional breakpoint is created,
    /// so the caller can open the Details Inspector for the user to configure Branch B.
    /// </param>
    public static void PopulateMenu(
        BTreeEditorNode node,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        // ---- Submenu: Add Breakpoint -------------------------------------------

        var sub = builder.BeginSubmenu("Add Breakpoint");

        // Break on Activation (Enter): NodeEvaluated + Running status
        sub.AddItem("Break on Activation (Enter)", () =>
        {
            var predicate = new TraceBufferScanPredicateDto
            {
                ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                IndexField       = (ushort)node.KernelBlobIndex,
                MatchIndexField  = true,
                StatusField      = (byte)NodeStatus.Running,
                MatchStatusField = true,
            };
            manager.AddBreakpoint(predicate,
                displayName:     $"BTree Enter: {node.DisplayLabel}",
                sourceElementId: node.VisualId);
        });

        // Break on Completion (Exit): NodeEvaluated Success OR Failure
        sub.AddItem("Break on Completion (Exit)", () =>
        {
            var successScan = new TraceBufferScanPredicateDto
            {
                ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                IndexField       = (ushort)node.KernelBlobIndex,
                MatchIndexField  = true,
                StatusField      = (byte)NodeStatus.Success,
                MatchStatusField = true,
            };
            var failureScan = new TraceBufferScanPredicateDto
            {
                ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                IndexField       = (ushort)node.KernelBlobIndex,
                MatchIndexField  = true,
                StatusField      = (byte)NodeStatus.Failure,
                MatchStatusField = true,
            };
            var predicate = new CompoundPredicateDto
            {
                Operator   = LogicalOperator.Or,
                Conditions = new List<SearchPredicateDto> { successScan, failureScan },
            };
            manager.AddBreakpoint(predicate,
                displayName:     $"BTree Exit: {node.DisplayLabel}",
                sourceElementId: node.VisualId);
        });

        // Break on Interruption (Abort): ScopePopped, no index constraint
        sub.AddItem("Break on Interruption (Abort)", () =>
        {
            var predicate = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(BTreeTraceWorkingMemory1024),
                OpCode          = (byte)BTreeTraceOpCode.ScopePopped,
                MatchIndexField = false,
            };
            manager.AddBreakpoint(predicate,
                displayName:     $"BTree Abort: {node.DisplayLabel}",
                sourceElementId: node.VisualId);
        });

        sub.EndSubmenu();

        // ---- Top-level: Add Conditional Data Breakpoint... --------------------

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            // Branch A (read-only): trace-buffer scan for Enter
            var enterScan = new TraceBufferScanPredicateDto
            {
                ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                IndexField       = (ushort)node.KernelBlobIndex,
                MatchIndexField  = true,
                StatusField      = (byte)NodeStatus.Running,
                MatchStatusField = true,
            };

            // Branch B: empty BehaviorParam predicate for the user to configure
            var compound = new CompoundPredicateDto
            {
                Operator            = LogicalOperator.And,
                Conditions          = new List<SearchPredicateDto>
                {
                    enterScan,
                    new BehaviorParamPredicateDto(),
                },
                ReadOnlyChildIndices = new List<int> { 0 },
            };

            var bpId = manager.AddBreakpoint(compound,
                displayName:     $"BTree Conditional: {node.DisplayLabel}",
                sourceElementId: node.VisualId);

            onOpenConditionalInspector?.Invoke(bpId, compound);
        });
    }
}
