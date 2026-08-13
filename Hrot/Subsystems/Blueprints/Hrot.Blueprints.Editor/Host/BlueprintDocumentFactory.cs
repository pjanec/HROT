using System.Numerics;
using Hrot.Blueprints.Core;   // BlueprintJsonServices (in Hrot.Blueprints.Core namespace, Compiler assembly)
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;   // IChannelCommandCatalog
using Hrot.Blueprints.Core.Compiler.Transform;   // CollapseTarget (BP-74)
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

        // ── 1. Resolve the graph to open on (BP-24 / Q23-C) ──────────────────
        // Last-viewed graph for this asset when known, else the FIRST graph in authored order.
        // The old rule *preferred an Event graph*, which silently moved the canvas off the main
        // graph whenever an asset gained one (CustomEventSubscriberDemo opened on OnPing instead
        // of Tick) — and would have made B2's auto-created event bodies steal the canvas.
        // Cross-restart persistence belongs in BlueprintEditorPreferences once something actually
        // composes/loads that file; today nothing does, so the memory is session-scoped.
        var graph = ResolveInitialGraph(bpAsset)
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
        // Register FixedString32/64/128 as string-editor types (unmanaged; authored as plain text).
        builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
        builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString64), new StringPinEditor());
        builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString128), new StringPinEditor());
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

        // BP-24: graph switching. Retargets the model + sink in place (the view and its undo
        // stack survive), tags undo entries with the graph they were recorded in, and remembers
        // per-graph viewport/selection. Everything below that needs "the current graph" must read
        // it through this object rather than capturing `graph` — a captured local goes stale on
        // the first switch.
        var switcher = new BlueprintGraphSwitcher(
            bpAsset, graphModel, commandSink, view, hostServices, debugSession);
        BlueprintGraphViewMemory.SetLastViewed(bpAsset.AssetId, graph.Id);

        // ── 8. Picker sources (BCP-E) ─────────────────────────────────────────
        // BP-57: the last argument is what lets `variables.all` offer the current graph's LOCALS.
        // ⚠ Read through `switcher`, never the captured `graph` — the same staleness trap BP-24 hit
        // at five build-time capture sites.
        BlueprintPickerSources.Register(
            bundle.PickerRegistry, nodeCatalog, bpAsset, () => switcher.CurrentGraph);

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
                    // BP-24: read the graph id through the switcher — a captured `graph.Id`
                    // would evaluate breakpointability against the wrong graph after a switch.
                    bpDebugSession.IsNodeBreakpointable(bpAsset.AssetId, switcher.CurrentGraphId, n.Value)),
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
        // BP-24: the current graph is read through the switcher (was a captured `graph`, which
        // would have pasted into the wrong graph after a switch).
        RegisterClipboardCommands(
            commands, view, () => switcher.CurrentGraph, hostServices.Clipboard, () => bpFile.MarkDirty());

        // BP-24: graph switching + navigation (Q23-D1). One handler behind
        // CommandCatalog.GoToGraph; the My Blueprint window invokes it on double-click and the
        // bookmark jump below routes through the same switcher.
        RegisterGoToGraphCommand(commands, bpAsset, switcher);

        // ── 10. Bookmarks (navigation aids) ──────────────────────────────────
        // Per-document BookmarkStore + the shared NodeEdit set/jump commands (Ctrl+1..9
        // jump, Ctrl+Shift+1..9 set — see BookmarkCommands). Pumped automatically by
        // AiGraphCanvasWindow's per-frame EditorHotkeyDispatcher since they're registered
        // on this document's `commands` (same object as FindBar/Toggle-Breakpoint/etc).
        // The store is exposed via AiCanvasContext.Bookmarks so the composition root can
        // draw the off-screen edge-marker overlay (BlueprintEditorBootstrap.DrawBookmarkEdgeMarkers)
        // and, optionally, a Bookmarks panel window.
        var bookmarkStore = new BookmarkStore();
        // BP-74 / BP-223: ONE indicators instance per document, shared by bookmarks and collapse and
        // exposed on AiCanvasContext so the composition root can actually draw what is queued. It
        // used to be a local built solely for BookmarkCommands, over a ToastQueue nothing ever
        // drained — every bookmark notification since BP-24 has been enqueued and discarded.
        var indicators = new EditorIndicatorsImpl(new ToastQueue());
        BookmarkCommands.RegisterAll(
            commands, view, bookmarkStore, indicators,
            // BP-24: a bookmark set in another of this asset's graphs first switches the canvas
            // there (Bookmark.TargetGraph stores the view-level id, hence the mapping call).
            // This was `_ => { }` for as long as the canvas couldn't switch.
            navigateToGraph: viewId => switcher.SwitchToViewId(viewId));

        // BP-74: collapse a selection into a new Macro/Function graph. Registered last because it
        // is the only command here that both reads the selection and appends a graph.
        RegisterCollapseCommands(
            commands, view, bpAsset, () => switcher.CurrentGraph, indicators, () => bpFile.MarkDirty());

        // BP-76: expand + go-to-definition. Both ids were declared with no handler, and the Expand
        // item was gated in shared UI on kind ids no blueprint node carries.
        RegisterExpandCommands(
            commands, view, bpAsset, () => switcher.CurrentGraph,
            goToGraph: id => switcher.SwitchTo(id), indicators, () => bpFile.MarkDirty());

        // Store the BlueprintAsset in AssetRef so the composition root can retarget
        // My Blueprint / Details / Variables windows without a kind-specific dependency.
        return new AiCanvasContext(view, AssetKind.Blueprint.ToString())
        {
            AssetRef   = bpAsset,
            FindBar    = findBar,
            Commands   = commands,
            Bookmarks  = bookmarkStore,
            Indicators = indicators,
            // BP-72: graph-scoped windows must follow the switched canvas. Read through the
            // switcher, never a captured `graph` — that is the same staleness trap the five
            // build-time capture sites hit in BP-24.
            CurrentGraphId = () => switcher.CurrentGraphId,
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

    // ── My Blueprint item rename / delete / duplicate (BP-12b) ────────────────

    /// <summary>
    /// BP-12b — registers <c>editor.rename-item</c>, <c>editor.delete-item</c> and
    /// <c>editor.duplicate-item</c>, which the My Blueprint context menu has always invoked and
    /// nothing ever handled. Consequence: a variable could be <b>created but never renamed or
    /// removed</b>.
    /// </summary>
    /// <param name="view">
    /// When supplied, each edit is recorded on this view's undo stack (BP-11's single stack).
    /// Null applies the edit directly — for hosts with no open canvas.
    /// </param>
    /// <param name="promptForName">
    /// Opens the rename prompt: <c>(currentName, onConfirm)</c>. Null makes rename require an
    /// explicit <c>Args["newName"]</c>, which is how tests drive it.
    /// </param>
    internal static void RegisterMyBlueprintItemCommands(
        EditorCommandsImpl        commands,
        BlueprintAsset            asset,
        GraphView?                view          = null,
        Action?                   markDirty     = null,
        Action<string, Action<string>>? promptForName = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(asset);

        var reg = new CommandRegistration(commands);

        reg.Add(
            "editor.rename-item", "Rename", "Edit",
            ctx =>
            {
                var itemId = ReadArg(ctx, "itemId") as string;
                if (string.IsNullOrEmpty(itemId)) return;

                if (ReadArg(ctx, "newName") is string supplied)
                {
                    RecordItemEdit(view, asset, markDirty, "Rename",
                        () => RenameItem(asset, itemId!, supplied));
                    return;
                }

                if (promptForName is null) return;
                var current = ItemDisplayName(asset, itemId!) ?? "";
                promptForName(current, entered => RecordItemEdit(view, asset, markDirty, "Rename",
                    () => RenameItem(asset, itemId!, entered)));
            },
            description: "Renames the selected item.");

        reg.Add(
            "editor.delete-item", "Delete", "Edit",
            ctx =>
            {
                if (ReadArg(ctx, "itemId") is not string itemId || itemId.Length == 0) return;
                RecordItemEdit(view, asset, markDirty, "Delete", () => DeleteItem(asset, itemId));
            },
            description: "Deletes the selected item from this Blueprint.");

        reg.Add(
            "editor.duplicate-item", "Duplicate", "Edit",
            ctx =>
            {
                if (ReadArg(ctx, "itemId") is not string itemId || itemId.Length == 0) return;
                RecordItemEdit(view, asset, markDirty, "Duplicate",
                    () => DuplicateItem(asset, itemId));
            },
            description: "Adds a copy of the selected item.");
    }

    /// <summary>
    /// Runs an item edit as a snapshot-and-restore undo step. These are <b>asset-level</b> edits —
    /// declarations, not graph elements — so the inverse cannot be expressed as a
    /// <see cref="GraphCommand"/>; <see cref="BlueprintEditCommand"/> carries both halves onto the
    /// same stack as everything else (BP-11).
    ///
    /// <para>
    /// The inverse restores the declaration <i>lists</i> wholesale rather than reversing the
    /// specific mutation. Rename, delete and duplicate each touch a different shape of state
    /// (a field, a list entry, both), and a snapshot is one correct inverse for all three.
    /// </para>
    /// </summary>
    private static void RecordItemEdit(
        GraphView? view, BlueprintAsset asset, Action? markDirty, string label, Func<bool> mutate)
    {
        var beforeVariables = SnapshotVariables(asset);
        var beforeEvents    = SnapshotEvents(asset);
        var beforeNames     = SnapshotEventNaming(asset);

        if (!mutate()) return;

        var afterVariables = SnapshotVariables(asset);
        var afterEvents    = SnapshotEvents(asset);
        var afterNames     = SnapshotEventNaming(asset);

        if (view is null)
        {
            markDirty?.Invoke();
            return;
        }

        void Restore(
            List<VariableDecl> variables, List<CustomEventDecl> events,
            (Dictionary<Guid, string> GraphNames,
             Dictionary<Guid, string> CallEventIds,
             Dictionary<Guid, string> PeerFunctionRefs) naming)
        {
            asset.Variables.Clear();
            asset.Variables.AddRange(variables);
            asset.CustomEvents.Clear();
            asset.CustomEvents.AddRange(events);

            // BP-24 fix to a BP-12b gap: renaming a custom event also renames its body graph and
            // rewrites name-keyed CallCustomEvent refs (RenameItem), but the decl-list snapshots
            // above never covered those two mutations — undoing a rename restored the declaration
            // and left the graph/refs renamed, silently desyncing the Event_{Name} pairing into a
            // BP1407. These two maps restore them.
            foreach (var g in asset.Graphs)
                if (naming.GraphNames.TryGetValue(g.Id, out var n)) g.Name = n;
            foreach (var call in asset.Graphs.SelectMany(g => g.Nodes).OfType<CallCustomEventNode>())
                if (naming.CallEventIds.TryGetValue(call.Id, out var id)) call.EventId = id;
            foreach (var peer in asset.Graphs.SelectMany(g => g.Nodes).OfType<CallPeerBlueprintNode>())
                if (naming.PeerFunctionRefs.TryGetValue(peer.Id, out var fn)) peer.FunctionRef = fn;
        }

        // The forward has already been applied above, so its Mutate re-applies the same end state
        // on redo rather than repeating the operation (which would, e.g., duplicate twice).
        view.Execute(
            new BlueprintEditCommand(label, () => Restore(afterVariables,  afterEvents,  afterNames)),
            new BlueprintEditCommand(label, () => Restore(beforeVariables, beforeEvents, beforeNames)),
            label);

        markDirty?.Invoke();
    }

    /// <summary>Graph names + CallCustomEvent EventIds — the two things RenameItem mutates
    /// outside the declaration lists. Strings only; cheap for any asset size.</summary>
    private static (Dictionary<Guid, string> GraphNames,
                    Dictionary<Guid, string> CallEventIds,
                    Dictionary<Guid, string> PeerFunctionRefs)
        SnapshotEventNaming(BlueprintAsset asset)
        => (asset.Graphs.ToDictionary(g => g.Id, g => g.Name),
            asset.Graphs.SelectMany(g => g.Nodes).OfType<CallCustomEventNode>()
                 .ToDictionary(c => c.Id, c => c.EventId),
            // BP-127: a graph rename also rewrites name-keyed CallPeerBlueprint.FunctionRef, so undo
            // must restore those too. ⚠ Exactly the gap BP-24 closed for CallCustomEvent, one node
            // kind over: restoring the graph name while leaving the refs renamed desyncs them into a
            // silent BP1302 "no function graph named …".
            asset.Graphs.SelectMany(g => g.Nodes).OfType<CallPeerBlueprintNode>()
                 .ToDictionary(c => c.Id, c => c.FunctionRef));

    /// <summary>
    /// A <b>deep</b> copy of the declaration list. A shallow one would be wrong for rename: the
    /// declarations are mutated in place, so both snapshots would hold the same object and undo
    /// would restore the new name.
    /// </summary>
    private static List<VariableDecl> SnapshotVariables(BlueprintAsset asset)
        => asset.Variables.Select(v => new VariableDecl
        {
            Id   = v.Id,
            Name = v.Name,
            Type = new BlueprintTypeRef
            {
                TypeId        = v.Type.TypeId,
                IsArray       = v.Type.IsArray,
                Capacity      = v.Type.Capacity,
                InitialLength = v.Type.InitialLength,
                GenericArgs   = v.Type.GenericArgs.ToList(),
            },
            DefaultValueJson = v.DefaultValueJson,
            IsEditable       = v.IsEditable,
            IsExposedOnSpawn = v.IsExposedOnSpawn,
            Category         = v.Category,
            Tooltip          = v.Tooltip,
            Comment          = v.Comment,
        }).ToList();

    private static List<CustomEventDecl> SnapshotEvents(BlueprintAsset asset)
        => asset.CustomEvents.Select(e => new CustomEventDecl
        {
            Id         = e.Id,
            Name       = e.Name,
            Parameters = e.Parameters.Select(p => new ParameterDecl
            {
                Id               = p.Id,
                Name             = p.Name,
                Type             = new BlueprintTypeRef { TypeId = p.Type.TypeId },
                DefaultValueJson = p.DefaultValueJson,
                Tooltip          = p.Tooltip,
                Comment          = p.Comment,
            }).ToList(),
        }).ToList();

    /// <summary>The declared name behind a My Blueprint item id, or null when it resolves to nothing.</summary>
    internal static string? ItemDisplayName(BlueprintAsset asset, string itemId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return FindVariable(asset, itemId)?.Name
               ?? FindCustomEvent(asset, itemId)?.Name
               ?? FindGraph(asset, itemId)?.Name;
    }

    /// <summary>
    /// Renames the variable or custom event named by <paramref name="itemId"/>. Returns false —
    /// changing nothing — for a blank, invalid or already-taken name, and for an id that resolves
    /// to nothing.
    ///
    /// <para>
    /// Renaming a custom event also renames its paired <c>Event</c> handler graph and rewrites any
    /// <b>name-keyed</b> <c>CallCustomEvent</c> reference. The editor writes GUIDs, which survive a
    /// rename untouched, but Stage5 accepts a bare name and hand-authored assets use one — leaving
    /// those behind would turn a rename into a silent BP1403.
    /// </para>
    /// </summary>
    internal static bool RenameItem(BlueprintAsset asset, string itemId, string newName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(newName)) return false;

        var trimmed = newName.Trim();

        if (FindVariable(asset, itemId) is { } variable)
        {
            if (string.Equals(variable.Name, trimmed, StringComparison.Ordinal)) return false;
            if (IsDuplicateVariableName(asset, trimmed)) return false;
            variable.Name = trimmed;
            return true;
        }

        if (FindCustomEvent(asset, itemId) is { } evt)
        {
            if (string.Equals(evt.Name, trimmed, StringComparison.Ordinal)) return false;
            // Same identifier rule as the create path: the compiler emits Event_{Name} verbatim.
            if (!IsValidDeclarationName(trimmed)) return false;
            if (IsDuplicateCustomEventName(asset, trimmed)) return false;

            var oldName = evt.Name;
            evt.Name = trimmed;

            foreach (var graph in asset.Graphs)
                if (graph.Kind == GraphKind.Event
                    && string.Equals(graph.Name, oldName, StringComparison.Ordinal))
                    graph.Name = trimmed;

            foreach (var call in asset.Graphs.SelectMany(g => g.Nodes).OfType<CallCustomEventNode>())
                if (string.Equals(call.EventId, oldName, StringComparison.Ordinal))
                    call.EventId = trimmed;

            return true;
        }

        // BP-127 -- renaming a graph. Settled by the authoring-UX decisions round: this lives in My
        // Blueprint's context menu, where Unreal puts it, NOT on an empty-canvas Details surface.
        if (FindGraph(asset, itemId) is { } target)
        {
            if (string.Equals(target.Name, trimmed, StringComparison.Ordinal)) return false;

            // The compiler emits a method per Function graph and Event_{Name} per Event graph, so the
            // name has to be a legal identifier -- the same rule the custom-event create path applies.
            if (!IsValidDeclarationName(trimmed)) return false;
            if (asset.Graphs.Any(g => g != target
                    && string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                return false;

            // ⚠ An Event graph is PAIRED with its declaration by name (see the custom-event branch
            // above, which renames the graph when the decl is renamed). Renaming that graph directly
            // would break the pairing into a BP1407, so it is refused -- rename the event instead.
            if (target.Kind == GraphKind.Event
                && asset.CustomEvents.Any(e =>
                    string.Equals(e.Name, target.Name, StringComparison.Ordinal)))
                return false;

            var previousName = target.Name;
            target.Name = trimmed;

            // ⚠ A peer/local FunctionCall addresses a Function graph BY NAME as well as by id, and
            // Stage5 accepts either -- so a rename that left those behind would turn into a silent
            // BP1302 "no function graph named …". Same class of miss BP-24 fixed for custom events.
            foreach (var call in asset.Graphs.SelectMany(g => g.Nodes))
            {
                if (call is CallPeerBlueprintNode peer
                    && string.Equals(peer.FunctionRef, previousName, StringComparison.Ordinal))
                    peer.FunctionRef = trimmed;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the declaration named by <paramref name="itemId"/>.
    ///
    /// <para>
    /// Nodes that referenced it are <b>left in place</b>. They render as dangling references and the
    /// compiler names them (BP1403 / BP1500), which is recoverable; silently deleting a designer's
    /// wired-up nodes because a declaration went away is not.
    /// </para>
    /// </summary>
    internal static bool DeleteItem(BlueprintAsset asset, string itemId)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (FindVariable(asset, itemId) is { } variable)
            return asset.Variables.Remove(variable);

        if (FindCustomEvent(asset, itemId) is { } evt)
            return asset.CustomEvents.Remove(evt);

        return false;
    }

    /// <summary>
    /// Adds a copy of the declaration named by <paramref name="itemId"/> under a free name
    /// (<c>Health</c> → <c>Health1</c>). Parameters, type, category and tooltip come along; the id
    /// does not.
    /// </summary>
    internal static bool DuplicateItem(BlueprintAsset asset, string itemId)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (FindVariable(asset, itemId) is { } variable)
        {
            asset.Variables.Add(new VariableDecl
            {
                Id       = Guid.NewGuid(),
                Name     = MakeUniqueName(asset.Variables.Select(v => v.Name), variable.Name),
                Type     = new BlueprintTypeRef
                {
                    TypeId        = variable.Type.TypeId,
                    IsArray       = variable.Type.IsArray,
                    Capacity      = variable.Type.Capacity,
                    InitialLength = variable.Type.InitialLength,
                },
                DefaultValueJson = variable.DefaultValueJson,
                IsEditable       = variable.IsEditable,
                IsExposedOnSpawn = variable.IsExposedOnSpawn,
                Category         = variable.Category,
                Tooltip          = variable.Tooltip,
            });
            return true;
        }

        if (FindCustomEvent(asset, itemId) is { } evt)
        {
            asset.CustomEvents.Add(new CustomEventDecl
            {
                Id         = Guid.NewGuid(),
                Name       = MakeUniqueName(asset.CustomEvents.Select(e => e.Name), evt.Name),
                Parameters = evt.Parameters.Select(p => new ParameterDecl
                {
                    Id               = Guid.NewGuid(),
                    Name             = p.Name,
                    Type             = new BlueprintTypeRef { TypeId = p.Type.TypeId },
                    DefaultValueJson = p.DefaultValueJson,
                    Tooltip          = p.Tooltip,
                }).ToList(),
            });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a <c>var:{guid}</c> item id. The My Blueprint panel prefixes its item ids by
    /// section; the prefix is what tells a variable id from an event id.
    /// </summary>
    private static VariableDecl? FindVariable(BlueprintAsset asset, string itemId)
        => TryItemGuid(itemId, "var:", out var id)
            ? asset.Variables.FirstOrDefault(v => v.Id == id)
            : null;

    private static CustomEventDecl? FindCustomEvent(BlueprintAsset asset, string itemId)
        => TryItemGuid(itemId, "evt:", out var id)
            ? asset.CustomEvents.FirstOrDefault(e => e.Id == id)
            : null;

    /// <summary>
    /// BP-127 — resolves a <c>graph:{guid}</c> item id, the form the My Blueprint panel gives its
    /// Graphs / Functions rows (<c>BlueprintMyBlueprintModel</c>).
    /// </summary>
    private static Graph? FindGraph(BlueprintAsset asset, string itemId)
        => TryItemGuid(itemId, "graph:", out var id)
            ? asset.Graphs.FirstOrDefault(g => g.Id == id)
            : null;

    private static bool TryItemGuid(string itemId, string prefix, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrEmpty(itemId)) return false;
        if (!itemId.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return Guid.TryParse(itemId.Substring(prefix.Length), out id);
    }

    // ── Graph switching (BP-24 / Q23-D1) ──────────────────────────────────────

    /// <summary>
    /// BP-24 — registers <c>editor.go-to-graph</c>, the id that has been declared in
    /// <c>CommandCatalog</c> with zero handlers since the catalog was written. Accepts either
    /// <c>Args["itemId"]</c> in the My Blueprint panel's forms (<c>graph:{guid}</c> from the
    /// Graphs/Functions sections, <c>evt:{guid}</c> from Custom Events — resolved to the event's
    /// body graph by name), or <c>Args["graphId"]</c> as a GUID / GUID string.
    ///
    /// <para>Exposed <c>internal</c> so tests can drive switching without ImGui.</para>
    /// </summary>
    internal static void RegisterGoToGraphCommand(
        EditorCommandsImpl    commands,
        BlueprintAsset        asset,
        BlueprintGraphSwitcher switcher)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(switcher);

        var reg = new CommandRegistration(commands);

        reg.Add(
            NodeEditor.Core.CommandCatalog.GoToGraph, "Go to Graph", "Navigate",
            ctx =>
            {
                if (TryResolveTargetGraph(asset, ctx, out var graphId))
                    switcher.SwitchTo(graphId);
            },
            description: "Shows the given graph on the canvas (double-click a graph in My Blueprint).");
    }

    /// <summary>Resolves the command context to an asset graph id; see the register doc.</summary>
    private static bool TryResolveTargetGraph(
        BlueprintAsset asset, EditorCommandContext ctx, out Guid graphId)
    {
        graphId = Guid.Empty;

        if (ReadArg(ctx, "graphId") is { } rawId)
        {
            if (rawId is Guid g) { graphId = g; return true; }
            if (rawId is string s && Guid.TryParse(s, out var parsed)) { graphId = parsed; return true; }
        }

        if (ReadArg(ctx, "itemId") is string itemId)
        {
            if (TryItemGuid(itemId, "graph:", out var direct)) { graphId = direct; return true; }

            // A custom event navigates to its body graph — the Event graph carrying the decl's
            // name (the same pairing rule the compiler's Event_{Name} emission uses).
            if (FindCustomEvent(asset, itemId) is { } evt)
            {
                var body = asset.Graphs.FirstOrDefault(
                    gr => gr.Kind == GraphKind.Event
                       && string.Equals(gr.Name, evt.Name, StringComparison.Ordinal));
                if (body is not null) { graphId = body.Id; return true; }
            }
        }

        return false;
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
    /// <param name="currentGraph">
    /// BP-24 — resolved per invocation, not captured: after a canvas graph switch a captured
    /// graph would copy from / paste into a graph the designer is no longer looking at. (Inside
    /// <see cref="PasteFrom"/> the closures still bind the graph resolved at paste time — that is
    /// correct, the undo stack's graph context switches back there before replaying.)
    /// </param>
    internal static void RegisterClipboardCommands(
        EditorCommandsImpl commands,
        GraphView          view,
        Func<Graph>        currentGraph,
        IClipboard?        clipboard,
        Action?            markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(currentGraph);

        var reg = new CommandRegistration(commands);

        reg.Add(
            NodeEditor.Core.CommandCatalog.Copy, "Copy", "Edit",
            _ => CopySelection(view, currentGraph(), clipboard),
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
                if (CopySelection(view, currentGraph(), clipboard))
                    commands.Invoke(NodeEditor.Core.CommandCatalog.DeleteSelection);
            },
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Copies the selected nodes and deletes them.",
            defaultKey: new KeyBinding(EditorKey.X, KeyModifiers.Ctrl));

        reg.Add(
            NodeEditor.Core.CommandCatalog.Paste, "Paste", "Edit",
            ctx => PasteFrom(view, currentGraph(), clipboard?.GetText(), ctx.CanvasPos, markDirty),
            isEnabled: () => BlueprintClipboard.TryParse(clipboard?.GetText(), out _),
            description: "Pastes previously copied nodes.",
            defaultKey: new KeyBinding(EditorKey.V, KeyModifiers.Ctrl));

        reg.Add(
            NodeEditor.Core.CommandCatalog.Duplicate, "Duplicate", "Edit",
            _ =>
            {
                var g = currentGraph();
                PasteFrom(view, g, BlueprintClipboard.Copy(g, SelectedNodeIds(view)),
                          targetPosition: null, markDirty);
            },
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Duplicates the selected nodes in place.",
            defaultKey: new KeyBinding(EditorKey.D, KeyModifiers.Ctrl));
    }

    private static IReadOnlyCollection<Guid> SelectedNodeIds(GraphView view)
        => view.Selection.Nodes.Select(n => n.Value).ToList();

    // ── Expand Node + Go to Definition (BP-76) ────────────────────────────────

    /// <summary>
    /// BP-76 — registers <c>editor.expand-node</c> and <c>editor.go-to-definition</c>. Both ids were
    /// declared in <c>CommandCatalog</c> with no handler; the Expand item was additionally gated in
    /// shared UI on kind ids no blueprint node carries, which is what kept a <b>corrupting</b> path
    /// unreachable rather than merely inert (see <see cref="BlueprintExpand"/>).
    ///
    /// <para>
    /// ⭐⭐ <c>isEnabled</c> is <b>"is exactly one node selected"</b>, and nothing else — Q26-B2, the
    /// rule Batch 34 proved out on collapse. Whether that node is an expandable macro call is decided
    /// on invoke and reported by name. Putting it in the predicate would drag blueprint vocabulary
    /// back into <c>NodeEditor.UI</c>, which is the whole defect being removed here.
    /// </para>
    /// </summary>
    /// <param name="goToGraph">
    /// ⭐ Go to Definition delegates to the already-registered <c>editor.go-to-graph</c> rather than
    /// re-resolving anything: that handler owns every id form the panel and the canvas use, and a
    /// second navigation path would be a second thing to keep in step.
    /// </param>
    internal static void RegisterExpandCommands(
        EditorCommandsImpl  commands,
        GraphView           view,
        BlueprintAsset      asset,
        Func<Graph>         currentGraph,
        Action<Guid>        goToGraph,
        IEditorIndicators?  indicators = null,
        Action?             markDirty  = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(currentGraph);
        ArgumentNullException.ThrowIfNull(goToGraph);

        var reg = new CommandRegistration(commands);

        reg.Add(
            NodeEditor.Core.CommandCatalog.ExpandNode, "Expand Node", "Refactor",
            _ =>
            {
                var graph = currentGraph();
                var id    = SelectedNodeIds(view).FirstOrDefault();
                var node  = graph.Nodes.FirstOrDefault(n => n.Id == id);
                if (node is null) return;
                BlueprintExpand.Run(view, asset, graph, node, markDirty, indicators);
            },
            isEnabled: () => view.Selection.Nodes.Count() == 1,
            description: "Replaces a macro call with a copy of the macro's body.");

        reg.Add(
            NodeEditor.Core.CommandCatalog.GoToDefinition, "Go to Definition", "Navigate",
            _ =>
            {
                var graph = currentGraph();
                var id    = SelectedNodeIds(view).FirstOrDefault();
                var node  = graph.Nodes.FirstOrDefault(n => n.Id == id);

                var targetId = TargetGraphIdOf(node);
                if (targetId is null)
                {
                    indicators?.Notify(new EditorNotification(
                        Id:          "goto-definition.no-target",
                        Severity:    NotificationSeverity.Warning,
                        Title:       "No definition to go to",
                        Body:        DescribeNoTarget(node),
                        AutoDismiss: TimeSpan.FromSeconds(6),
                        Actions:     null));
                    return;
                }

                goToGraph(targetId.Value);
            },
            isEnabled: () => view.Selection.Nodes.Count() == 1,
            description: "Opens the graph this call node targets.");
    }

    /// <summary>
    /// BP-76 — the in-blueprint graph a call node points at, or null when it points outside the asset
    /// or nowhere.
    ///
    /// <para>
    /// ⚠ <b><c>CallCustomEventNode</c> deliberately returns null here.</b> It resolves by <i>name</i>
    /// to an Event graph, not by <c>TargetGraphId</c> — and My Blueprint already navigates to a custom
    /// event's body by double-clicking it, through <c>editor.go-to-graph</c>'s <c>evt:</c> arm. Making
    /// this command re-derive that pairing would be a second copy of a rule the compiler also holds
    /// (<c>Event_{Name}</c>), so it refuses and says where to go instead.
    /// </para>
    /// </summary>
    private static Guid? TargetGraphIdOf(Node? node)
    {
        var raw = node switch
        {
            MacroCallNode m    => m.TargetGraphId,
            FunctionCallNode f => f.TargetGraphId,
            _                  => null,
        };
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private static string DescribeNoTarget(Node? node) => node switch
    {
        null                  => "Select a call node first.",
        CallCustomEventNode   => "A custom event is opened from the My Blueprint panel — double-click "
                                 + "it under Custom Events. It resolves by name, not by a graph id.",
        FunctionCallNode      => "This node calls a library method, not a graph in this blueprint.",
        _                     => "This node has no definition inside this blueprint.",
    };

    // ── Collapse selection: to Macro / to Function (BP-74) ────────────────────

    /// <summary>
    /// BP-74 — registers <c>editor.collapse-to-macro</c> and <c>editor.collapse-to-function</c>.
    /// Both ids were already declared in <c>CommandCatalog</c> with no handler anywhere in the repo,
    /// the same starting state <c>BP-23a</c>'s clipboard commands were in.
    ///
    /// <para>
    /// ⭐⭐ <b><c>isEnabled</c> tests the selection and NOTHING else</b> — not latency, not the
    /// exec-entry count, not the graph kind. Q26-B2: the item is offered whenever there is a
    /// selection and <b>refuses on invoke</b>, naming the offending nodes. A greyed item does not say
    /// why, and this repo already files greyed-with-no-explanation as a defect (<c>BP-76</c>,
    /// <c>BP-77</c>). ⚠ Do not "help" by adding legality to this predicate: doing so would also drag
    /// blueprint rules into the shared <c>NodeEditor.UI</c> menu, which is <c>BP-76</c>'s actual
    /// mistake.
    /// </para>
    ///
    /// <para>
    /// Host-side rather than through <c>GraphCommand</c>, for <c>BP-60</c>'s reason: collapse
    /// <b>creates a graph</b>, and <c>GraphCommand</c>'s vocabulary is node/link-only. The gesture
    /// travels as a <see cref="BlueprintEditCommand"/> pair so it is one undo entry.
    /// </para>
    ///
    /// <para>Exposed <c>internal</c> so tests can drive both without ImGui.</para>
    /// </summary>
    /// <param name="currentGraph">
    /// BP-24 — resolved per invocation, not captured: after a canvas graph switch a captured graph
    /// would collapse a selection out of a graph the designer is no longer looking at.
    /// </param>
    /// <param name="indicators">Refusal surface (toast). Null in headless tests that read the result.</param>
    internal static void RegisterCollapseCommands(
        EditorCommandsImpl  commands,
        GraphView           view,
        BlueprintAsset      asset,
        Func<Graph>         currentGraph,
        IEditorIndicators?  indicators = null,
        Action?             markDirty  = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(currentGraph);

        var reg = new CommandRegistration(commands);

        reg.Add(
            NodeEditor.Core.CommandCatalog.CollapseToMacro, "Collapse to Macro", "Refactor",
            _ => BlueprintCollapse.Run(
                    view, asset, currentGraph(), SelectedNodeIds(view),
                    CollapseTarget.Macro, markDirty, indicators),
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Moves the selected nodes into a new macro and calls it.");

        reg.Add(
            NodeEditor.Core.CommandCatalog.CollapseToFunction, "Collapse to Function", "Refactor",
            _ => BlueprintCollapse.Run(
                    view, asset, currentGraph(), SelectedNodeIds(view),
                    CollapseTarget.Function, markDirty, indicators),
            isEnabled: () => view.Selection.Nodes.Any(),
            description: "Moves the selected nodes into a new function and calls it.");
    }

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

        // BP-24: the free name must clear graph names too — the create now also adds the body
        // Event graph, and rejects a name a non-Event graph already holds.
        var name = MakeUniqueName(
            asset.CustomEvents.Select(e => e.Name).Concat(asset.Graphs.Select(g => g.Name)),
            "NewEvent");
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
    /// <param name="view">
    /// BP-24 — when supplied, the whole create (declaration <b>and</b> body graph) is one undoable
    /// entry on the document stack. Null applies directly (headless hosts, quick-add).
    /// </param>
    /// <returns>The created declaration, or <see langword="null"/> if anything was rejected.</returns>
    internal static CustomEventDecl? CreateCustomEvent(
        BlueprintAsset asset,
        string         name,
        IReadOnlyList<(string Name, string TypeId)>? parameters = null,
        Action?        markDirty = null,
        GraphView?     view      = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!IsValidDeclarationName(name)) return null;
        var trimmed = name.Trim();
        if (IsDuplicateCustomEventName(asset, trimmed)) return null;

        // BP-24 (Q23-B2): the event's body will be an Event graph of the same name. A non-Event
        // graph already holding the name would leave the pairing ambiguous (and two graphs with
        // one name once the body is added) — reject up front, same as a duplicate event name.
        var existingBody = asset.Graphs.FirstOrDefault(g =>
            string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existingBody is not null && existingBody.Kind != GraphKind.Event) return null;

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

        // BP-24 (Q23-B2): declaring the event also creates its body — the Kind:Event graph whose
        // Name matches, which is what the compiler emits Event_{Name} from. This closes the half
        // BP-12c had to leave open (the canvas could not switch, so a created body was
        // unreachable) and makes BP1407 reachable only from hand-authored JSON. An Event graph
        // already carrying the name (hand-authored body-first) is adopted instead of duplicated.
        var body = existingBody ?? new Graph
        {
            Id     = Guid.NewGuid(),
            Name   = trimmed,
            Kind   = GraphKind.Event,
            // The Event_{Name} parameter list is emitted from graph Inputs
            // (InstanceEmitter.EmitEventMethod), so the body mirrors the declaration. Fresh
            // ids: these are separate declarations, paired by name; arity drift is BP1408's job.
            Inputs = paramDecls.Select(p => new ParameterDecl
            {
                Id   = Guid.NewGuid(),
                Name = p.Name,
                Type = new BlueprintTypeRef { TypeId = p.Type.TypeId },
            }).ToList(),
            Nodes =
            {
                new EventEntryNode
                {
                    Id             = Guid.NewGuid(),
                    EventTypeId    = "",
                    EditorMetadata = new NodeMetadata { X = 120f, Y = 120f },
                },
            },
        };
        bool addBody = existingBody is null;

        void Apply()
        {
            asset.CustomEvents.Add(decl);
            if (addBody) asset.Graphs.Add(body);
        }
        void Undo()
        {
            if (addBody) asset.Graphs.Remove(body);
            asset.CustomEvents.Remove(decl);
        }

        var label = $"Create Custom Event '{trimmed}'";
        if (view is not null)
            view.Execute(new BlueprintEditCommand(label, Apply),
                         new BlueprintEditCommand(label, Undo), label);
        else
            Apply();

        markDirty?.Invoke();
        return decl;
    }

    /// <summary>
    /// BP-24 — the Event graph that is <paramref name="decl"/>'s body: same Name, Kind Event
    /// (the compiler's pairing rule). Null when the body does not exist (hand-authored JSON that
    /// predates auto-creation — the BP1407 case).
    /// </summary>
    internal static Graph? FindCustomEventBodyGraph(BlueprintAsset asset, CustomEventDecl decl)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(decl);
        return asset.Graphs.FirstOrDefault(g =>
            g.Kind == GraphKind.Event
            && string.Equals(g.Name, decl.Name, StringComparison.Ordinal));
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

    // ── Graph switching support (BP-24 / Q23-C) ───────────────────────────────

    /// <summary>
    /// BP-24 — the graph an asset opens on: the last-viewed one when this session remembers it,
    /// else the <b>first in authored order</b>. Null only for a graphless asset.
    /// The pre-BP-24 rule preferred an Event graph, silently moving the canvas whenever an asset
    /// gained one. Exposed <c>internal</c> for tests; <c>Build</c> is the caller.
    /// </summary>
    internal static Graph? ResolveInitialGraph(BlueprintAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var lastViewed = BlueprintGraphViewMemory.GetLastViewed(asset.AssetId);
        return asset.Graphs.FirstOrDefault(g => g.Id == lastViewed)
            ?? asset.Graphs.FirstOrDefault();
    }

    // ── Graph create (BP-24 / Q23-B2) ─────────────────────────────────────────

    /// <summary>
    /// BP-24 — registers <c>editor.create-function</c> as a <b>quick-add</b>: one click appends an
    /// empty Function graph with a free default name (<c>NewFunction</c>, <c>NewFunction1</c>, …).
    /// Mirrors the variable/custom-event quick-add overloads so the path is drivable headlessly.
    /// <para>Production wiring uses the modal overload
    /// (<see cref="RegisterCreateFunctionCommand(EditorCommandsImpl, Action)"/>).</para>
    /// </summary>
    internal static void RegisterCreateFunctionCommand(
        EditorCommandsImpl commands,
        BlueprintAsset     asset,
        Action?            markDirty = null,
        GraphView?         view      = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(asset);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateFunction,
            "Create Function", "Add",
            _ =>
            {
                var name = MakeUniqueName(asset.Graphs.Select(g => g.Name), "NewFunction");
                CreateFunctionGraph(asset, name, markDirty, view);
            },
            description: "Adds a new Function graph to this blueprint.");
    }

    /// <summary>
    /// BP-24 — registers <c>editor.create-function</c> so the My Blueprint panel's "Functions +"
    /// (and the header's "+ Function") opens the name modal. The modal's confirm callback calls
    /// <see cref="CreateFunctionGraph"/>.
    /// </summary>
    public static void RegisterCreateFunctionCommand(
        EditorCommandsImpl commands,
        Action             openModal)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(openModal);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateFunction,
            "Create Function", "Add",
            _ => openModal(),
            description: "Adds a new Function graph to this blueprint.");
    }

    /// <summary>
    /// BP-77 — registers <c>editor.create-macro</c> as a <b>quick-add</b>: one click appends a macro
    /// with a free name. Mirrors <see cref="RegisterCreateFunctionCommand(EditorCommandsImpl, BlueprintAsset, Action, GraphView)"/>
    /// and exists for the same reason — so the create path is drivable headlessly, without ImGui.
    ///
    /// <para>Production wiring uses the modal overload below.</para>
    /// </summary>
    internal static void RegisterCreateMacroCommand(
        EditorCommandsImpl commands,
        BlueprintAsset     asset,
        Action?            markDirty = null,
        GraphView?         view      = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(asset);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateMacro,
            "Create Macro", "Add",
            _ =>
            {
                var name = MakeUniqueName(asset.Graphs.Select(g => g.Name), "NewMacro");
                CreateMacroGraph(asset, name, markDirty, view);
            },
            description: "Adds a new Macro graph to this blueprint.");
    }

    /// <summary>
    /// BP-77 — registers <c>editor.create-macro</c> so the My Blueprint panel's <i>"Macros +"</i>
    /// opens the name modal. ⭐ The id and the button have both existed since BP-12e; <b>only the
    /// handler was missing</b>, so the item rendered permanently greyed — the same
    /// declared-with-no-handler shape as BP-23a's clipboard commands and BP-74's collapse.
    /// </summary>
    public static void RegisterCreateMacroCommand(
        EditorCommandsImpl commands,
        Action             openModal)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(openModal);

        var reg = new CommandRegistration(commands);
        reg.Add(
            NodeEditor.Core.CommandCatalog.CreateMacro,
            "Create Macro", "Add",
            _ => openModal(),
            description: "Adds a new Macro graph to this blueprint.");
    }

    /// <summary>
    /// BP-24 — appends a new, empty <b>Function</b> graph to the asset. Until this, nothing in the
    /// editor ever appended to <see cref="BlueprintAsset.Graphs"/>; Function graphs could only be
    /// hand-written in JSON.
    ///
    /// <para>
    /// The graph is born with one <see cref="EventEntryNode"/> whose <c>EventTypeId</c> is empty —
    /// the explicit entry indicator the compiler's <c>Stage2_Validate.FindEntryNode</c> looks for,
    /// and exactly how the shipped Function graphs (e.g. <c>CustomEventSubscriberDemo</c>'s
    /// <c>Tick</c>) are shaped. Signature (inputs/outputs) is edited afterwards in the Graph
    /// Signature window, which already does full CRUD.
    /// </para>
    ///
    /// <para>
    /// BP-126 — Unreal's "New Function" hands the author an already-wired entry + return; this one
    /// used to hand back just the entry, so every new function needed a trip to the palette to find
    /// Return, place it, and wire it — miss the wire and the compiler reports BP3010 (orphan) +
    /// BP1657. The graph is now born with a <see cref="ReturnNode"/> too, exec-linked from the
    /// entry's <c>Out</c> pin to the return's <c>In</c> pin, positioned apart on the canvas so the
    /// wire is visible rather than a same-point overlap. Both nodes' pins are not yet materialised
    /// (projection-only asset), so the link addresses them by <see cref="DeterministicIds.PinId"/> —
    /// the same deterministic scheme Stage0_Rehydrate/<c>BlueprintGraphModel.Rebuild</c> use to
    /// reconstruct pin GUIDs on load, so the link resolves correctly the moment pins materialise.
    /// </para>
    ///
    /// <para>
    /// <b>Rejects</b> (returns <see langword="null"/>) a name that is not a C# identifier or that
    /// collides with any existing graph name (case-insensitive). When <paramref name="view"/> is
    /// supplied the append is one undoable entry on the document stack.
    /// </para>
    /// </summary>
    internal static Graph? CreateFunctionGraph(
        BlueprintAsset asset,
        string         name,
        Action?        markDirty = null,
        GraphView?     view      = null)
        => CreateGraph(asset, name, GraphKind.Function, "Create Function Graph", markDirty, view);

    /// <summary>
    /// BP-77 — appends a new, empty <b>Macro</b> graph. Same shape as
    /// <see cref="CreateFunctionGraph"/>, and deliberately the same code: a macro graph's boundary
    /// is an <see cref="EventEntryNode"/> and a <see cref="ReturnNode"/> exactly as a function's is,
    /// and BP-126's reason for wiring them together (a bare entry costs a palette trip and then
    /// reports BP3010 + BP1657) applies unchanged.
    ///
    /// <para>
    /// ⚠ The new macro declares <b>no</b> <c>ExecInputs</c>/<c>ExecOutputs</c>. That is the
    /// wireable degenerate case, not an omission: <c>NodePinSchema</c> projects the single default
    /// <c>Out</c>/<c>In</c> pin when either list is empty (Q26-A3's N=0), so the macro can be wired
    /// and called immediately, and the designer declares extra entries/exits in the Graph Signature
    /// window when they want them.
    /// </para>
    /// </summary>
    internal static Graph? CreateMacroGraph(
        BlueprintAsset asset,
        string         name,
        Action?        markDirty = null,
        GraphView?     view      = null)
        => CreateGraph(asset, name, GraphKind.Macro, "Create Macro Graph", markDirty, view);

    private static Graph? CreateGraph(
        BlueprintAsset asset,
        string         name,
        GraphKind      kind,
        string         undoLabel,
        Action?        markDirty,
        GraphView?     view)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!IsValidDeclarationName(name)) return null;
        var trimmed = name.Trim();
        if (IsDuplicateGraphName(asset, trimmed)) return null;

        var entryId  = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        var graph = new Graph
        {
            Id   = Guid.NewGuid(),
            Name = trimmed,
            Kind = kind,
            Nodes =
            {
                new EventEntryNode
                {
                    Id             = entryId,
                    EventTypeId    = "",
                    EditorMetadata = new NodeMetadata { X = 120f, Y = 120f },
                },
                new ReturnNode
                {
                    Id             = returnId,
                    EditorMetadata = new NodeMetadata { X = 420f, Y = 120f },
                },
            },
            Links =
            {
                new Link
                {
                    FromNodeId = entryId,
                    FromPinId  = DeterministicIds.PinId(entryId, "Out", "Out"),
                    ToNodeId   = returnId,
                    ToPinId    = DeterministicIds.PinId(returnId, "In", "In"),
                },
            },
        };

        AppendGraph(asset, graph, undoLabel, markDirty, view);
        return graph;
    }

    /// <summary>
    /// True when <paramref name="name"/> matches an existing graph name (case-insensitive).
    /// Exposed <c>internal</c> so the create modal can validate live input.
    /// </summary>
    internal static bool IsDuplicateGraphName(BlueprintAsset asset, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        return asset.Graphs.Any(g =>
            string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Appends <paramref name="graph"/> to the asset — through the view's undo stack when one is
    /// supplied (one entry; the inverse removes the graph again), directly otherwise.
    /// The undo entry's graph context is wherever the canvas was at create time, so replaying the
    /// removal never runs while the canvas points at the graph being removed.
    /// </summary>
    private static void AppendGraph(
        BlueprintAsset asset, Graph graph, string label, Action? markDirty, GraphView? view)
    {
        void Apply() => asset.Graphs.Add(graph);
        void Undo()  => asset.Graphs.Remove(graph);

        if (view is not null)
            view.Execute(new BlueprintEditCommand(label, Apply),
                         new BlueprintEditCommand(label, Undo), label);
        else
            Apply();

        markDirty?.Invoke();
    }

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
