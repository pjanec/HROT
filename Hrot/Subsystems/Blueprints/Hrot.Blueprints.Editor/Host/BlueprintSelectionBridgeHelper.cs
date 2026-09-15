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
        var all = MapSelections(selection, bpAsset);
        return all.Count == 1 ? all[0] : null;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>L0.2</c> — THE BRIDGE REPORTS, IT NEVER FILTERS.</b> 📌 <c>R-118</c>, and 📄
    /// <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L0.2</c>: <i>"map every selected node; empty
    /// list only when nothing is selected; an unresolvable node is DROPPED, not fatal."</i>
    ///
    /// <para>🔴 <b>What was deleted here, and why it was wrong:</b> <c>if (selection.Count != 1) return
    /// null;</c>. ⛔ That one <c>null</c> meant <b>three different facts</b> — <i>nothing is
    /// selected</i>, <i>more than one thing is</i>, and <i>the node is not in this asset</i> — and a
    /// caller could not tell them apart. 📌 The design's own words: <i>"three facts flattened into
    /// one."</i></para>
    ///
    /// <para>⭐⭐ <b>And it caused the PAN defect</b> *(§2b's second sequence)*: a pan does not change
    /// the selection, but with two nodes selected this returned <c>null</c> every frame, so the store
    /// was told <i>"nothing"</i> and the panel LOST the node. ⇒ ⭐ reporting the same set every frame
    /// lets the store's elementwise guard see no change and stay silent.</para>
    ///
    /// <para>⚠ <b>An unresolvable node is DROPPED, not fatal</b> — a canvas id with no owning graph is
    /// stale, and one stale id must not discard the designer's other selections.</para>
    /// </summary>
    public static IReadOnlyList<BlueprintNodeSelection> MapSelections(
        SelectionState  selection,
        BlueprintAsset? bpAsset)
    {
        if (bpAsset == null) return Array.Empty<BlueprintNodeSelection>();

        List<BlueprintNodeSelection>? mapped = null;
        foreach (var nodeId in selection.Nodes)
        {
            // Walk asset graphs to find the graph that owns this node.
            foreach (var graph in bpAsset.Graphs)
            {
                if (!graph.Nodes.Any(n => n.Id == nodeId.Value)) continue;
                (mapped ??= new List<BlueprintNodeSelection>()).Add(
                    new BlueprintNodeSelection(graph.Id, nodeId.Value));
                break;
            }
            // ⭐ no `else` — an unresolvable id is skipped, deliberately and silently.
        }

        // ⭐ The empty case allocates nothing, so "nothing selected" costs no garbage per frame.
        return (IReadOnlyList<BlueprintNodeSelection>?)mapped ?? Array.Empty<BlueprintNodeSelection>();
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
            // ⭐⭐ L0.2 — the FULL set is published. 📌 R-118: the store decides nothing either; the
            //    "exactly one" question moves to a view's predicate (L1.4).
            selectionStore.ActiveSubSelections = MapSelections(ctx.View.Selection, bpAsset);
        };
    }
}
