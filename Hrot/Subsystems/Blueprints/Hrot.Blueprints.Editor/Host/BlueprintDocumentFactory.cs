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
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Action;
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
        var ctx = new EditServiceContext(history, markDirty);
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

        // Store the BlueprintAsset in AssetRef so the composition root can retarget
        // My Blueprint / Details / Variables windows without a kind-specific dependency.
        return new AiCanvasContext(view, AssetKind.Blueprint.ToString())
        {
            AssetRef = bpAsset,
            FindBar  = findBar,
            Commands = commands,
        };
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
    /// <returns>The created declaration, or <see langword="null"/> if the name was rejected.</returns>
    internal static VariableDecl? CreateVariable(
        BlueprintAsset asset,
        string         name,
        string         typeId,
        Action?        markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // Reject blank/whitespace and duplicate names rather than silently renaming.
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        if (IsDuplicateVariableName(asset, trimmed))
            return null;

        var finalType = string.IsNullOrWhiteSpace(typeId) ? BlueprintTypeSystem.Bool : typeId.Trim();

        var decl = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = trimmed,
            Type = new BlueprintTypeRef { TypeId = finalType },
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
    {
        var existing = new HashSet<string>(
            asset.Variables.Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName)) return baseName;
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName}{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
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
