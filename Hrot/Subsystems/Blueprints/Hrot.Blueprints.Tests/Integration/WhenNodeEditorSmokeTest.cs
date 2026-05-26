using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Core;
using Hrot.Editor.AiShared;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// WHEN-M11-T6: End-to-end smoke test validating all M11 production wiring.
/// This test serves as a regression guard for the When-Node reactivity feature,
/// ensuring that all bootstrap wiring (drawers, palette, attachments, recipes)
/// remains functional across future changes.
/// </summary>
public sealed class WhenNodeEditorSmokeTest
{
    /// <summary>
    /// Comprehensive smoke test that validates all five M11 tasks are correctly wired:
    /// - M11-T1: DrawerRegistry has three new drawers
    /// - M11-T2: Palette has three new entries
    /// - M11-T3: Canvas attachment providers registered
    /// - M11-T4: Recipes available from production location
    /// - M11-T5: ReactiveGuardVocabulary resolves to canonical type
    /// </summary>
    [Fact]
    public void EditorSmokeTest_AllM11Wiring_WorksEndToEnd()
    {
        // ── M11-T1: Drawer registry populated ───────────────────────────────────

        var drawerRegistry = CreateTestDrawerRegistry();

        var whenDrawer = drawerRegistry.GetDrawerFor(new WhenNode { Id = Guid.NewGuid() });
        Assert.NotNull(whenDrawer);
        Assert.IsType<WhenNodeDrawer>(whenDrawer);

        var readEqsDrawer = drawerRegistry.GetDrawerFor(new ReadEqsResultNode { Id = Guid.NewGuid() });
        Assert.NotNull(readEqsDrawer);
        Assert.IsType<ReadEqsResultNodeDrawer>(readEqsDrawer);

        var spawnEqsDrawer = drawerRegistry.GetDrawerFor(new SpawnEqsSensorNode { Id = Guid.NewGuid() });
        Assert.NotNull(spawnEqsDrawer);
        Assert.IsType<SpawnEqsSensorNodeDrawer>(spawnEqsDrawer);

        // ── M11-T2: Palette entries present ─────────────────────────────────────

        var paletteRegistry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        var whenDescriptor = paletteRegistry.TryGet("When");
        Assert.NotNull(whenDescriptor);
        Assert.Equal("When", whenDescriptor.DisplayName);
        Assert.Equal(ReactiveGuardVocabulary.CategoryName, whenDescriptor.Category);

        var readEqsDescriptor = paletteRegistry.TryGet("ReadEqsResult");
        Assert.NotNull(readEqsDescriptor);
        Assert.Equal("Read EQS Result", readEqsDescriptor.DisplayName);

        var spawnEqsDescriptor = paletteRegistry.TryGet("SpawnEqsSensor");
        Assert.NotNull(spawnEqsDescriptor);
        Assert.Equal("Spawn EQS Sensor", spawnEqsDescriptor.DisplayName);

        // ── M11-T3: Canvas attachment providers registered ──────────────────────

        var attachmentProviders = BlueprintEditorBootstrap.CreateAttachmentProviders(
            new EqsTemplateRegistry(), _ => null);

        Assert.Contains(attachmentProviders, p => p is WhenNodeAttachmentProvider);
        Assert.Contains(attachmentProviders, p => p is ReadEqsResultAttachmentProvider);
        Assert.Contains(attachmentProviders, p => p is EqsTemplateAttachmentProvider);
        Assert.Contains(attachmentProviders, p => p is CrossAssetDependencyAttachmentProvider);

        // ── M11-T4: Recipes available in Asset Browser ──────────────────────────

        var recipes = LoadTestRecipes();

        Assert.Contains(recipes, r => r.Name == "CoverAwarePatrol");
        Assert.Contains(recipes, r => r.Name == "HealthThresholdReaction");
        Assert.Contains(recipes, r => r.Name == "SquadAwareEngagement");
        Assert.Contains(recipes, r => r.Name == "MoveAndFireCombo");
        Assert.Contains(recipes, r => r.Name == "SquadState");

        // Verify all recipes have Recipe metadata
        Assert.All(recipes, r => Assert.NotNull(r.EditorMetadata.Recipe));

        // ── M11-T5: ReactiveGuardVocabulary is single declaration ───────────────

        var vocabularyType = typeof(ReactiveGuardVocabulary);
        Assert.Equal(
            "Hrot.Editor.AiShared.ReactiveGuardVocabulary",
            vocabularyType.FullName);

        // Verify vocabulary constants are accessible
        Assert.Equal("Reactive Guards", ReactiveGuardVocabulary.CategoryName);
        Assert.NotEmpty(ReactiveGuardVocabulary.BlueprintWhenNodeTooltip);
        Assert.NotEmpty(ReactiveGuardVocabulary.GenericTooltip);
    }

