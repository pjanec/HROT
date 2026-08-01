using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.UI.Bookmarks;
using System.Reflection;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Centralizes all production-code registration for the Blueprint editor:
/// node drawers, palette entries, and visual attachment providers/renderers.
/// </summary>
public static class BlueprintEditorBootstrap
{
    /// <summary>
    /// Creates and populates a BlueprintNodeDrawerRegistry with all built-in node drawers.
    /// Called by the editor subsystem at startup.
    /// </summary>
    public static BlueprintNodeDrawerRegistry CreateNodeDrawerRegistry(
        IChannelCommandCatalog channelCatalog,
        IEngineEventCatalog eventCatalog,
        IEditService editService,
        IPredicateCompiler predicateCompiler,
        EqsTemplateRegistry eqsTemplates,
        IAnimationTkbQueries? animationQueries = null,
        Func<string?>? currentClassProvider = null,
        ISharedStructTypeProvider? sharedStructTypeProvider = null,
        IComponentTypeProvider? componentTypeProvider = null,
        IComponentTypeProvider? writableComponentTypeProvider = null)
    {
        sharedStructTypeProvider     ??= new ReflectionSharedStructTypeProvider();
        componentTypeProvider        ??= new ReflectionComponentTypeProvider();
        writableComponentTypeProvider ??= new ReflectionWritableComponentTypeProvider();

        var registry = new BlueprintNodeDrawerRegistry();

        // WHEN-M11-T1: Register the three When-Node drawers
        registry.Register(typeof(WhenNode), new WhenNodeDrawer(
            channelCatalog, eventCatalog, editService, predicateCompiler));
        registry.Register(typeof(ReadEqsResultNode), new ReadEqsResultNodeDrawer());
        registry.Register(typeof(SpawnEqsSensorNode), new SpawnEqsSensorNodeDrawer(eqsTemplates));

        // BATCH-03D1: Register FunctionCallNode drawer
        registry.Register(typeof(FunctionCallNode), new FunctionCallNodeDrawer(editService));

        // Typed literal nodes: Inspector editor for the literal value (one widget per TypeId)
        registry.Register(typeof(LiteralNode), new LiteralNodeDrawer(editService));

        // AN5 (D-B): ChannelCommandNodeDrawer now renders ChannelType/ActionId as read-only
        // labels (action is baked at creation via the per-action palette; no mutation path).
        registry.Register(typeof(ChannelCommandNode), new ChannelCommandNodeDrawer(channelCatalog));

        // Slice 2a-3: GetSharedNode/SetSharedNode — VariableId (free-text) + SharedTypeId
        // (filtered picker over ISharedStructTypeProvider) editable post-placement; see
        // SharedNodeDrawers.cs for the picker rationale.
        registry.Register(typeof(GetSharedNode), new GetSharedNodeDrawer(editService, sharedStructTypeProvider));
        registry.Register(typeof(SetSharedNode), new SetSharedNodeDrawer(editService, sharedStructTypeProvider));

        // CA-02: GetComponentNode -- ComponentTypeFqn (filtered picker over IComponentTypeProvider,
        // always re-bakes the full field set; no collapse toggle, see ComponentNodeDrawers.cs).
        registry.Register(typeof(GetComponentNode), new GetComponentNodeDrawer(editService, componentTypeProvider));

        // CA-04: SetComponentNode -- same picker mechanism as GetComponentNode, but over the
        // WRITABLE-only provider ([BlueprintWritable]-gated), see ComponentNodeDrawers.cs.
        registry.Register(typeof(SetComponentNode), new SetComponentNodeDrawer(editService, writableComponentTypeProvider));

        // ANC-P5-08a: Register PlayMontageChainNode drawer (if animation queries available)
        if (animationQueries != null && currentClassProvider != null)
        {
            registry.Register(typeof(BranchNode), new PlayMontageChainNodeDrawer(
                animationQueries, editService, currentClassProvider));
        }

        return registry;
    }

