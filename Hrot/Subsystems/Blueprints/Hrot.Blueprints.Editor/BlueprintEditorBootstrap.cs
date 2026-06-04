using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Hrot.Editor.AiShared.Catalog;
using Fdp.Toolkit.ReplayBrowser.Search;
using NodeEditor.Core.Interfaces;
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
        Func<string?>? currentClassProvider = null)
    {
        var registry = new BlueprintNodeDrawerRegistry();

        // WHEN-M11-T1: Register the three When-Node drawers
        registry.Register(typeof(WhenNode), new WhenNodeDrawer(
            channelCatalog, eventCatalog, editService, predicateCompiler));
        registry.Register(typeof(ReadEqsResultNode), new ReadEqsResultNodeDrawer());
        registry.Register(typeof(SpawnEqsSensorNode), new SpawnEqsSensorNodeDrawer(eqsTemplates));

        // BATCH-03D1: Register FunctionCallNode drawer
        registry.Register(typeof(FunctionCallNode), new FunctionCallNodeDrawer(editService));

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
    public static NodeKindRegistry CreatePaletteRegistry()
    {
        var registry = new NodeKindRegistry();

        // WHEN-M11-T2: Register the three When-Node palette entries (hand-authored pins).
        registry.Register(WhenNodePaletteEntries.WhenNode());
        registry.Register(WhenNodePaletteEntries.ReadEqsResult());
        registry.Register(WhenNodePaletteEntries.SpawnEqsSensor());

        // BCP-BATCH-02-FIX2 Task 2: register the full set of built-in blueprint node kinds
        // so the TAB / wire-drop picker offers the complete vocabulary, grouped by category.
        // Pins are projected by NodePinSchema at render time (projection-only).
        foreach (var descriptor in BlueprintNodePaletteEntries.All())
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
    /// Enumerates all blueprint recipe files from the production location
    /// (Hrot.AI.Behaviors/Blueprints/Recipes/). Returns only assets with
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

        var recipesPath = Path.Combine(assemblyLocation, "Blueprints", "Recipes");
        if (!Directory.Exists(recipesPath))
            return recipes;

        // Enumerate all .bp.json files in the recipes directory
        var recipeFiles = Directory.GetFiles(recipesPath, "*.bp.json");

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