    /// <summary>
    /// Validates that recipes can be instantiated via NewFromRecipeService.
    /// This confirms the full recipe workflow: discovery → creation → validation.
    /// </summary>
    [Fact]
    public void RecipeWorkflow_DiscoverAndCreate_ProducesValidBlueprint()
    {
        var recipes = LoadTestRecipes();
        var coverAwarePatrol = recipes.FirstOrDefault(r => r.Name == "CoverAwarePatrol");

        Assert.NotNull(coverAwarePatrol);
        Assert.NotNull(coverAwarePatrol.EditorMetadata.Recipe);

        // Create a new blueprint from the recipe
        var service = new NewFromRecipeService();
        var newBlueprint = service.CreateFromRecipe(coverAwarePatrol, "TestCoverPatrol");

        // Verify the new blueprint has a fresh identity
        Assert.NotEqual(coverAwarePatrol.AssetId, newBlueprint.AssetId);
        Assert.Equal("TestCoverPatrol", newBlueprint.Name);
        Assert.Null(newBlueprint.EditorMetadata.Recipe);  // Recipe metadata stripped

        // Verify the blueprint structure is cloned correctly
        Assert.NotEmpty(newBlueprint.Graphs);
        Assert.Equal(coverAwarePatrol.Graphs.Count, newBlueprint.Graphs.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads recipe blueprints from the test or production location.
    /// Prefers production location (Hrot.AI.Behaviors assembly output) but falls back
    /// to test assets if the assembly isn't loaded.
    /// </summary>
    private static List<BlueprintAsset> LoadTestRecipes()
    {
        var recipes = new List<BlueprintAsset>();

        // Try production location first (validates WHEN-M11-T4 deployment)
        var aiBehaviorsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

        string recipesPath;
        if (aiBehaviorsAssembly != null)
        {
            var assemblyLocation = Path.GetDirectoryName(aiBehaviorsAssembly.Location)!;
            recipesPath = Path.Combine(assemblyLocation, "Blueprints", "Recipes");
        }
        else
        {
            // Fallback: test assets location
            var testAssetsDir = TestData.ResolveTestAssetsDir();
            recipesPath = Path.Combine(testAssetsDir, "Recipes");
        }

        if (!Directory.Exists(recipesPath))
            return recipes;

        var recipeFiles = Directory.GetFiles(recipesPath, "*.bp.json");
        foreach (var filePath in recipeFiles)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var asset = BlueprintJsonServices.Deserialize(json);

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
    private static BlueprintNodeDrawerRegistry CreateTestDrawerRegistry()
    {
        var channelCatalog = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog = BuiltInEngineEventCatalog.Instance;
        var editService = new TestEditService();
        var predicateCompiler = new TestPredicateCompiler();
        var eqsTemplates = new EqsTemplateRegistry();

        return BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);
    }

    // ── Test stubs ───────────────────────────────────────────────────────────────

    private sealed class TestEditService : IEditService
    {
        public void Edit<T>(string label, ref T value, Action<T>? onChange = null) { }
        public void MarkDirty(BlueprintAsset asset) { }
    }

    private sealed class TestPredicateCompiler : IPredicateCompiler
    {
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public Func<EntityRepository, Entity, bool> CompileEntityPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto predicate)
            => Array.Empty<Type>();
    }
}
