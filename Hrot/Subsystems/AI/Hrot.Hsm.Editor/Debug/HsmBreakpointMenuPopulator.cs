using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Hsm.Editor.Model;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Hsm.Editor.Debug;

/// <summary>
/// Populates the right-click context menu for an HSM state node with
/// data-breakpoint items that synthesise <see cref="TraceBufferScanPredicateDto"/> and
/// <see cref="CompoundPredicateDto"/> conditions registered via
/// <see cref="IDataBreakpointManager.AddBreakpoint"/>.
/// </summary>
public static class HsmBreakpointMenuPopulator
{
    /// <summary>
    /// Adds breakpoint menu items to <paramref name="builder"/> for the given
    /// <paramref name="state"/>. Called by the canvas right-click handler.
    /// </summary>
    /// <param name="state">The state that was right-clicked.</param>
    /// <param name="builder">The context menu builder.</param>
    /// <param name="manager">The data-breakpoint manager to register breakpoints on.</param>
    /// <param name="onOpenConditionalInspector">
    /// Optional callback invoked after a conditional breakpoint is created,
    /// so the caller can open the Details Inspector for the user to configure Branch B.
    /// </param>
    public static void PopulateStateMenu(
        StateNode state,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        // ---- Submenu: Add Breakpoint -------------------------------------------

        var sub = builder.BeginSubmenu("Add Breakpoint");

        // Break on State Enter
        sub.AddItem("Break on Enter", () =>
        {
            var predicate = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(HsmTraceWorkingMemory1024),
                OpCode          = (byte)TraceOpCode.StateEnter,
                IndexField      = state.FlatIndex,
                MatchIndexField = true,
            };
            manager.AddBreakpoint(predicate,
                displayName:     $"HSM Enter: {state.Name}",
                sourceElementId: state.StableId);
        });

        // Break on State Exit
        sub.AddItem("Break on Exit", () =>
        {
            var predicate = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(HsmTraceWorkingMemory1024),
                OpCode          = (byte)TraceOpCode.StateExit,
                IndexField      = state.FlatIndex,
                MatchIndexField = true,
            };
            manager.AddBreakpoint(predicate,
                displayName:     $"HSM Exit: {state.Name}",
                sourceElementId: state.StableId);
        });

        sub.EndSubmenu();

        // ---- Top-level: Add Conditional Data Breakpoint... --------------------

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            // Branch A (read-only): trace-buffer scan for Enter
            var enterScan = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(HsmTraceWorkingMemory1024),
                OpCode          = (byte)TraceOpCode.StateEnter,
                IndexField      = state.FlatIndex,
                MatchIndexField = true,
            };

            // Branch B: empty BehaviorParam predicate for the user to configure
            var compound = new CompoundPredicateDto
            {
                Operator             = LogicalOperator.And,
                Conditions           = new List<SearchPredicateDto>
                {
                    enterScan,
                    new BehaviorParamPredicateDto(),
                },
                ReadOnlyChildIndices = new List<int> { 0 },
            };

            var bpId = manager.AddBreakpoint(compound,
                displayName:     $"HSM Conditional: {state.Name}",
                sourceElementId: state.StableId);

            onOpenConditionalInspector?.Invoke(bpId, compound);
        });
    }
}
