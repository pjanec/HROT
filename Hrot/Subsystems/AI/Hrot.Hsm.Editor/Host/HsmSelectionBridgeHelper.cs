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
/// FIX-A + HSM-TRANS: per-frame bridge that maps the NodeEdit canvas <see cref="SelectionState"/>
/// to an HSM sub-selection and publishes it to the HSM <see cref="AiSelectionStore"/>
/// so the HSM Inspector can show the SE1/SE2 facet table.
///
/// <para>
/// <b>Coverage:</b>
/// <list type="bullet">
///   <item>Exactly one canvas node selected → <see cref="HsmStateSelection"/> (state facet).</item>
///   <item>Exactly one canvas link selected → <see cref="HsmTransitionSelection"/> (transition facet).
///         <c>LinkId.Value == TransitionNode.VisualId</c> per <see cref="HsmGraphModel"/> contract.</item>
///   <item>Empty / multi / mixed / comment / attachment → <see langword="null"/>.</item>
/// </list>
/// When a node and a link are both selected (mixed), the state node is preferred deterministically.
/// </para>
///
/// <para>
/// The canvas <c>NodeId.Value</c> equals <see cref="StateNode.StableId"/> (as established
/// by <c>StateNode.Id = new NodeId(StableId)</c> in <see cref="HsmGraphModel"/>), so no
/// additional asset lookup is required beyond confirming the state exists.
/// </para>
///
/// <para>
/// Kinds <em>not</em> wired through this canvas path:
/// <see cref="HsmRegionSelection"/>, <see cref="HsmEventSelection"/>,
/// <see cref="HsmGlobalTransitionSelection"/> (already wired in <c>HsmGlobalsStrip</c>).
/// </para>
/// </summary>
public static class HsmSelectionBridgeHelper
{
    /// <summary>
    /// Pure mapping: given the canvas <paramref name="selection"/> and the active
    /// <paramref name="hsmAsset"/>, returns:
    /// <list type="bullet">
    ///   <item><see cref="HsmStateSelection"/> when exactly one state node is selected;</item>
    ///   <item><see cref="HsmTransitionSelection"/> when exactly one transition link is selected;</item>
    ///   <item><see langword="null"/> for empty, multi-select, mixed, or unrecognised selections.</item>
    /// </list>
    ///
    /// <para>
    /// Canvas identity contracts (<see cref="HsmGraphModel"/>):
    /// <c>NodeId.Value == StateNode.StableId</c>;
    /// <c>LinkId.Value == TransitionNode.VisualId</c>.
    /// Both are verified against the asset before constructing the selection record so
    /// stale canvas ids do not propagate.
    /// </para>
    ///
    /// <para>
    /// When the selection contains exactly one node and exactly one link (Count == 2, mixed),
    /// the state node is preferred.  Any other multi-element selection returns <see langword="null"/>.
    /// </para>
    /// </summary>
    /// <param name="selection">The NodeEdit canvas selection state.</param>
    /// <param name="hsmAsset">
    ///   The active HSM asset (from <c>AiCanvasContext.AssetRef</c>).
    ///   When <see langword="null"/>, returns <see langword="null"/>.
    /// </param>
    /// <returns>
    ///   An <see cref="IAssetSubSelection"/> representing the selected HSM element,
    ///   or <see langword="null"/> when the selection maps to nothing.
    /// </returns>
    public static IAssetSubSelection? MapSelection(
        SelectionState  selection,
        HsmAsset?       hsmAsset)
    {
        if (hsmAsset == null) return null;
        if (selection.Count == 0) return null;

        // --- single node (state) ---
        using var nodeEnum = selection.Nodes.GetEnumerator();
        if (nodeEnum.MoveNext())
        {
            // Prefer node over link when mixed (deterministic tie-break: state wins).
            var stableId = nodeEnum.Current.Value;
            // Only return state selection when it's an exclusive single-node selection
            // OR a mixed node+link single pair — node is preferred in both cases.
            if (!nodeEnum.MoveNext()) // no second node
            {
                var state = hsmAsset.FindStateByStableId(stableId);
                if (state is not null)
                    return new HsmStateSelection(stableId);
                // Node present but not a known state → fall through to check links.
            }
            else
            {
                // More than one node → multi-select, return null.
                return null;
            }
        }

        // --- single link (transition) ---
        using var linkEnum = selection.Links.GetEnumerator();
        if (linkEnum.MoveNext())
        {
            // Canvas LinkId.Value == TransitionNode.VisualId (HsmGraphModel contract).
            var visualId = linkEnum.Current.Value;
            if (!linkEnum.MoveNext()) // no second link
            {
                // Must also be the only selected element overall (no nodes, one link).
                if (selection.Count == 1)
                {
                    var transition = hsmAsset.FindTransitionByVisualId(visualId);
                    return transition is null ? null : new HsmTransitionSelection(visualId);
                }
            }
        }

        return null;
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
