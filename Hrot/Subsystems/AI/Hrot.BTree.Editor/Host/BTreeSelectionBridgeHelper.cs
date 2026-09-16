using System;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.View;

// Alias to avoid ambiguity with per-project EditorSelectionStore.
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// FIX-A: per-frame bridge that maps the NodeEdit canvas <see cref="SelectionState"/>
/// to a <see cref="BTreeNodeSelection"/> and publishes it to the BTree
/// <see cref="AiSelectionStore"/> so the BTree Inspector can show the SE1/SE2 facet table.
///
/// <para>
/// The core mapping logic is in the pure static <see cref="MapSelection"/> method so it can
/// be unit-tested without an ImGui context or a real document manager.
/// </para>
///
/// <para>
/// Wiring: for each opened BTree document, call
/// <see cref="BuildAfterDrawAction"/> once and assign the result to
/// <see cref="AiGraphCanvasWindow.AfterDraw"/> on the BTree canvas window.
/// Also call <see cref="BuildFacetDispatcher"/> and forward to
/// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetDispatcher"/> whenever
/// the active BTree asset changes (<c>AiDocumentManager.ActiveChanged</c>).
/// </para>
/// </summary>
public static class BTreeSelectionBridgeHelper
{
    /// <summary>
    /// Pure mapping: given the canvas <paramref name="selection"/> and the active
    /// <paramref name="btreeAsset"/>, returns a <see cref="BTreeNodeSelection"/> when
    /// exactly one node is selected, or <see langword="null"/> otherwise (empty / multi /
    /// no-asset selection).
    ///
    /// <para>
    /// The canvas <c>NodeId.Value</c> equals <see cref="BTreeEditorNode.VisualId"/> (as
    /// established by <c>BTreeNodeModel.Id = new NodeId(node.VisualId)</c> in
    /// <see cref="BTreeGraphModel"/>), so no additional asset lookup is required.
    /// </para>
    /// </summary>
    /// <param name="selection">The NodeEdit canvas selection state.</param>
    /// <param name="btreeAsset">
    ///   The active BTree asset (from <c>AiCanvasContext.AssetRef</c>).
    ///   When <see langword="null"/>, returns <see langword="null"/>.
    /// </param>
    /// <returns>
    ///   A <see cref="BTreeNodeSelection"/> with the selected node's <c>VisualId</c>,
    ///   or <see langword="null"/> when the selection is empty, multi-select, or the
    ///   selected element is not a node.
    /// </returns>
    public static IAssetSubSelection? MapSelection(
        SelectionState      selection,
        BehaviorTreeAsset?  btreeAsset)
    {
        var all = MapSelections(selection, btreeAsset);
        return all.Count == 1 ? all[0] : null;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>L0.2</c> — THE BRIDGE REPORTS, IT NEVER FILTERS.</b> 📌 <c>R-118</c> · 📄
    /// <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L0.2</c>.
    ///
    /// <para>🔴 <b>Deleted:</b> <c>if (selection.Count != 1) return null;</c> — see
    /// <c>BlueprintSelectionBridgeHelper.MapSelections</c> for why that <c>null</c> was three facts in
    /// one, and for the pan defect it caused.</para>
    ///
    /// <para>⚠⚠ <b>BTree's ORDER is load-bearing and is preserved deliberately: ATTACHMENTS FIRST.</b>
    /// 📐 The deleted code checked attachments before nodes, so a single selected attachment produced a
    /// <c>BTreePillSelection</c> rather than a node selection. ⭐ Reporting attachments first keeps that
    /// tie-break intact for the derived single — ⛔ a naive "nodes then attachments" would silently
    /// change which facet a one-pill selection resolves to.</para>
    /// </summary>
    public static IReadOnlyList<IAssetSubSelection> MapSelections(
        SelectionState      selection,
        BehaviorTreeAsset?  btreeAsset)
    {
        if (btreeAsset == null) return Array.Empty<IAssetSubSelection>();

        List<IAssetSubSelection>? mapped = null;

        // ⭐ Attachments first — the tie-break the single-selection path has always used.
        foreach (var attachment in selection.Attachments)
            (mapped ??= new List<IAssetSubSelection>()).Add(new BTreePillSelection(attachment.Value));

        // Canvas NodeId.Value == BTreeEditorNode.VisualId (BTreeNodeModel.Id contract).
        foreach (var nodeId in selection.Nodes)
            (mapped ??= new List<IAssetSubSelection>()).Add(new BTreeNodeSelection(nodeId.Value));

        return (IReadOnlyList<IAssetSubSelection>?)mapped ?? Array.Empty<IAssetSubSelection>();
    }

    /// <summary>
    /// Builds the per-frame <see cref="AiGraphCanvasWindow.AfterDraw"/> delegate that
    /// polls the canvas selection each frame and publishes the result to
    /// <paramref name="selectionStore"/>.
    /// </summary>
    /// <param name="selectionStore">
    ///   The BTree perspective's <see cref="AiSelectionStore"/>. Its
    ///   <c>ActiveSubSelection</c> is updated each frame when the active document is a
    ///   BTree.
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
            var btreeAsset = ctx.AssetRef as BehaviorTreeAsset;
            // ⭐⭐ L0.2 — the FULL set is published (R-118).
            selectionStore.ActiveSubSelections = MapSelections(ctx.View.Selection, btreeAsset);
        };
    }

    /// <summary>
    /// Builds a <see cref="BTreeFacetMapper"/> for <paramref name="asset"/> and returns it
    /// as an <see cref="IFacetDispatcher"/> ready to be forwarded to
    /// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetDispatcher"/>.
    /// Returns <see langword="null"/> when <paramref name="asset"/> is <see langword="null"/>.
    /// </summary>
    public static BTreeFacetMapper? BuildFacetDispatcher(BehaviorTreeAsset? asset)
        => asset is null ? null : new BTreeFacetMapper(asset);

    /// <summary>
    /// Builds a <see cref="BTreeFacetMapper"/> that also updates <paramref name="fqnContext"/>
    /// with the selected action/condition FQN on each <c>GetFacet</c> call.
    /// Pass the same <paramref name="fqnContext"/> to
    /// <see cref="BTreePickerDrawerFactory.BuildDrawers"/> so the blackboard-field picker
    /// filters variables by the current action's DtoType.
    /// Returns <see langword="null"/> when <paramref name="asset"/> is <see langword="null"/>.
    /// </summary>
    public static BTreeFacetMapper? BuildFacetDispatcher(
        BehaviorTreeAsset?    asset,
        BTreeFacetFqnContext? fqnContext)
        => asset is null ? null : new BTreeFacetMapper(asset, fqnContext);
}
