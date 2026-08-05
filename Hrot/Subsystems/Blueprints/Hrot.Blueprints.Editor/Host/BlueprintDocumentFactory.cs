using System.Numerics;
using Hrot.Blueprints.Core;   // BlueprintJsonServices (in Hrot.Blueprints.Core namespace, Compiler assembly)
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;   // IChannelCommandCatalog
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Action;
using NodeEditor.UI.Bookmarks;
using NodeEditor.UI.Find;
using NodeEditor.UI.MiniEditors;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Factory that builds all NodeEdit host objects for a Blueprint document and returns an
/// <see cref="AiCanvasContext"/> ready to be stored in <see cref="AiDocument.ViewState"/>.
///
/// <para>
/// <b>Construction order per opened asset:</b>
/// <list type="number">
///   <item>Cast <see cref="IEditableAsset"/> to <see cref="BlueprintFileAsset"/>
///         and load its <see cref="BlueprintAsset"/>.</item>
///   <item>Build <see cref="BlueprintGraphModel"/> (projects the asset's first graph).</item>
///   <item>Build <see cref="BlueprintCommandSink"/> (asset + graph model + history).</item>
///   <item>Build <see cref="BlueprintEditorHostServices"/> with adapters from
///         <see cref="AiEditorAdapterBundle"/> + kind-specific catalog, type system, validator.</item>
///   <item>Inject the per-document <see cref="EditServiceContext"/> into the shared
///         <see cref="EditService"/> so node drawers route edits through this document's history.</item>
///   <item>Build <see cref="GraphView"/> (model + host services).</item>
///   <item>Return an <see cref="AiCanvasContext"/>.</item>
/// </list>
/// </para>
/// </summary>
public static class BlueprintDocumentFactory
{
    /// <summary>
    /// Builds the full host-service stack for the given Blueprint asset and returns a
    /// canvas context ready to be stored in <see cref="AiDocument.ViewState"/>.
    /// </summary>
    /// <param name="asset">
    ///   The editable Blueprint asset (must be a <see cref="BlueprintFileAsset"/>).
    /// </param>
    /// <param name="bundle">
    ///   Engine adapter bundle (pickers, icons, theme, input, clipboard, diagnostics).
    /// </param>
    /// <param name="editService">
    ///   The shared <see cref="EditService"/> created by <c>EditorSubsystem</c>; its
    ///   <see cref="EditService.Context"/> will be updated to point to this document's
    ///   <see cref="CommandHistory"/> and dirty-callback so node drawers stay in sync.
    /// </param>
    /// <param name="paletteRegistry">
    ///   Node-kind palette registry from <c>BlueprintEditorBootstrap.CreatePaletteRegistry()</c>.
    ///   When null, an empty <see cref="NodeKindRegistry"/> is used (authoring palette disabled).
    /// </param>
    /// <param name="extraRenderers">
    ///   Optional extra custom canvas renderers appended after the built-in Blueprint set.
    /// </param>
    /// <param name="channelCommands">
    ///   Optional channel-command catalog forwarded to <see cref="BlueprintGraphModel"/> so that
    ///   <see cref="ChannelCommandNode"/>s project their parameter data-IN pins from the matching
    ///   catalog entry's params type.  When null, channel-command nodes are exec-only.
    /// </param>
    /// <param name="peerAssetCatalog">
    ///   Optional peer-source used to build a peer-signature lookup delegate for
    ///   <see cref="CallPeerBlueprintNode"/> pin projection.  When non-null, the factory
    ///   constructs a delegate that parses each peer blueprint's <see cref="BlueprintSignature"/>
    ///   from disk (via <see cref="BlueprintSignatureParser"/>), mirroring
    ///   <c>QuickReloadService.BuildSiblingSignatures</c>.  When null,
    ///   <see cref="CallPeerBlueprintNode"/>s fall back to static exec+Return pins.
    /// </param>
    /// <param name="behaviorActions">
    ///   AN7 — optional unified behavior-action catalog forwarded (alongside
    ///   <paramref name="channelCommands"/>) to <see cref="BlueprintGraphModel"/> and
    ///   <see cref="BlueprintCommandSink"/> so that non-channel <see cref="ChannelCommandNode"/>s
    ///   (i.e. those with <c>ActionFqn</c> set) project their parameter data-IN pins from the
    ///   matching catalog entry's params type.  When null, non-channel action nodes are exec-only.
    /// </param>
    /// <param name="debugSession">
    ///   Optional <see cref="IBlueprintDebugSession"/> for debug visualisation and
    ///   editor commands (Toggle Breakpoint on F9). When non-null, the factory wires
    ///   a <see cref="BlueprintDebugToNodeEditAdapter"/> so NodeEdit's native
    ///   <c>NodeRenderer</c> draws breakpoint markers, execution overlays, and the
    ///   pausing pulse automatically. When null, all debug features are inactive with
    ///   zero per-frame cost.
    /// </param>
    /// <returns>
    ///   A populated <see cref="AiCanvasContext"/> whose <see cref="AiCanvasContext.View"/>
    ///   is ready to render on the Blueprint canvas.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="asset"/> is not a <see cref="BlueprintFileAsset"/>.
    /// </exception>
    public static AiCanvasContext Build(
        IEditableAsset          asset,
        AiEditorAdapterBundle   bundle,
        EditService?            editService     = null,
        NodeKindRegistry?       paletteRegistry = null,
        IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null,
        IChannelCommandCatalog? channelCommands = null,
        BlueprintPeerSource?    peerAssetCatalog = null,
        ActionCatalog.IBehaviorActionCatalog? behaviorActions = null,
        IBlueprintDebugSession? debugSession    = null)
    {
        if (asset  is null) throw new ArgumentNullException(nameof(asset));
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        if (asset is not BlueprintFileAsset bpFile)
            throw new ArgumentException(
                $"Expected {nameof(BlueprintFileAsset)} but got {asset.GetType().Name}.",
                nameof(asset));

        // Resolve the underlying BlueprintAsset (loaded from the .bp.json file on demand).
        var bpAsset = LoadAsset(bpFile);

        // ── 1. Resolve the event graph (fall back to first graph) ─────────────
        var graph = bpAsset.Graphs.FirstOrDefault(g => g.Kind == GraphKind.Event)
                 ?? bpAsset.Graphs.FirstOrDefault()
                 ?? throw new InvalidOperationException(
                        $"Blueprint asset '{bpAsset.Name}' has no graphs.");

        // CF-6: Register all graphs with the debug session so stepping
        // (ExecSuccessors) can compute next exec node(s) from the graph structure.
        if (debugSession is BlueprintDebugSession bpSession)
        {
            foreach (var g in bpAsset.Graphs)
                bpSession.RegisterGraph(g);
        }

        // ── 3. Kind-specific host components ─────────────────────────────────
        var kindRegistry = paletteRegistry ?? new NodeKindRegistry();

        // ── 2. Graph model (pass registry for pin hydration of JSON-loaded assets) ──
        // The channel-command catalog (when supplied) lets ChannelCommandNodes project their
        // parameter data-IN pins from the matching catalog entry's params type (projection-only).
        // The peer-signature lookup (when peerAssetCatalog is non-null) lets CallPeerBlueprintNodes
        // project typed argument pins from the peer's exported function signature.
        // The editor registry is created first so BlueprintGraphModel can use it to expose
        // type-zero Default values on unset In-data pins (FIX-A: BF-BATCH-0607).
        Func<Guid, BlueprintSignature?>? peerLookup = BuildPeerSignatureLookup(peerAssetCatalog);
        var builtinRegistry = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        // Register FixedString32/64 as string-editor types (unmanaged; authored as plain text).
        builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
        builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString64), new StringPinEditor());
        // Wrap with the enum-sentinel interceptor so any TypeKey starting with "global::" returns
        // an EnumPinEditor backed by BlueprintEnumValueProvider (AN6).
        // The inner registry handles all non-enum (primitive / FixedString) TypeKeys.
        // DO NOT edit PinDefaultValueEditorRegistry.CreateWithBuiltins (framework contract).
        var enumProvider   = new BlueprintEnumValueProvider();
        IPinDefaultValueEditorRegistry editorRegistry =
            new EnumSentinelPinEditorRegistry(builtinRegistry, enumProvider);
        var graphModel = new BlueprintGraphModel(bpAsset, graph, kindRegistry, channelCommands, peerLookup,
            editorRegistry, enumProvider, behaviorActions);
        var nodeCatalog  = new BlueprintNodeCatalog(kindRegistry);
        var typeSystem   = new BlueprintTypeSystem(editorRegistry);
        var validator    = new BlueprintLinkValidator(graphModel, typeSystem);
        var history      = new CommandHistory();

        // Dirty-marking callback: marks the AiDocument dirty via the asset's Changed event.
        // We track dirtyness on the BlueprintAsset level here and propagate it up.
        var markDirty = (BlueprintAsset _) => { bpFile.MarkDirty(); };

        // ── 4. EditService context (AIE-049) ──────────────────────────────────
        // Inject a per-document context into the shared EditService so node drawers
        // route property edits through this document's CommandHistory.
        //
        // Data-driven view refresh: the canvas graph model is derived state projected from the
        // asset. A Details-panel edit that changes a node's projected pin shape (e.g. a struct
        // field expansion) must re-project it — but the drawer must NOT reach across to the canvas
        // window. Instead the drawer emits a structural-change signal (EditService.NotifyStructureChanged)
        // and the composition root (here) subscribes the derived view so it rebuilds itself.
        // BP-11 (Q22-A1/C2): property edits go on the SAME UndoStack as structural ones, so Ctrl+Z
        // reverses a mixed sequence in the order the designer performed it. The view does not exist
        // yet (§7 below), so the transport closes over this local and is assigned before any edit
        // can be issued — a delegate, not a GraphView reference, keeps EditService canvas-agnostic
        // exactly as onStructureChanged already does.
        GraphView? undoTarget = null;
        Action<string, Action, Action> recordUndoable = (label, apply, undo) =>
        {
            if (undoTarget is { } v)
                v.Execute(
                    new BlueprintEditCommand(label, apply),
                    new BlueprintEditCommand(label, undo),
                    label);
            else
                apply();   // no view yet — still perform the edit, just without a stack entry
        };

        var ctx = new EditServiceContext(
            history, markDirty,
            onStructureChanged: _ => graphModel.RebuildAndNotify(),
            recordUndoable: recordUndoable);
        if (editService != null)
            editService.Context = ctx;

        // Create a local EditService for the command sink even when the shared one is null.
        var localEditService = editService ?? new EditService { Context = ctx };

        // BF-UX1 FIX B: pass channelCommands so BlueprintCommandSink.ApplyPinIds threads it into
        // NodePinSchema.GetCanonicalPins — ChannelCommandNode then projects param data-IN pins.
        var commandSink = new BlueprintCommandSink(
            bpAsset, graph, graphModel, nodeCatalog, validator, history,
            localEditService, markDirty, channelCommands: channelCommands,
            enumProvider: enumProvider, behaviorActions: behaviorActions);

        // ── 5. Custom renderers (Blueprint set + caller extras) ───────────────
        var renderers = BuildRenderers(bpAsset, extraRenderers);

        // ── 6. Host services ──────────────────────────────────────────────────
        var hostServices = new BlueprintEditorHostServices(
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
            customRenderers: renderers);

        // Bridge IBlueprintDebugSession -> NodeEdit IDebugSession so NodeRenderer
        // natively draws breakpoint markers and execution overlays.
        if (debugSession != null)
        {
            var adapter = new BlueprintDebugToNodeEditAdapter(debugSession, bpAsset.AssetId, graph.Id);
            hostServices.SetDebugSession(adapter);
        }

        // The NodeEdit CanvasRenderer natively handles the node context menu
        // (right-click → Toggle Breakpoint) via NodeEdit's HoverKind.Node + IEditorCommands.

        // ── 7. GraphView ──────────────────────────────────────────────────────
        var view = new GraphView(
            graphModel,
            hostServices.CommandSink,
            hostServices.LinkValidator,
            hostServices.TypeSystem,
            hostServices.NodeCatalog,
            hostServices);

        // BP-11: close the loop on the transport declared at §4 — from here on, a drawer edit lands
        // on this view's UndoStack alongside the structural edits.
        undoTarget = view;

        // ── 8. Picker sources (BCP-E) ─────────────────────────────────────────
        BlueprintPickerSources.Register(bundle.PickerRegistry, nodeCatalog, bpAsset);

        // ── 9. FindBar + IEditorCommands (BCP-F) ─────────────────────────────
        var commands = new EditorCommandsImpl();
        var findBar  = new FindBar(view, new FindEngine(graphModel, null));
        BuiltinCommandHandlers.RegisterAll(commands, view, findBar);

        // Register debug commands (Toggle Breakpoint etc.)
        var reg = new CommandRegistration(commands);
        var bpDebugSession = debugSession; // capture for closure
        reg.Add(
            CommandCatalog.ToggleBreakpoint,
            "Toggle Breakpoint", "Debug",
            _ =>
            {
                var dbg = hostServices.Debug;
                if (dbg == null) return;
                foreach (var nodeId in view.Selection.Nodes)
                    dbg.ToggleBreakpoint(nodeId);
            },
            isEnabled: () => bpDebugSession != null
                && view.Selection.Nodes.Any(n =>
                    bpDebugSession.IsNodeBreakpointable(bpAsset.AssetId, graph.Id, n.Value)),
            description: "Toggles a breakpoint on the selected node. Requires a compiled DebugMap entry (exec nodes only).",
            defaultKey: new KeyBinding(EditorKey.F9, KeyModifiers.None));

        // BCP-BATCH-02-FIX Task 3: My Blueprint "+" → Create Variable.
        // The MyBlueprintPanel "+ Variable" item invokes "editor.create-variable"; register a
        // real handler that appends a VariableDecl to the asset so it shows up in the
        // Variables section. (Was a no-op since BATCH-13.)
        RegisterCreateVariableCommand(commands, bpAsset, () => bpFile.MarkDirty());

        // BP-12a: My Blueprint → right-click a variable → "Get"/"Set". The context menu has always
        // invoked editor.create-variable-get / -set, but nothing registered them, so the most-used
        // motion in Unreal-style authoring silently did nothing. (The drag-to-canvas path already
        // worked — CanvasRenderer.PlaceVariableNode handles the drop — so this closes the gap for
        // the menu, which is the only route when the panel is docked away from the canvas.)
        RegisterVariableGetSetCommands(commands, view, bpAsset);

        // BP-60: the canvas's "Promote to Variable" modal. Until now the modal opened, took a name,
        // and applied a GraphCommand this sink has no case for — landing on the default: arm that
        // returns success. Nothing happened, and it reported that it worked.
        RegisterPromoteToVariableCommand(
            commands, view, bpAsset, nodeCatalog.KindRegistry, () => bpFile.MarkDirty());

        // BP-23a: copy/cut/paste/duplicate. All four command ids were declared in CommandCatalog
        // with no handler anywhere in the repo, and the canvas menu's Paste was hard-disabled.
        RegisterClipboardCommands(
            commands, view, graph, hostServices.Clipboard, () => bpFile.MarkDirty());

        // ── 10. Bookmarks (navigation aids) ──────────────────────────────────
        // Per-document BookmarkStore + the shared NodeEdit set/jump commands (Ctrl+1..9
        // jump, Ctrl+Shift+1..9 set — see BookmarkCommands). Pumped automatically by
        // AiGraphCanvasWindow's per-frame EditorHotkeyDispatcher since they're registered
        // on this document's `commands` (same object as FindBar/Toggle-Breakpoint/etc).
        // The store is exposed via AiCanvasContext.Bookmarks so the composition root can
        // draw the off-screen edge-marker overlay (BlueprintEditorBootstrap.DrawBookmarkEdgeMarkers)
        // and, optionally, a Bookmarks panel window.
        var bookmarkStore = new BookmarkStore();
        var bookmarkIndicators = new EditorIndicatorsImpl(new ToastQueue());
        BookmarkCommands.RegisterAll(
            commands, view, bookmarkStore, bookmarkIndicators,
            // The Blueprint editor renders a single graph per open document (no in-document
            // tab switching between the asset's other graphs yet), so every bookmark's
            // TargetGraph is always this view's own graph id — cross-graph jump is a no-op.
            navigateToGraph: _ => { });

        // Store the BlueprintAsset in AssetRef so the composition root can retarget
        // My Blueprint / Details / Variables windows without a kind-specific dependency.
        return new AiCanvasContext(view, AssetKind.Blueprint.ToString())
        {
            AssetRef  = bpAsset,
            FindBar   = findBar,
            Commands  = commands,
            Bookmarks = bookmarkStore,
        };
    }

    // ── Variable Get/Set placement commands (BP-12a) ──────────────────────────

    /// <summary>
    /// Registers <c>editor.create-variable-get</c> and <c>editor.create-variable-set</c>, which the
    /// My Blueprint panel's variable context menu invokes with the variable id in
    /// <c>ctx.Args["itemId"]</c> (see <c>MyBlueprintContextMenu.DrawVariableMenu</c>).
    ///
    /// <para>
    /// The node is created through <c>view.Execute</c>, so placing one is undoable like any other
    /// structural edit (BP-11's single stack). Placement goes at <c>ctx.CanvasPos</c> when the caller
    /// supplied one, otherwise the centre of the current viewport — a menu invocation carries no
    /// mouse position, and dropping the node at the graph origin would put it off-screen.
    /// </para>
    ///
    /// <para>Exposed <c>internal</c> so tests can drive both commands without ImGui.</para>
    /// </summary>
    internal static void RegisterVariableGetSetCommands(
        EditorCommandsImpl commands,
        GraphView          view,
        BlueprintAsset     asset)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asset);

        var reg = new CommandRegistration(commands);

        reg.Add(
            "editor.create-variable-get", "Get Variable", "Add",
            ctx => PlaceVariableNode(view, asset, ctx, isGet: true),
            description: "Places a Get node for the selected variable.");

        reg.Add(
            "editor.create-variable-set", "Set Variable", "Add",
            ctx => PlaceVariableNode(view, asset, ctx, isGet: false),
            description: "Places a Set node for the selected variable.");
    }

    /// <summary>
    /// Places a <c>GetVariableNode</c>/<c>SetVariableNode</c> for the variable named in
    /// <paramref name="ctx"/>. Mirrors <c>CanvasRenderer.PlaceVariableNode</c>'s kind ids and
    /// property bag so both routes produce an identical node.
    /// </summary>
    private static void PlaceVariableNode(
        GraphView view, BlueprintAsset asset, EditorCommandContext ctx, bool isGet)
    {
        var variableId = ctx.Args is not null && ctx.Args.TryGetValue("itemId", out var raw)
            ? raw as string
            : null;

        // No variable in the context means the menu was invoked without a selection; do nothing
        // rather than placing a node bound to nothing.
        if (string.IsNullOrEmpty(variableId)) return;

        // Prefer the declaration's own name for the display property; fall back to the id so a
        // variable that has since been renamed still places something meaningful.
        var variableName = asset.Variables
            .FirstOrDefault(v => string.Equals(v.Name, variableId, StringComparison.Ordinal))
            ?.Name ?? variableId!;

        var position = ctx.CanvasPos ?? ViewportCentre(view);

        var kind  = new NodeKindKey(isGet ? "Util.GetVar" : "Util.SetVar");
        var props = new Dictionary<string, object?>
        {
            ["VariableId"]   = variableId,
            ["VariableName"] = variableName,
        };

        var cb = new CommandBuilder(view.Model);
        var (fwd, inv) = cb.AddNode(kind, position, props);
        view.Execute(fwd, inv, isGet ? "Add Get Variable" : "Add Set Variable");
    }

    // ── Clipboard: copy / cut / paste / duplicate (BP-23a) ────────────────────

    /// <summary>
    /// BP-23a — registers <c>editor.copy</c>, <c>editor.cut</c>, <c>editor.paste</c> and
    /// <c>editor.duplicate</c>. All four ids were declared in <c>CommandCatalog</c> with no handler
    /// anywhere in the repo, and the canvas menu's Paste entry was hard-disabled.
    ///
    /// <para>
    /// <b>Host-side, like BP-60.</b> A clipboard entry is a list of asset <see cref="Node"/>s, and
    /// paste must add them <i>fully built</i>. Routing through <c>GraphCommand.AddNode</c> would
    /// rebuild each node from its kind and then re-apply only the properties
    /// <c>ApplyInitialProperties</c> knows — 8 node kinds of 50 — silently dropping the
    /// configuration of the other 42. <see cref="BlueprintEditCommand"/> carries the built nodes
    /// straight onto the graph instead, and gives the whole paste one undo entry.
    /// </para>
    ///
    /// <para>Exposed <c>internal</c> so tests can drive all four without ImGui.</para>
    /// </summary>
    /// <param name="clipboard">
    /// The host clipboard. Copy/cut/paste use it; <b>duplicate deliberately does not</b>, so
    /// duplicating never clobbers what the designer had copied.
    /// </param>
    internal static void RegisterClipboardCommands(
        EditorCommandsImpl commands,
        GraphView          view,
        Graph              graph,
        IClipboard?        clipboard,
        Action?            markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(graph);

        var reg = new CommandRegistration(commands);

        reg.Add(
            NodeEditor.Core.CommandCatalog.Copy, "Copy", "Edit",
            _ => CopySelection(view, graph, clipboard),
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Copies the selected nodes.",
            defaultKey: new KeyBinding(EditorKey.C, KeyModifiers.Ctrl));

        reg.Add(
            NodeEditor.Core.CommandCatalog.Cut, "Cut", "Edit",
            _ =>
            {
                // Copy first: the delete is undoable, so a failed copy must not lose the nodes.
                // Deleting through the registered command reuses BP-59's undoable path (which also
                // removes the orphaned links) rather than re-deriving it here.
                if (CopySelection(view, graph, clipboard))
                    commands.Invoke(NodeEditor.Core.CommandCatalog.DeleteSelection);
            },
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Copies the selected nodes and deletes them.",
            defaultKey: new KeyBinding(EditorKey.X, KeyModifiers.Ctrl));

        reg.Add(
            NodeEditor.Core.CommandCatalog.Paste, "Paste", "Edit",
            ctx => PasteFrom(view, graph, clipboard?.GetText(), ctx.CanvasPos, markDirty),
            isEnabled: () => BlueprintClipboard.TryParse(clipboard?.GetText(), out _),
            description: "Pastes previously copied nodes.",
            defaultKey: new KeyBinding(EditorKey.V, KeyModifiers.Ctrl));

        reg.Add(
            NodeEditor.Core.CommandCatalog.Duplicate, "Duplicate", "Edit",
            _ => PasteFrom(view, graph, BlueprintClipboard.Copy(graph, SelectedNodeIds(view)),
                           targetPosition: null, markDirty),
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Duplicates the selected nodes in place.",
            defaultKey: new KeyBinding(EditorKey.D, KeyModifiers.Ctrl));
    }

    private static IReadOnlyCollection<Guid> SelectedNodeIds(GraphView view)
        => view.Selection.Nodes.Select(n => n.Value).ToList();

    /// <summary>Writes the selection to the clipboard; false when there was nothing to write.</summary>
    private static bool CopySelection(GraphView view, Graph graph, IClipboard? clipboard)
    {
        if (clipboard is null) return false;

        var text = BlueprintClipboard.Copy(graph, SelectedNodeIds(view));
        if (text is null) return false;

        clipboard.SetText(text);
        return true;
    }

    /// <summary>
    /// Adds a clipboard fragment to the graph as one undoable step, and leaves the pasted nodes
    /// selected — so paste-then-drag works, and a second Ctrl+V after a paste duplicates what was
    /// just pasted rather than the original.
    /// </summary>
    /// <param name="targetPosition">
    /// Graph-space position for the fragment's top-left corner (the canvas passes the cursor).
    /// Null pastes at the original coordinates plus a small offset, so the copy is visibly distinct
    /// from what it was copied from.
    /// </param>
    private static void PasteFrom(
        GraphView view, Graph graph, string? text, Vector2? targetPosition, Action? markDirty)
    {
        if (!BlueprintClipboard.TryParse(text, out var payload)) return;

        var offset = targetPosition is { } target
            ? target - BlueprintClipboard.TopLeftOf(payload)
            : BlueprintClipboard.DefaultPasteOffset;

        var fragment = BlueprintClipboard.Rehydrate(payload, offset);
        if (fragment.Nodes.Count == 0) return;

        var pastedIds = fragment.Nodes.Select(n => new NodeId(n.Id)).ToList();

        void Apply()
        {
            foreach (var node in fragment.Nodes) graph.Nodes.Add(node);
            foreach (var link in fragment.Links) graph.Links.Add(link);
        }

        void Undo()
        {
            foreach (var link in fragment.Links) graph.Links.Remove(link);
            foreach (var node in fragment.Nodes) graph.Nodes.Remove(node);
        }

        var label = fragment.Nodes.Count == 1 ? "Paste Node" : $"Paste {fragment.Nodes.Count} Nodes";
        view.Execute(
            new BlueprintEditCommand(label, Apply),
            new BlueprintEditCommand(label, Undo),
            label);

        view.Selection.ReplaceWith(pastedIds.Select(SelectionEntry.OfNode).ToArray());
        markDirty?.Invoke();
    }

    // ── Promote to Variable (BP-60) ───────────────────────────────────────────

    /// <summary>Horizontal offset of the node promotion places beside the promoted pin's owner.</summary>
    private const float PromoteNodeOffsetX = 240f;

    /// <summary>
    /// BP-60 — registers <c>editor.promote-to-variable</c>, which the canvas's promote modal invokes
    /// with the pin and the entered name.
    ///
    /// <para>
    /// <b>Why a host command and not a sink case.</b> <c>GraphCommand.PromoteToVariable</c> is a
    /// single opaque command: whichever sink implements it allocates the new node's id internally,
    /// so a caller cannot write the inverse — which is exactly why BP-02 left this one site on
    /// <c>Commands.Apply</c>, and why it hit the sink's <c>default:</c> arm and silently reported
    /// success. Promotion is not one primitive anyway; it is <i>declare a variable</i> + <i>place a
    /// node</i> + <i>link it</i>. Composing it here from commands the sink already implements keeps
    /// BP-11's invariant intact — <b>the sink applies, the stack records</b> — and makes the whole
    /// gesture one undo entry, because the caller owns every id in it.
    /// </para>
    ///
    /// <para>Exposed <c>internal</c> so tests can drive the whole gesture without ImGui.</para>
    /// </summary>
    /// <param name="commands">The editor command catalog.</param>
    /// <param name="view">The document's view; promotion is recorded on its undo stack.</param>
    /// <param name="asset">The asset that receives the new <see cref="VariableDecl"/>.</param>
    /// <param name="kindRegistry">
    /// Used to project the new node's canonical pins so the link can name the right one.
    /// </param>
    /// <param name="markDirty">Invoked when a promotion succeeds.</param>
    internal static void RegisterPromoteToVariableCommand(
        EditorCommandsImpl commands,
        GraphView          view,
        BlueprintAsset     asset,
        NodeKindRegistry?  kindRegistry = null,
        Action?            markDirty    = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asset);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.PromoteToVariable,
            "Promote to Variable", "Refactor",
            ctx => PromoteToVariable(view, asset, kindRegistry, markDirty, ctx),
            description: "Creates a Blueprint variable from this pin and wires it up.");
    }

    /// <summary>
    /// Performs the promotion described by <paramref name="ctx"/>'s arguments
    /// (<c>pinId</c>, <c>name</c>, <c>isLocal</c>, <c>categoryPath</c>).
    ///
    /// <para>
    /// An <b>input</b> pin gets a Get node placed to its left, feeding it; an <b>output</b> pin gets
    /// a Set node to its right, fed by it. Same shape as the reference implementation in
    /// <c>NodeEditor.Demo</c>'s <c>FakeCommandSink</c> — which was the only implementation that
    /// existed.
    /// </para>
    /// </summary>
    private static void PromoteToVariable(
        GraphView            view,
        BlueprintAsset       asset,
        NodeKindRegistry?    kindRegistry,
        Action?              markDirty,
        EditorCommandContext ctx)
    {
        if (ReadArg(ctx, "pinId") is not PinId pinId) return;

        var pin = view.Model.FindPin(pinId);
        // Exec pins carry no value, so there is nothing to promote.
        if (pin is null || pin.Kind != PinKind.Data) return;

        var owner = view.Model.FindNode(pin.OwnerNodeId);
        if (owner is null) return;

        var rawName = ReadArg(ctx, "name") as string;
        // The modal disables Confirm on a blank name, but the guard belongs here too.
        if (string.IsNullOrWhiteSpace(rawName)) return;

        // Never collide with an existing declaration: the designer asked to promote, not to
        // overwrite, and CreateVariable would reject a duplicate outright.
        var name     = MakeUniqueName(asset.Variables.Select(v => v.Name), rawName!.Trim());
        var category = ReadArg(ctx, "categoryPath") as string;

        // BP-57: the data model has no per-graph variable scope, so "Promote to Local Variable"
        // can only produce a Blueprint-scoped one. Say so rather than silently reinterpreting it.
        if (ReadArg(ctx, "isLocal") is true)
            view.Host.Diagnostics?.Log(DiagnosticSeverity.Info,
                $"Promote: local variables are not supported yet (BP-57); '{name}' was created "
                + "as a Blueprint variable.");

        bool isInput = pin.Direction == PinDirection.Input;

        var decl = new VariableDecl
        {
            Id       = Guid.NewGuid(),
            Name     = name,
            Type     = new BlueprintTypeRef { TypeId = PromotedTypeId(pin) },
            Category = string.IsNullOrWhiteSpace(category) ? null : category!.Trim(),
        };

        // The variable id is stored in the "var:" form the compiler tolerates and the My Blueprint
        // drag path already emits, so a promoted node is indistinguishable from a dragged one.
        var variableId = $"var:{decl.Id:D}";
        var nodeKind   = new NodeKindKey(isInput ? "Util.GetVar" : "Util.SetVar");

        // Project the new node's pins here so the link can name the right one. The sink stamps
        // InitialProperties["PinIds"] onto canonical pins in inputs-then-outputs order (ApplyPinIds),
        // so mirroring that order is what makes the ids line up.
        var probe = isInput
            ? (Node)new GetVariableNode { VariableId = variableId }
            : new SetVariableNode { VariableId = variableId };
        var canonical = NodePinSchema.GetCanonicalPins(probe, kindRegistry, asset);
        var ordered   = canonical.Where(p => p.Direction == "In")
            .Concat(canonical.Where(p => p.Direction != "In"))
            .ToList();

        // The Value pin faces the promoted pin: a Get node feeds it (Value out), a Set node
        // receives it (Value in).
        var wantDirection = isInput ? "Out" : "In";
        var valueIndex = ordered.FindIndex(p =>
            !p.IsExec
            && p.Direction == wantDirection
            && string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase));
        if (valueIndex < 0) return;

        var pinIds = ordered.Select(_ => IdGenerator.NewPinId()).ToList();
        var nodeId = IdGenerator.NewNodeId();
        var linkId = IdGenerator.NewLinkId();

        var position = owner.Position
            + new Vector2(isInput ? -PromoteNodeOffsetX : PromoteNodeOffsetX, 0f);

        var props = new Dictionary<string, object?>
        {
            ["VariableId"]   = variableId,
            ["VariableName"] = name,
            ["PinIds"]       = pinIds,
        };

        var (from, to) = isInput
            ? (pinIds[valueIndex], pinId)
            : (pinId, pinIds[valueIndex]);

        // Three steps, one undo entry. CommandBuilder.Batch reverses the inverses, so undo runs
        // unlink → remove node → undeclare, which is the only order that leaves no dangling
        // reference at any point.
        var steps = new List<(GraphCommand Forward, GraphCommand Inverse)>
        {
            (new BlueprintEditCommand("Declare Variable",   () => asset.Variables.Add(decl)),
             new BlueprintEditCommand("Undeclare Variable", () => asset.Variables.Remove(decl))),

            (new GraphCommand.AddNode(nodeId, nodeKind, position, props),
             new GraphCommand.RemoveNodes(new[] { nodeId })),

            (new GraphCommand.AddLink(linkId, from, to),
             new GraphCommand.RemoveLinks(new[] { linkId })),
        };

        var cb = new CommandBuilder(view.Model);
        var (forward, inverse) = cb.Batch("Promote to Variable", steps);
        view.Execute(forward, inverse, "Promote to Variable");

        markDirty?.Invoke();
    }

    /// <summary>
    /// The declared type for a promoted pin. An untyped (wildcard) pin falls back to
    /// <see cref="BlueprintTypeSystem.Bool"/> — the same default the "+ Variable" quick-add uses —
    /// so promotion always produces a well-formed declaration the designer can retype.
    /// </summary>
    private static string PromotedTypeId(IPinModel pin)
        => pin.Type is { IsEmpty: false } type ? type.Id : BlueprintTypeSystem.Bool;

    private static object? ReadArg(EditorCommandContext ctx, string key)
        => ctx.Args is not null && ctx.Args.TryGetValue(key, out var value) ? value : null;

    /// <summary>Centre of the visible canvas in graph space; graph origin before the first layout.</summary>
    private static Vector2 ViewportCentre(GraphView view)
    {
        var size = view.Viewport.CanvasScreenSize;
        if (size.X <= 0f || size.Y <= 0f) return Vector2.Zero;
        return view.Viewport.ScreenToGraph(view.Viewport.CanvasScreenOrigin + size * 0.5f);
    }

    // ── Create-variable command (BCP-BATCH-02-FIX Task 3) ─────────────────────

    /// <summary>
    /// Registers the <c>editor.create-variable</c> command so the My Blueprint panel's
    /// "+ Variable" action appends a new <see cref="VariableDecl"/> to <paramref name="asset"/>
    /// and marks the document dirty. The new variable gets a unique default name and a
    /// <c>System.Boolean</c> type (the user can retype it in the Variables panel).
    /// <para>Exposed <c>internal</c> so tests can verify the create path without ImGui.</para>
    /// </summary>
    internal static void RegisterCreateVariableCommand(
        EditorCommandsImpl commands,
        BlueprintAsset     asset,
        Action             markDirty)
    {
        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateVariable,
            "Create Variable", "Add",
            _ => AddVariable(asset, markDirty),
            description: "Add a new variable to this blueprint.");
    }

    /// <summary>
    /// Registers the <c>editor.create-variable</c> command so that invoking it opens the
    /// variable-create modal (name + type) rather than immediately creating a variable with
    /// a default name. The modal's confirm callback is responsible for calling
    /// <see cref="CreateVariable"/>. Used in production wiring; the parameterless-create
    /// overload remains for headless tests of the create path.
    /// </summary>
    /// <param name="commands">The editor command catalog.</param>
    /// <param name="openModal">Opens the variable-create modal (e.g. <c>modal.Open</c>).</param>
    public static void RegisterCreateVariableCommand(
        EditorCommandsImpl commands,
        Action             openModal)
    {
        ArgumentNullException.ThrowIfNull(openModal);
        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateVariable,
            "Create Variable", "Add",
            _ => openModal(),
            description: "Add a new variable to this blueprint.");
    }

    /// <summary>
    /// The "+ Variable" quick-add path (no modal): appends a new <see cref="VariableDecl"/>
    /// with an auto-generated unique default name. Unlike <see cref="CreateVariable"/> this
    /// path picks a free name itself (e.g. <c>NewVar</c>, <c>NewVar1</c>, …) so repeated
    /// clicks never collide; it never rejects. Returns the created declaration.
    /// </summary>
    internal static VariableDecl AddVariable(BlueprintAsset asset, Action? markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var name = MakeUniqueVariableName(asset, "NewVar");
        var decl = CreateVariable(asset, name, BlueprintTypeSystem.Bool, markDirty);
        // MakeUniqueVariableName guarantees a free name, so CreateVariable cannot reject here.
        return decl!;
    }

    /// <summary>
    /// Headless-testable create path used by the variable-create modal: appends a new
    /// <see cref="VariableDecl"/> with the supplied <paramref name="name"/> and
    /// <paramref name="typeId"/> to the asset and invokes the dirty callback.
    /// <para>
    /// <b>Rejects</b> (returns <see langword="null"/>, adds nothing) when
    /// <paramref name="name"/> is blank/whitespace or collides (case-insensitively) with an
    /// existing variable name. The caller (modal) is responsible for warning the user up
    /// front and disabling Confirm on collision; this method is the authoritative guard so
    /// the invariant holds even if a caller skips that check. No silent numeric suffixing.
    /// </para>
    /// </summary>
    /// <param name="asset">The asset to append the variable to.</param>
    /// <param name="name">
    /// The desired variable name. Blank/whitespace or a duplicate name causes rejection.
    /// </param>
    /// <param name="typeId">
    /// The variable's type id (e.g. <c>"System.Single"</c>); blank falls back to
    /// <see cref="BlueprintTypeSystem.Bool"/>.
    /// </param>
    /// <param name="markDirty">Optional dirty-marking callback (invoked only on success).</param>
    /// <param name="capacity">
    /// FC-2/LV-4: <c>&gt; 0</c> declares a FIXED-LIST variable of this capacity (the
    /// discriminator is <see cref="BlueprintTypeRef.Capacity"/>, never IsArray — F7);
    /// <c>0</c> (default) declares an ordinary scalar. A list of a managed element type
    /// (<c>System.String</c>) is rejected here (the compiler's BP1500 is the authoritative
    /// backstop for hand-edited JSON).
    /// </param>
    /// <param name="initialLength">
    /// FC-2/LV-4: the list's seeded logical length; clamped into <c>[0, capacity]</c>
    /// (BP1504 remains the compile-time guard for out-of-range hand-edited JSON).
    /// Ignored for scalars.
    /// </param>
    /// <returns>The created declaration, or <see langword="null"/> if the name was rejected.</returns>
    internal static VariableDecl? CreateVariable(
        BlueprintAsset asset,
        string         name,
        string         typeId,
        Action?        markDirty     = null,
        int            capacity      = 0,
        int            initialLength = 0)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // Reject blank/whitespace and duplicate names rather than silently renaming.
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        if (IsDuplicateVariableName(asset, trimmed))
            return null;

        var finalType = string.IsNullOrWhiteSpace(typeId) ? BlueprintTypeSystem.Bool : typeId.Trim();

        // FC-2/LV-4: fixed-list element must be unmanaged (blittable state bytes).
        if (capacity > 0 && finalType == BlueprintTypeSystem.String)
            return null;

        var decl = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = trimmed,
            Type = capacity > 0
                ? new BlueprintTypeRef
                {
                    TypeId        = finalType,
                    Capacity      = capacity,
                    InitialLength = Math.Clamp(initialLength, 0, capacity),
                }
                : new BlueprintTypeRef { TypeId = finalType },
        };
        asset.Variables.Add(decl);
        markDirty?.Invoke();
        return decl;
    }

    /// <summary>
    /// True when <paramref name="name"/> matches an existing variable name (case-insensitive).
    /// Exposed <c>internal</c> so the variable-create modal can validate the live input and
    /// disable Confirm before invoking <see cref="CreateVariable"/>.
    /// </summary>
    internal static bool IsDuplicateVariableName(BlueprintAsset asset, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        return asset.Variables.Any(v =>
            string.Equals(v.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string MakeUniqueVariableName(BlueprintAsset asset, string baseName)
        => MakeUniqueName(asset.Variables.Select(v => v.Name), baseName);

    /// <summary>
    /// <paramref name="baseName"/> if free, otherwise the first <c>baseName1</c>, <c>baseName2</c>, …
    /// not already in <paramref name="existingNames"/> (case-insensitive).
    /// </summary>
    private static string MakeUniqueName(IEnumerable<string> existingNames, string baseName)
    {
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName)) return baseName;
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName}{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    // ── Create-custom-event command (BP-12c) ──────────────────────────────────

    /// <summary>
    /// BP-12c — registers <c>editor.create-custom-event</c> as a <b>quick-add</b>: one click appends a
    /// parameterless <see cref="CustomEventDecl"/> with a free default name. Mirrors the
    /// <c>editor.create-variable</c> quick-add overload and exists for the same reason — so the create
    /// path is drivable headlessly, without ImGui.
    /// <para>
    /// Production wiring uses the modal overload
    /// (<see cref="RegisterCreateCustomEventCommand(EditorCommandsImpl, Action)"/>).
    /// </para>
    /// </summary>
    internal static void RegisterCreateCustomEventCommand(
        EditorCommandsImpl commands,
        BlueprintAsset     asset,
        Action?            markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(asset);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateCustomEvent,
            "Create Custom Event", "Add",
            _ => AddCustomEvent(asset, markDirty),
            description: "Declare a new custom event on this blueprint.");
    }

    /// <summary>
    /// BP-12c — registers <c>editor.create-custom-event</c> so the My Blueprint panel's
    /// "Custom Events +" opens the create modal (name + parameters) rather than appending a
    /// default-named declaration. The modal's confirm callback calls
    /// <see cref="CreateCustomEvent"/>.
    /// </summary>
    /// <param name="commands">The editor command catalog.</param>
    /// <param name="openModal">Opens the custom-event-create modal (e.g. <c>modal.Open</c>).</param>
    public static void RegisterCreateCustomEventCommand(
        EditorCommandsImpl commands,
        Action             openModal)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(openModal);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateCustomEvent,
            "Create Custom Event", "Add",
            _ => openModal(),
            description: "Declare a new custom event on this blueprint.");
    }

    /// <summary>
    /// The "Custom Events +" quick-add path (no modal): appends a parameterless
    /// <see cref="CustomEventDecl"/> with an auto-generated free name (<c>NewEvent</c>,
    /// <c>NewEvent1</c>, …), so repeated clicks never collide. Never rejects.
    /// </summary>
    internal static CustomEventDecl AddCustomEvent(BlueprintAsset asset, Action? markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var name = MakeUniqueName(asset.CustomEvents.Select(e => e.Name), "NewEvent");
        // A free, identifier-shaped name cannot be rejected by CreateCustomEvent.
        return CreateCustomEvent(asset, name, parameters: null, markDirty)!;
    }

    /// <summary>
    /// Headless-testable create path used by the custom-event modal: appends a
    /// <see cref="CustomEventDecl"/> named <paramref name="name"/> with the supplied
    /// <paramref name="parameters"/>.
    ///
    /// <para>
    /// <b>Rejects</b> (returns <see langword="null"/>, adds nothing) when the event name — or any
    /// parameter name — is blank, is not a C# identifier, or collides with a sibling
    /// (case-insensitively). The name is not cosmetic: the compiler emits
    /// <c>Event_{Name}(…)</c> verbatim (<c>InstanceEmitter.EmitEventMethod</c>) and each parameter
    /// becomes a C# parameter, so a name with a space or a leading digit is a Roslyn error rather
    /// than a validation message. The modal warns and disables Confirm up front; this method is the
    /// authoritative guard. No silent renaming.
    /// </para>
    /// </summary>
    /// <param name="asset">The asset to append the declaration to.</param>
    /// <param name="name">The event name; must be a C# identifier and unique on the asset.</param>
    /// <param name="parameters">
    /// Ordered <c>(name, typeId)</c> payload parameters; these become the <c>CallCustomEvent</c>
    /// node's data-in pins (<c>NodePinSchema.CallCustomEventPins</c>). Null/empty declares a
    /// parameterless event.
    /// </param>
    /// <param name="markDirty">Optional dirty-marking callback (invoked only on success).</param>
    /// <returns>The created declaration, or <see langword="null"/> if anything was rejected.</returns>
    internal static CustomEventDecl? CreateCustomEvent(
        BlueprintAsset asset,
        string         name,
        IReadOnlyList<(string Name, string TypeId)>? parameters = null,
        Action?        markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!IsValidDeclarationName(name)) return null;
        var trimmed = name.Trim();
        if (IsDuplicateCustomEventName(asset, trimmed)) return null;

        var paramDecls = new List<ParameterDecl>();
        if (parameters is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (paramName, typeId) in parameters)
            {
                if (!IsValidDeclarationName(paramName)) return null;
                var paramTrimmed = paramName.Trim();
                if (!seen.Add(paramTrimmed)) return null;

                paramDecls.Add(new ParameterDecl
                {
                    Id   = Guid.NewGuid(),
                    Name = paramTrimmed,
                    Type = new BlueprintTypeRef
                    {
                        TypeId = string.IsNullOrWhiteSpace(typeId)
                            ? BlueprintTypeSystem.Bool
                            : typeId.Trim(),
                    },
                });
            }
        }

        var decl = new CustomEventDecl
        {
            Id         = Guid.NewGuid(),
            Name       = trimmed,
            Parameters = paramDecls,
        };
        asset.CustomEvents.Add(decl);
        markDirty?.Invoke();
        return decl;
    }

    /// <summary>
    /// True when <paramref name="name"/> matches an existing custom-event name (case-insensitive).
    /// Exposed <c>internal</c> so the create modal can validate live input and disable Confirm.
    /// </summary>
    internal static bool IsDuplicateCustomEventName(BlueprintAsset asset, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        return asset.CustomEvents.Any(e =>
            string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when <paramref name="name"/> can be emitted verbatim into generated C# — a non-keyword
    /// identifier (letter or <c>_</c> first, then letters/digits/<c>_</c>). Exposed
    /// <c>internal</c> so the create modal can warn before Confirm rather than after Roslyn.
    /// </summary>
    internal static bool IsValidDeclarationName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();

        if (!char.IsLetter(trimmed[0]) && trimmed[0] != '_') return false;
        for (int i = 1; i < trimmed.Length; i++)
            if (!char.IsLetterOrDigit(trimmed[i]) && trimmed[i] != '_') return false;

        return !CSharpKeywords.Contains(trimmed);
    }

    /// <summary>
    /// C# reserved words. A parameter named <c>class</c> is a well-formed identifier by shape but a
    /// compile error once emitted, so shape alone is not enough.
    /// </summary>
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a peer-signature lookup delegate from <paramref name="catalog"/>.
    /// The delegate reads each peer's .bp.json from disk on demand (lazy, not cached) and
    /// returns the parsed <see cref="BlueprintSignature"/>, mirroring
    /// <c>QuickReloadService.BuildSiblingSignatures</c> for the factory context.
    /// Returns <see langword="null"/> when <paramref name="catalog"/> is null (no lookup).
    /// </summary>
    private static Func<Guid, BlueprintSignature?>? BuildPeerSignatureLookup(BlueprintPeerSource? catalog)
    {
        if (catalog == null) return null;

        return peerGuid =>
        {
            try
            {
                var entry = catalog.EnumerateAll()
                    .FirstOrDefault(e => e.AssetId == peerGuid);
                if (entry.Path == null || !File.Exists(entry.Path))
                    return null;
                var json = File.ReadAllText(entry.Path);
                return BlueprintSignatureParser.Parse(entry.Path, json);
            }
            catch
            {
                return null;
            }
        };
    }

    private static BlueprintAsset LoadAsset(BlueprintFileAsset file)
    {
        // Read and deserialize the .bp.json file.
        var json   = File.ReadAllText(file.SourceFilePath);
        var loaded = BlueprintJsonServices.Deserialize(json);
        if (loaded == null)
            throw new InvalidOperationException(
                $"Failed to deserialize Blueprint asset at '{file.SourceFilePath}'.");
        return loaded;
    }

    private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
        BlueprintAsset                              bpAsset,
        IReadOnlyList<ICustomCanvasRenderer>?       extra)
    {
        // ── Only Blueprint-specific custom renderers ──────────────────────────
        // NodeEdit's native NodeRenderer already draws breakpoint markers,
        // execution overlays, and the debug pulse — no custom renderers needed
        // for those features.

        var list = new List<ICustomCanvasRenderer>
        {
            // Blueprint custom renderer: pulsing overlay when a WhenNode fires at runtime.
            // In debug mode it is active; in release mode IsActive == false (no per-frame cost).
            new WhenFiringPulseRenderer(),
        };

        if (extra != null)
            list.AddRange(extra);

        return list;
    }
}
