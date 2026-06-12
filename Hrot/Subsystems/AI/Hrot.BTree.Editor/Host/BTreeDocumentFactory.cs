using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Renderers;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.UI.Action;
using NodeEditor.UI.Find;

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
    /// <param name="debugSession">
    ///   Optional NodeEdit <see cref="IDebugSession"/> (executing-node overlay in the canvas UI).
    ///   Stored in <see cref="BTreeEditorHostServices.Debug"/> and used by the canvas renderer.
    /// </param>
    /// <param name="btreeDebugSession">
    ///   Optional BTree-specific debug session.  When non-null, the runtime-overlay and
    ///   breakpoint-gutter renderers bind to it so live execution state is shown.
    ///   When null (authoring mode), both renderers report <c>IsActive==false</c>
    ///   so there is no per-frame cost.
    /// </param>
    /// <param name="breakpointManager">
    ///   Optional shared <see cref="IDataBreakpointManager"/>. When non-null, the
    ///   breakpoint-gutter renderer also draws dots for breakpoints registered in the
    ///   universal-breakpoint stack (AIE-034 / UBP-P10).
    /// </param>
    /// <param name="extraRenderers">
    ///   Optional additional custom canvas renderers to append after the built-in BTree set.
    /// </param>
    /// <param name="actionSchema">
    ///   Optional <see cref="IActionSchemaExporter"/> for populating the node catalog with
    ///   dynamic action/condition entries. When null, the catalog contains only static entries.
    /// </param>
    /// <returns>A populated <see cref="AiCanvasContext"/> whose <see cref="AiCanvasContext.View"/>
    ///   is ready to render on the BTree canvas.</returns>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="asset"/> is not a <see cref="BehaviorTreeAsset"/>.
    /// </exception>
    public static AiCanvasContext Build(
        IEditableAsset          asset,
        AiEditorAdapterBundle   bundle,
        EditorSelectionStore?   selectionStore      = null,
        IDebugSession?          debugSession        = null,
        IBTreeDebugSession?     btreeDebugSession   = null,
        IDataBreakpointManager? breakpointManager   = null,
        IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null,
        IActionSchemaExporter?  actionSchema        = null)
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
        var nodeCatalog  = new BTreeNodeCatalog(actionSchema, btAsset.BlackboardTypeName);
        var typeSystem   = new BTreeTypeSystem();
        var validator    = new BTreeLinkValidator(graphModel);
        var commandSink  = new BTreeCommandSink(btAsset, graphModel);

        // ── 3. Custom renderers (built-in BTree set + caller extras) ──────────
        var store = selectionStore ?? new EditorSelectionStore();
        var renderers = BuildRenderers(btAsset, store, btreeDebugSession, breakpointManager, extraRenderers);

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

        // Wire breakpoint manager into the host services (command-sink path via
        // BTreeBreakpointContextMenuProvider; also makes BpGutterRenderer accessible).
        if (breakpointManager != null)
            hostServices.SetBreakpointManager(breakpointManager);

        // ── 5. GraphView ──────────────────────────────────────────────────────
        var view = new GraphView(
            graphModel,
            hostServices.CommandSink,
            hostServices.LinkValidator,
            hostServices.TypeSystem,
            hostServices.NodeCatalog,
            hostServices);

        // ── BCP-F: FindBar + IEditorCommands ─────────────────────────────────
        var commands = new EditorCommandsImpl();
        var findBar  = new FindBar(view, new FindEngine(graphModel, null));
        BuiltinCommandHandlers.RegisterAll(commands, view, findBar);

        // ── Picker sources ──────────────────────────────────────────────────
        BTreePickerSources.Register(bundle.PickerRegistry, nodeCatalog);

        // Store the BehaviorTreeAsset in AssetRef so the composition root can wire
        // the selection→Inspector bridge (BTreeSelectionBridgeHelper.BuildAfterDrawAction)
        // without a kind-specific dependency in AiShared.
        return new AiCanvasContext(view, AssetKind.BTree.ToString())
        {
            AssetRef = btAsset,
            FindBar  = findBar,
            Commands = commands,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
        BehaviorTreeAsset       asset,
        EditorSelectionStore    store,
        IBTreeDebugSession?     btreeDebugSession,
        IDataBreakpointManager? breakpointManager,
        IReadOnlyList<ICustomCanvasRenderer>? extra)
    {
        // ── Registration order (per design-talk §9) ──────────────────────────
        // BeforeContent pass:
        //   1. HeatmapOverlayRenderer  — node frequency heat map (background fill)
        //   2. SubtreeBoundaryRenderer — dashed subtree rectangle (background hint)
        // AfterWires pass:
        //   3. ObserverGuardBadgeRenderer     — "OBSERVES" badge on guard connections
        //   4. VariableBindingBadgeRenderer   — variable-binding pins
        // AfterNodes pass:
        //   5. BTreeBreakpointGutterRenderer  — red dot in node gutter for armed BPs
        //   6. BTreeRuntimeOverlayRenderer    — pulsing gold outline on executing node

        var runtimeOverlay = new BTreeRuntimeOverlayRenderer();
        var gutterRenderer = new BTreeBreakpointGutterRenderer(asset);

        // Wire BTree-specific debug session into overlay and gutter.
        if (btreeDebugSession != null)
        {
            runtimeOverlay.SetSession(btreeDebugSession);
            gutterRenderer.SetSession(btreeDebugSession);
        }
        if (breakpointManager != null)
            gutterRenderer.SetManager(breakpointManager);

        var list = new List<ICustomCanvasRenderer>
        {
            new HeatmapOverlayRenderer(asset),         // BeforeContent
            new SubtreeBoundaryRenderer(asset),        // BeforeContent
            new ObserverGuardBadgeRenderer(),          // AfterWires
            new VariableBindingBadgeRenderer(store),   // AfterWires
            gutterRenderer,                            // AfterNodes
            runtimeOverlay,                            // AfterNodes (last — most ephemeral)
        };

        if (extra != null)
            list.AddRange(extra);

        return list;
    }
}
