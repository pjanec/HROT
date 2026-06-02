using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Renderers;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Factory that builds all NodeEdit host objects for a BTree document and returns an
/// <see cref="AiCanvasContext"/> ready to be stored in <see cref="AiDocument.ViewState"/>.
///
/// <para>
/// Construction order per opened asset:
/// <list type="number">
///   <item>Cast <see cref="IEditableAsset"/> to <see cref="BehaviorTreeAsset"/>.</item>
///   <item>Build <see cref="BTreeGraphModel"/> (wraps the asset's nodes/pills).</item>
///   <item>Build <see cref="BTreeCommandSink"/> (asset + graph model).</item>
///   <item>Build <see cref="BTreeEditorHostServices"/> with adapters from <see cref="AiEditorAdapterBundle"/>
///         + kind-specific catalog, type system, validator, and existing custom renderers.</item>
///   <item>Build <see cref="GraphView"/> (model + host services).</item>
///   <item>Return an <see cref="AiCanvasContext"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>IGraphModel note:</b> There is no pre-existing <c>BTreeGraphModel</c> in the
/// codebase.  <c>BTreeEditorNode</c> is a plain data class and does not implement
/// <c>INodeModel</c>.  <see cref="BTreeGraphModel"/> was introduced by BATCH-05 to
/// bridge this gap; the fact that the batch instructions mentioned "verify — may not
/// surface" is confirmed here.
/// </para>
/// </summary>
public static class BTreeDocumentFactory
{
    /// <summary>
    /// Builds the full host-service stack for the given BTree asset and returns a
    /// canvas context ready to be stored in <see cref="AiDocument.ViewState"/>.
    /// </summary>
    /// <param name="asset">The editable BTree asset (must be a <see cref="BehaviorTreeAsset"/>).</param>
    /// <param name="bundle">Engine adapter bundle (pickers, icons, theme, input, clipboard, diagnostics).</param>
    /// <param name="selectionStore">
    ///   Per-perspective selection store; used by <see cref="VariableBindingBadgeRenderer"/>.
    ///   When <c>null</c> a new empty store is created.
    /// </param>
    /// <param name="debugSession">Optional debug session (null while Phase 3 is not wired).</param>
    /// <param name="extraRenderers">
    ///   Optional additional custom canvas renderers to append after the built-in BTree set.
    /// </param>
    /// <returns>A populated <see cref="AiCanvasContext"/> whose <see cref="AiCanvasContext.View"/>
    ///   is ready to render on the BTree canvas.</returns>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="asset"/> is not a <see cref="BehaviorTreeAsset"/>.
    /// </exception>
    public static AiCanvasContext Build(
        IEditableAsset        asset,
        AiEditorAdapterBundle bundle,
        EditorSelectionStore? selectionStore      = null,
        IDebugSession?        debugSession        = null,
        IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null)
    {
        if (asset is null)   throw new ArgumentNullException(nameof(asset));
        if (bundle is null)  throw new ArgumentNullException(nameof(bundle));

        if (asset is not BehaviorTreeAsset btAsset)
            throw new ArgumentException(
                $"Expected {nameof(BehaviorTreeAsset)} but got {asset.GetType().Name}.",
                nameof(asset));

        // ── 1. Graph model ────────────────────────────────────────────────────
        var graphModel = new BTreeGraphModel(btAsset);

        // ── 2. Kind-specific host components ─────────────────────────────────
        var nodeCatalog  = new BTreeNodeCatalog();
        var typeSystem   = new BTreeTypeSystem();
        var validator    = new BTreeLinkValidator(graphModel);
        var commandSink  = new BTreeCommandSink(btAsset, graphModel);

        // ── 3. Custom renderers (built-in BTree set + caller extras) ──────────
        var store = selectionStore ?? new EditorSelectionStore();
        var renderers = BuildRenderers(btAsset, store, extraRenderers);

        // ── 4. Host services ──────────────────────────────────────────────────
        var hostServices = new BTreeEditorHostServices(
            nodeCatalog:     nodeCatalog,
            typeSystem:      typeSystem,
            linkValidator:   validator,
            commandSink:     commandSink,
            pickers:         bundle.PickerRegistry,
            clipboard:       bundle.ClipboardInterface,
            icons:           bundle.IconProvider,
            diagnostics:     bundle.DiagnosticsSink,
            input:           bundle.InputSource,
            theme:           bundle.EditorTheme,
            debug:           debugSession,
            customRenderers: renderers);

        // ── 5. GraphView ──────────────────────────────────────────────────────
        var view = new GraphView(
            graphModel,
            hostServices.CommandSink,
            hostServices.LinkValidator,
            hostServices.TypeSystem,
            hostServices.NodeCatalog,
            hostServices);

        return new AiCanvasContext(view, AssetKind.BTree.ToString());
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
        BehaviorTreeAsset asset,
        EditorSelectionStore store,
        IReadOnlyList<ICustomCanvasRenderer>? extra)
    {
        // Include the standard BTree custom renderers with their correct ctors.
        var list = new List<ICustomCanvasRenderer>
        {
            new SubtreeBoundaryRenderer(asset),
            new ObserverGuardBadgeRenderer(),
            new VariableBindingBadgeRenderer(store),
        };

        if (extra != null)
            list.AddRange(extra);

        return list;
    }
}
