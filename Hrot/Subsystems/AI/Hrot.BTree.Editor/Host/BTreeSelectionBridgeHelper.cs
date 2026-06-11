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
    public static BTreeNodeSelection? MapSelection(
        SelectionState      selection,
        BehaviorTreeAsset?  btreeAsset)
    {
        if (btreeAsset == null) return null;
        if (selection.Count != 1) return null;

        using var enumerator = selection.Nodes.GetEnumerator();
        if (!enumerator.MoveNext()) return null;

        // Canvas NodeId.Value == BTreeEditorNode.VisualId (BTreeNodeModel.Id contract).
        var visualId = enumerator.Current.Value;
        return new BTreeNodeSelection(visualId);
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
            var newSel     = MapSelection(ctx.View.Selection, btreeAsset);
            selectionStore.ActiveSubSelection = newSel;
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