    /// <summary>
    /// Creates and populates a NodeKindRegistry with all palette entries.
    /// Called by the editor subsystem at startup.
    /// </summary>
    /// <param name="channelCatalog">
    /// AN4: optional channel-command catalog.  When non-null, one palette entry per
    /// channel-command action is registered (kind = <c>"ChannelCommand:{channel}:{action}"</c>,
    /// action baked at creation).  When null, no channel-command entries are added (the generic
    /// single entry has been removed per D-B: no chameleon hazard).
    /// </param>
    /// <param name="behaviorActionCatalog">
    /// AN7: optional unified behavior-action catalog (<see cref="IBehaviorActionCatalog"/>).
    /// When non-null, one palette entry per Blueprint-valid NON-channel action is registered
    /// (kind = <c>"Action:{FQN}"</c>, <see cref="ChannelCommandNode.ActionFqn"/> baked at
    /// creation).  When null, no non-channel action entries are added.
    /// </param>
    public static NodeKindRegistry CreatePaletteRegistry(
        IChannelCommandCatalog?   channelCatalog        = null,
        IBehaviorActionCatalog?   behaviorActionCatalog = null)
    {
        var registry = new NodeKindRegistry();

        // WHEN-M11-T2: Register the three When-Node palette entries (hand-authored pins).
        registry.Register(WhenNodePaletteEntries.WhenNode());
        registry.Register(WhenNodePaletteEntries.ReadEqsResult());
        registry.Register(WhenNodePaletteEntries.SpawnEqsSensor());

        // BCP-BATCH-02-FIX2 Task 2: register the full set of built-in blueprint node kinds
        // so the TAB / wire-drop picker offers the complete vocabulary, grouped by category.
        // Pins are projected by NodePinSchema at render time (projection-only).
        // NOTE: ChannelCommandNode is not included in All() (AN4).
        foreach (var descriptor in BlueprintNodePaletteEntries.All())
            registry.Register(descriptor);

        // AN4: register one palette entry per channel-command action.
        // Each entry bakes ChannelType + ActionId in its CreateInstance factory.
        foreach (var descriptor in BlueprintNodePaletteEntries.ChannelCommandEntries(channelCatalog))
            registry.Register(descriptor);

        // AN7: register one palette entry per Blueprint-valid NON-channel action.
        // Each entry bakes ActionFqn in its CreateInstance factory (ChannelType/ActionId empty).
        // Compile lowering is deferred to AN8.
        foreach (var descriptor in BlueprintNodePaletteEntries.NonChannelActionEntries(behaviorActionCatalog))
            registry.Register(descriptor);

        // BATCH-05B: register BlueprintMath function-call presets (Math/* categories).
        foreach (var descriptor in BlueprintMathPaletteEntries.All())
            registry.Register(descriptor);

        // Q#12: register discovered [BlueprintCallable] CLR helpers (curated picker; designers never
        // type an FQN). Attribute-driven analogue of the hand-written entries above.
        foreach (var descriptor in BlueprintCallablePaletteEntries.Discover())
            registry.Register(descriptor);

        // Q#14: register a "Publish: {Event}" entry per discovered custom event (C# [BlueprintEvent] +
        // editor-authored defs). Each drops a PublishEvent node baked with the event's FQN + fields.
        foreach (var descriptor in BlueprintEventPaletteEntries.PublishEntries())
            registry.Register(descriptor);

        // Q#14 Option B: register a "Make {Struct}"/"Break {Struct}" pair per discovered
        // [BlackboardDtoStruct]. Each drops a Make/BreakStruct node baked with the struct FQN + fields.
        foreach (var descriptor in MakeBreakStructPaletteEntries.Entries(new ReflectionSharedStructTypeProvider()))
            registry.Register(descriptor);

        // CA-02: register a "Get Component: {Type}" entry per discovered ECS component type
        // ([ComponentId]-marked struct/class) with at least one reflectable field. Each drops a
        // GetComponentNode baked with the component FQN + its full reflected field set.
        foreach (var descriptor in ComponentPaletteEntries.GetComponentEntries(new ReflectionComponentTypeProvider()))
            registry.Register(descriptor);

        // CA-04: register a "Set Component: {Type}" entry per discovered [BlueprintWritable] ECS
        // component type with at least one reflectable field. Each drops a SetComponentNode baked
        // with the component FQN + its full reflected field set.
        foreach (var descriptor in ComponentPaletteEntries.SetComponentEntries(new ReflectionWritableComponentTypeProvider()))
            registry.Register(descriptor);

        return registry;
    }

