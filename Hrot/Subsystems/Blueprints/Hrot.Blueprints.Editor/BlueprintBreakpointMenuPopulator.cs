using System;
using System.Collections.Generic;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using DiagBP = Hrot.Diagnostics.Breakpoints;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Populates the right-click context menu for a Blueprint graph node with
/// data-breakpoint items. Blueprint nodes use the external-hit path via
/// <see cref="IDataBreakpointManager.OnExternalHit"/> because Blueprint execution
/// is probe-driven rather than trace-buffer-driven.
/// </summary>
public static class BlueprintBreakpointMenuPopulator
{
    /// <summary>
    /// Adds breakpoint menu items to <paramref name="builder"/> for the given node.
    /// </summary>
    /// <param name="nodeId">The runtime node id string (e.g. <c>nodeGuid.ToString("D")</c>).</param>
    /// <param name="assetId">The asset GUID of the containing Blueprint.</param>
    /// <param name="builder">The context menu builder.</param>
    /// <param name="manager">The data-breakpoint manager to register breakpoints on.</param>
    /// <param name="onOpenConditionalInspector">
    /// Optional callback invoked after a conditional breakpoint is created,
    /// so the caller can open the Details Inspector for the user to configure Branch B.
    /// </param>
    public static void PopulateNodeMenu(
        string nodeId,
        Guid assetId,
        IContextMenuBuilder builder,
        DiagBP.IDataBreakpointManager manager,
        Action<DiagBP.BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        // ---- Top-level: Add Conditional Data Breakpoint... --------------------

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            // Branch A (read-only): external-hit tag matching the Blueprint node id
            var tagPredicate = new ExternalHitTagPredicateDto { Tag = nodeId };

            // Branch B: blueprint-variable predicate scoped to this asset
            var variablePredicate = new BlueprintVariablePredicateDto
            {
                TargetBlueprintAssetId = assetId,
            };

            var compound = new CompoundPredicateDto
            {
                Operator             = LogicalOperator.And,
                Conditions           = new List<SearchPredicateDto>
                {
                    tagPredicate,
                    variablePredicate,
                },
                ReadOnlyChildIndices = new List<int> { 0 },
            };

            var bpId = manager.AddBreakpoint(compound,
                displayName: $"Blueprint Conditional: {nodeId}");

            onOpenConditionalInspector?.Invoke(bpId, compound);
        });
    }
}
