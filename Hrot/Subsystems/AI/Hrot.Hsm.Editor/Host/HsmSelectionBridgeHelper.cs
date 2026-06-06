using System;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.View;

// Alias to avoid ambiguity with per-project EditorSelectionStore.
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// FIX-A: per-frame bridge that maps the NodeEdit canvas <see cref="SelectionState"/>
/// to an HSM sub-selection and publishes it to the HSM <see cref="AiSelectionStore"/>
/// so the HSM Inspector can show the SE1/SE2 facet table.
///
/// <para>
/// <b>Coverage:</b> canvas node selection is mapped exclusively to state nodes
/// (<see cref="HsmStateSelection"/>).  In the HSM graph model, transitions are
/// <c>ILinkModel</c> instances (not nodes), so they cannot appear in
/// <c>SelectionState.Nodes</c> — only states do.  All other selection-kinds
/// (links, comments, attachments) yield <see langword="null"/>.
/// </para>
///
/// <para>
/// The canvas <c>NodeId.Value</c> equals <see cref="StateNode.StableId"/> (as established
/// by <c>StateNode.Id = new NodeId(StableId)</c> in <see cref="HsmGraphModel"/>), so no
/// additional asset lookup is required beyond confirming the state exists.
/// </para>
///
/// <para>
/// Kinds <em>not</em> wired through the canvas node-click path and therefore not handled
/// here: <see cref="HsmTransitionSelection"/>, <see cref="HsmRegionSelection"/>,
/// <see cref="HsmEventSelection"/>, <see cref="HsmGlobalTransitionSelection"/>.
/// HsmGlobalTransitionSelection is already wired in <c>HsmGlobalsStrip</c>.
/// </para>
/// </summary>
public static class HsmSelectionBridgeHelper
{
    /// <summary>
    /// Pure mapping: given the canvas <paramref name="selection"/> and the active
    /// <paramref name="hsmAsset"/>, returns an <see cref="HsmStateSelection"/> when
    /// exactly one state node is selected, or <see langword="null"/> otherwise
    /// (empty / multi / non-node selection, or no asset).
    ///
    /// <para>
    /// The canvas <c>NodeId.Value</c> equals <see cref="StateNode.StableId"/>
    /// (<see cref="HsmGraphModel"/> contract).  The method verifies the id exists in the
    /// asset before constructing the selection so a stale canvas id does not propagate.
    /// </para>
    /// </summary>
    /// <param name="selection">The NodeEdit canvas selection state.</param>
    /// <param name="hsmAsset">
    ///   The active HSM asset (from <c>AiCanvasContext.AssetRef</c>).
    ///   When <see langword="null"/>, returns <see langword="null"/>.
    /// </param>
    /// <returns>
    ///   An <see cref="HsmStateSelection"/> with the selected state's <c>StableId</c>,
    ///   or <see langword="null"/> when the selection is empty, multi-select, the selected
    ///   element is not a node, or the state is not found in the asset.
    /// </returns>
    public static HsmStateSelection? MapSelection(
        SelectionState  selection,
        HsmAsset?       hsmAsset)
    {
        if (hsmAsset == null) return null;
        if (selection.Count != 1) return null;

        using var enumerator = selection.Nodes.GetEnumerator();
        if (!enumerator.MoveNext()) return null;

        // Canvas NodeId.Value == StateNode.StableId (HsmGraphModel / StateNode.Id contract).
        var stableId = enumerator.Current.Value;

        // Verify the state exists in the asset (guards against stale canvas ids).
        var state = hsmAsset.FindStateByStableId(stableId);
        return state is null ? null : new HsmStateSelection(stableId);
    }

    /// <summary>
    /// Builds the per-frame <see cref="AiGraphCanvasWindow.AfterDraw"/> delegate that
    /// polls the canvas selection each frame and publishes the result to
    /// <paramref name="selectionStore"/>.
    /// </summary>
    /// <param name="selectionStore">
    ///   The HSM perspective's <see cref="AiSelectionStore"/>. Its
    ///   <c>ActiveSubSelection</c> is updated each frame when the active document is an HSM.
    /// </param>
    /// <returns>
    ///   An <see cref="Action{AiCanvasContext}"/> ready to be assigned to
    ///   <see cref="AiGraphCanvasWindow.AfterDraw"/>.
    /// </returns>
    public static Action<AiCanvasContext> BuildAfterDrawAction(
        AiSelectionStore selectionStore)
    {
        ArgumentNullException.ThrowIfNull(selectionStore);
        return ctx =>
        {
            var hsmAsset = ctx.AssetRef as HsmAsset;
            var newSel   = MapSelection(ctx.View.Selection, hsmAsset);
            selectionStore.ActiveSubSelection = newSel;
        };
    }

    /// <summary>
    /// Builds an <see cref="HsmFacetDispatcher"/> for <paramref name="asset"/> and returns it
    /// ready to be forwarded to
    /// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetDispatcher"/>.
    /// Returns <see langword="null"/> when <paramref name="asset"/> is <see langword="null"/>.
    /// </summary>
    public static HsmFacetDispatcher? BuildFacetDispatcher(HsmAsset? asset)
        => asset is null ? null : new HsmFacetDispatcher(asset);
}
