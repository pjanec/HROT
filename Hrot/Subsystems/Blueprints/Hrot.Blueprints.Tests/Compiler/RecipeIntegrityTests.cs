using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class RecipeIntegrityTests
{
    // ---- loading helpers -----------------------------------------------

    private static BlueprintAsset LoadRecipe(string name)
    {
        var dir  = TestData.ResolveTestAssetsDir();
        var path = Path.Combine(dir, "Recipes", name + ".bp.json");
        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException($"Null from '{path}'");
    }

    private static CompileOptions RecipeCompileOptions(
        IReadOnlyList<BlueprintSignature>? siblings = null) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   EmptyChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());

    private static BlueprintSignature MakeSquadStateSignature()
    {
        var squadState = LoadRecipe("SquadState");
        return new BlueprintSignature(
            Path:                  "",
            AssetId:               squadState.AssetId,
            Name:                  squadState.Name,
            SanitizedName:         squadState.Name,
            BlueprintId:           0,
            Dispatch:              squadState.Dispatch,
            ExportedFunctionNames: squadState.Graphs
                .Where(g => g.Kind == GraphKind.Function)
                .Select(g => g.Name)
                .ToArray(),
            Hostings:              Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: squadState.CallablePeers.ToArray());
    }

    private sealed class EmptyChannelCommandCatalog : IChannelCommandCatalog
    {
        public static readonly EmptyChannelCommandCatalog Instance = new();
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
            Array.Empty<ChannelCommandCatalogEntry>();
    }

    // ---- AllRecipes_Parse -----------------------------------------------

    [Theory]
    [InlineData("CoverAwarePatrol")]
    [InlineData("HealthThresholdReaction")]
    [InlineData("SquadAwareEngagement")]
    [InlineData("MoveAndFireCombo")]
    [InlineData("SquadState")]
    public void AllRecipes_Parse(string name)
    {
        var asset = LoadRecipe(name);
        Assert.NotEqual(Guid.Empty, asset.AssetId);
        Assert.NotEmpty(asset.Name);
    }

    // ---- AllRecipes_HaveDescriptionsAndConcepts -------------------------

    [Theory]
    [InlineData("CoverAwarePatrol")]
    [InlineData("HealthThresholdReaction")]
    [InlineData("SquadAwareEngagement")]
    [InlineData("MoveAndFireCombo")]
    [InlineData("SquadState")]
    public void AllRecipes_HaveDescriptionsAndConcepts(string name)
    {
        var asset = LoadRecipe(name);
        Assert.NotNull(asset.EditorMetadata.Recipe);
        Assert.NotEmpty(asset.EditorMetadata.Recipe!.Description);
        Assert.True(asset.EditorMetadata.Recipe.ConceptsTaught.Count >= 2,
            $"{name}: expected >= 2 ConceptsTaught, got {asset.EditorMetadata.Recipe.ConceptsTaught.Count}");
    }

    // ---- AllRecipes_ValidateOnly_NoErrors --------------------------------

    [Theory]
    [InlineData("CoverAwarePatrol")]
    [InlineData("HealthThresholdReaction")]
    [InlineData("MoveAndFireCombo")]
    [InlineData("SquadState")]
    public void AllRecipes_ValidateOnly_NoErrors(string name)
    {
        var asset  = LoadRecipe(name);
        var opts   = RecipeCompileOptions();
        var result = new BlueprintCompiler().Compile(asset, opts);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void SquadAwareEngagement_ValidateOnly_NoErrors()
    {
        // Recipe 3 needs SquadState (Recipe 5) as a sibling to pass BP2004.
        var asset  = LoadRecipe("SquadAwareEngagement");
        var opts   = RecipeCompileOptions(siblings: new[] { MakeSquadStateSignature() });
        var result = new BlueprintCompiler().Compile(asset, opts);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    // ---- CoverAwarePatrol_UsesAllThreeNewNodes ---------------------------

    [Fact]
    public void CoverAwarePatrol_UsesAllThreeNewNodes()
    {
        var asset    = LoadRecipe("CoverAwarePatrol");
        var allNodes = asset.Graphs.SelectMany(g => g.Nodes).ToList();
        Assert.Contains(allNodes, n => n is WhenNode);
        Assert.Contains(allNodes, n => n is ReadEqsResultNode);
        Assert.Contains(allNodes, n => n is SpawnEqsSensorNode);
    }

    // ---- AllRecipes_HaveStableAssetIds ----------------------------------

    [Theory]
    [InlineData("CoverAwarePatrol",        "00000000-aaaa-0001-0000-000000000001")]
    [InlineData("HealthThresholdReaction", "00000000-aaaa-0001-0000-000000000002")]
    [InlineData("SquadAwareEngagement",    "00000000-aaaa-0001-0000-000000000003")]
    [InlineData("MoveAndFireCombo",        "00000000-aaaa-0001-0000-000000000004")]
    [InlineData("SquadState",              "00000000-aaaa-0001-0000-000000000005")]
    public void AllRecipes_HaveStableAssetIds(string name, string expectedId)
    {
        var asset1 = LoadRecipe(name);
        var asset2 = LoadRecipe(name);
        Assert.Equal(new Guid(expectedId), asset1.AssetId);
        Assert.Equal(asset1.AssetId, asset2.AssetId);
    }

    // ---- CrossReferenceResolves_SquadAware_ReferencesSquadState ---------

    [Fact]
    public void CrossReferenceResolves_SquadAware_ReferencesSquadState()
    {
        var squadAware    = LoadRecipe("SquadAwareEngagement");
        var squadStateId  = new Guid("00000000-aaaa-0001-0000-000000000005");
        Assert.Contains(squadAware.CallablePeers, id => id == squadStateId);
    }
}
