using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.View;

// Alias to resolve the ambiguity with Hrot.Blueprints.Editor.EditorSelectionStore.
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BF-UX1 FIX C: per-frame bridge that maps the NodeEdit canvas <see cref="SelectionState"/>
/// to a <see cref="BlueprintNodeSelection"/> and publishes it to the blueprint
/// <see cref="AiSelectionStore"/> so <c>BlueprintDetailsWindow</c> can show the drawer
/// for the selected node.
///
/// <para>
/// The core mapping logic is in the pure static <see cref="MapSelection"/> method so it can
/// be unit-tested without an ImGui context or a real document manager.
/// </para>
///
/// <para>
/// Wiring: call <see cref="BuildAfterDrawAction"/> once at startup and assign the result to
/// <see cref="AiGraphCanvasWindow.AfterDraw"/> on the Blueprint canvas window.
/// </para>
/// </summary>
public static class BlueprintSelectionBridgeHelper
{
    /// <summary>
    /// Pure mapping: given the canvas <paramref name="selection"/> and the active
    /// <paramref name="bpAsset"/>, returns a <see cref="BlueprintNodeSelection"/> when
    /// exactly one node is selected, or <see langword="null"/> otherwise (empty / multi /
    /// non-node selection, or no asset).
    ///
    /// <para>
    /// The <c>GraphId</c> stored is the asset's <c>Graph.Id</c> (the Guid used by
    /// <c>BlueprintDetailsWindow.ResolveSession</c> to look up the graph), NOT the
    /// deterministic canvas graph id.  The <c>NodeId</c> is <c>canvas NodeId.Value</c>
    /// which equals the asset <c>Node.Id</c> Guid.
    /// </para>
    /// </summary>
    /// <param name="selection">The NodeEdit canvas selection state.</param>
    /// <param name="bpAsset">
    ///   The active Blueprint asset (from <c>AiCanvasContext.AssetRef</c>).
    ///   When <see langword="null"/>, returns <see langword="null"/>.
    /// </param>
    /// <returns>
    ///   A <see cref="BlueprintNodeSelection"/> with the asset graph id and asset node id,
    ///   or <see langword="null"/> when the selection is empty, multi-select, or the node is
    ///   not found in the asset graphs.
    /// </returns>
    public static BlueprintNodeSelection? MapSelection(
        SelectionState  selection,
        BlueprintAsset? bpAsset)
    {
        if (bpAsset == null) return null;
        if (selection.Count != 1) return null;

        // Find the single selected node id.
        using var enumerator = selection.Nodes.GetEnumerator();
        if (!enumerator.MoveNext()) return null;
        var nodeId = enumerator.Current;

        // Walk asset graphs to find the graph that owns this node.
        foreach (var graph in bpAsset.Graphs)
        {
            if (graph.Nodes.Any(n => n.Id == nodeId.Value))
                return new BlueprintNodeSelection(graph.Id, nodeId.Value);
        }
        return null;
    }

    /// <summary>
    /// Builds the per-frame <see cref="AiGraphCanvasWindow.AfterDraw"/> delegate that
    /// polls the canvas selection each frame and publishes the result to
    /// <paramref name="selectionStore"/>.
    /// </summary>
    /// <param name="selectionStore">
    ///   The Blueprint perspective's <see cref="AiSelectionStore"/>. Its
    ///   <c>ActiveSubSelection</c> is updated each frame when the active document is a
    ///   Blueprint.
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
            var bpAsset = ctx.AssetRef as BlueprintAsset;
            var newSel  = MapSelection(ctx.View.Selection, bpAsset);
            selectionStore.ActiveSubSelection = newSel;
        };
    }
}
