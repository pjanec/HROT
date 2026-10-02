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
        var all = MapSelections(selection, hsmAsset);
        return all.Count == 1 ? all[0] : null;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>L0.2</c> — THE BRIDGE REPORTS, IT NEVER FILTERS.</b> 📌 <c>R-118</c> · 📄
    /// <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L0.2</c>.
    ///
    /// <para>🔴 <b>Deleted:</b> <c>if (selection.Count == 0) return null;</c>, the <i>"more than one
    /// node ⇒ null"</i> arm, and the <i>"must be the only selected element overall"</i> arm. ⭐ Every
    /// selected STATE and every selected TRANSITION is now reported; ⚠ an id the asset cannot resolve
    /// is <b>dropped</b>, not fatal — a stale canvas id must not discard the designer's other
    /// selections.</para>
    ///
    /// <para>⭐⭐ <b>NODES BEFORE LINKS, deliberately.</b> 📐 The deleted code preferred a state over a
    /// transition when both were selected *(its own words: "deterministic tie-break: state wins")*.
    /// ⇒ ⭐ reporting nodes first keeps that intent visible in the ORDER, which is what a ranked
    /// consumer will read.</para>
    ///
    /// <para>⚠⚠ <b>ONE MEASURED BEHAVIOUR CHANGE, and it is HSM-only</b> — 📌 the design asked for the
    /// <i>"same set ⇒ same context"</i> rule to be <b>measured per host</b>, so it is stated rather
    /// than discovered later. <b>One state + one transition selected together</b> used to resolve to
    /// the STATE; it now reports <b>both</b>, so the store's derived single is <c>null</c>.
    /// ⛔ Restoring the old answer would require the tie-break to live in the STORE — HSM knowledge in
    /// <c>AiShared</c>, and exactly the filtering <c>R-118</c> deletes. ⭐ The design's own answer for
    /// this shape is <c>L1.4</c>'s predicate plus <c>R-117</c>'s grey line, ⚠ which land in <c>L1</c>/
    /// <c>L2</c> — so the mixed selection shows nothing until then, instead of the state.</para>
    /// </summary>
    public static IReadOnlyList<IAssetSubSelection> MapSelections(
        SelectionState  selection,
        HsmAsset?       hsmAsset)
    {
        if (hsmAsset == null) return Array.Empty<IAssetSubSelection>();

        List<IAssetSubSelection>? mapped = null;

        // --- states (nodes) — first, preserving the old state-wins tie-break as an ORDER ---
        foreach (var nodeId in selection.Nodes)
        {
            var stableId = nodeId.Value;
            if (hsmAsset.FindStateByStableId(stableId) is null) continue;   // ⭐ dropped, not fatal
            (mapped ??= new List<IAssetSubSelection>()).Add(new HsmStateSelection(stableId));
        }

        // --- transitions (links) — Canvas LinkId.Value == TransitionNode.VisualId ---
        foreach (var linkId in selection.Links)
        {
            var visualId = linkId.Value;
            if (hsmAsset.FindTransitionByVisualId(visualId) is null) continue;   // ⭐ dropped
            (mapped ??= new List<IAssetSubSelection>()).Add(new HsmTransitionSelection(visualId));
        }

        return (IReadOnlyList<IAssetSubSelection>?)mapped ?? Array.Empty<IAssetSubSelection>();
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
            // ⭐⭐ L0.2 — the FULL set is published (R-118).
            selectionStore.ActiveSubSelections = MapSelections(ctx.View.Selection, hsmAsset);
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

    /// <summary>
    /// Builds an <see cref="HsmFacetDispatcher"/> that also updates <paramref name="fqnContext"/>
    /// with the selected transition action FQN on each <c>GetFacet</c> call.
    /// Pass the same <paramref name="fqnContext"/> to
    /// <see cref="HsmPickerDrawerFactory.BuildDrawers"/> so the blackboard-field picker
    /// filters variables by the current transition's DtoType.
    /// Returns <see langword="null"/> when <paramref name="asset"/> is <see langword="null"/>.
    /// </summary>
    public static HsmFacetDispatcher? BuildFacetDispatcher(
        HsmAsset?            asset,
        HsmFacetFqnContext?  fqnContext)
        => asset is null ? null : new HsmFacetDispatcher(asset, fqnContext);
}
