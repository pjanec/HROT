using Hrot.Blueprints.Core;   // BlueprintJsonServices (in Hrot.Blueprints.Core namespace, Compiler assembly)
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.UI.Action;
using NodeEditor.UI.Find;

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
        EditService?            editService    = null,
        NodeKindRegistry?       paletteRegistry = null,
        IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null)
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

        // ── 3. Kind-specific host components ─────────────────────────────────
        var kindRegistry = paletteRegistry ?? new NodeKindRegistry();

        // ── 2. Graph model (pass registry for pin hydration of JSON-loaded assets) ──
        var graphModel = new BlueprintGraphModel(bpAsset, graph, kindRegistry);
        var nodeCatalog  = new BlueprintNodeCatalog(kindRegistry);
        var typeSystem   = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
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

        var commandSink = new BlueprintCommandSink(
            bpAsset, graph, graphModel, nodeCatalog, validator, history,
            localEditService, markDirty);

        // ── 5. Custom renderers (Blueprint set + caller extras) ───────────────
        var renderers = BuildRenderers(extraRenderers);

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
    /// Appends a new <see cref="VariableDecl"/> with a unique name to the asset's
    /// <see cref="BlueprintAsset.Variables"/> list and invokes the dirty callback.
    /// Returns the created declaration.
    /// </summary>
    internal static VariableDecl AddVariable(BlueprintAsset asset, Action? markDirty = null)
    {
        var decl = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = MakeUniqueVariableName(asset, "NewVar"),
            Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        };
        asset.Variables.Add(decl);
        markDirty?.Invoke();
        return decl;
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
        IReadOnlyList<ICustomCanvasRenderer>? extra)
    {
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