    /// <summary>
    /// Creates the list of attachment providers for the canvas.
    /// Called by the editor subsystem at startup.
    /// </summary>
    public static List<IAttachmentProvider> CreateAttachmentProviders(
        EqsTemplateRegistry eqsTemplates,
        Func<Guid, string?> peerNameResolver)
    {
        var providers = new List<IAttachmentProvider>();

        // WHEN-M11-T3: Register the visual attachment providers
        providers.Add(new WhenNodeAttachmentProvider());
        providers.Add(new ReadEqsResultAttachmentProvider());
        providers.Add(new EqsTemplateAttachmentProvider(eqsTemplates));
        providers.Add(new CrossAssetDependencyAttachmentProvider(peerNameResolver));

        return providers;
    }

    /// <summary>
    /// Creates the list of custom canvas renderers.
    /// Includes the WhenFiringPulseRenderer only in Debug builds.
    /// </summary>
    public static List<ICustomCanvasRenderer> CreateCanvasRenderers()
    {
        var renderers = new List<ICustomCanvasRenderer>();

#if DEBUG
        // WHEN-M11-T3: WhenFiringPulseRenderer is Debug-mode only
        renderers.Add(new WhenFiringPulseRenderer());
#endif

        return renderers;
    }

    /// <summary>
    /// Draws bookmark edge-markers (arrows at the canvas edge pointing to off-screen
    /// bookmarked viewports, slots 1-9) for the given document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT registered through <see cref="CreateCanvasRenderers"/> /
    /// <see cref="ICustomCanvasRenderer"/>: <see cref="BookmarkEdgeMarkerRenderer"/> is a
    /// static helper (not an <see cref="ICustomCanvasRenderer"/> implementation) that needs
    /// direct <see cref="GraphView"/> access (<c>Interaction.BeginViewportTween</c> for
    /// click-to-jump) which <see cref="ICanvasRenderContext"/> deliberately does not expose
    /// (render-only contract). <c>NodeEditor.Demo.DemoShell</c> — the reference usage —
    /// calls it the same way: directly, in a dedicated input-transparent overlay child
    /// window drawn immediately after the canvas, not through the custom-renderer pass.
    /// This method mirrors that exact convention.
    /// </para>
    /// <para>
    /// Call once per frame after the canvas has rendered (e.g. from
    /// <c>AiGraphCanvasWindow.AfterDraw</c>), only when the document has a
    /// <see cref="BookmarkStore"/> attached.
    /// </para>
    /// </remarks>
    public static void DrawBookmarkEdgeMarkers(GraphView view, BookmarkStore store, IEditorTheme theme)
    {
        ImGui.SetCursorScreenPos(view.Viewport.CanvasScreenOrigin);
        if (ImGui.BeginChild("##bookmark_edge_markers", view.Viewport.CanvasScreenSize, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs))
        {
            BookmarkEdgeMarkerRenderer.Render(view, store, theme);
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// Enumerates all blueprint recipe files from the production location
    /// (Hrot.AI.Behaviors/Recipes/Blueprints/). Returns only assets with
    /// EditorMetadata.Recipe != null.
    /// </summary>
    /// <remarks>
    /// WHEN-M11-T4: Recipe discovery for the Asset Browser "New from Recipe" dialog.
    /// The production recipe path is relative to the Hrot.AI.Behaviors assembly location.
    /// </remarks>
    public static List<BlueprintAsset> DiscoverRecipes()
    {
        var recipes = new List<BlueprintAsset>();

        // Locate the Hrot.AI.Behaviors assembly directory
        var aiBehaviorsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

        if (aiBehaviorsAssembly == null)
        {
            // Assembly not loaded yet - return empty list
            return recipes;
        }

        var assemblyLocation = Path.GetDirectoryName(aiBehaviorsAssembly.Location);
        if (string.IsNullOrEmpty(assemblyLocation))
            return recipes;

        var recipesPath = Path.Combine(assemblyLocation, AssetRoots.RecipesRelative(AssetKind.Blueprint));
        if (!Directory.Exists(recipesPath))
            return recipes;

        // Enumerate all .bp.json files in the recipes directory.
        // Case-insensitive match: files may have drifted extension casing when authored
        // on Windows; PlatformDefault would silently miss them on Linux.
        var recipeFiles = Directory.GetFiles(recipesPath, "*.bp.json",
            new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });

        foreach (var filePath in recipeFiles)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var asset = BlueprintJsonServices.Deserialize(json);

                // Only include assets with Recipe metadata
                if (asset?.EditorMetadata.Recipe != null)
                {
                    recipes.Add(asset);
                }
            }
            catch
            {
                // Skip files that fail to deserialize
            }
        }

        return recipes;
    }
}
