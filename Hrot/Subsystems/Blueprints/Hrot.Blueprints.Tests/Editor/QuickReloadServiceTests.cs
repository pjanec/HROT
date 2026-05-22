using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Reload;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class QuickReloadServiceTests
{
    private sealed class StubCatalog : IAssetCatalog
    {
        public IEnumerable<AssetCatalogEntry> EnumerateAll() => [];
    }

    private static QuickReloadService MakeService(
        IOutputConsole? console = null,
        IBlueprintCompiler? compiler = null,
        AiHotReloadCoordinator? coordinator = null)
    {
        console     ??= new MockOutputConsole();
        compiler    ??= new BlueprintCompiler();
        coordinator ??= new AiHotReloadCoordinator(
            new BehaviorRegistry(),
            new BlueprintRegistry(),
            new AiHotReloadCoordinatorOptions());

        return new QuickReloadService(
            new StubCatalog(),
            new EditorState(),
            console,
            compiler,
            coordinator);
    }

    // SC1 -- Non-null asset triggers log output
    [Fact]
    public async Task QuickReloadService_TriggerAsync_LogsToOutputConsole()
    {
        var console = new MockOutputConsole();
        var service = MakeService(console: console);
        var asset   = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestAsset" };

        await service.TriggerAsync(asset);

        Assert.True(console.InfoMessages.Count > 0);
    }

    // SC2 -- Null asset throws ArgumentNullException
    [Fact]
    public async Task QuickReloadService_TriggerAsync_NonNullAsset_Required()
    {
        var service = MakeService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.TriggerAsync(null!));
    }

    // SC3 -- Constructor validates required parameters
    [Fact]
    public void QuickReloadService_Constructor_ThrowsOnNullParams()
    {
        var console     = new MockOutputConsole();
        var compiler    = new BlueprintCompiler();
        var coordinator = new AiHotReloadCoordinator(
            new BehaviorRegistry(), new BlueprintRegistry(),
            new AiHotReloadCoordinatorOptions());

        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(null!, new EditorState(), console, compiler, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(new StubCatalog(), null!, console, compiler, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(new StubCatalog(), new EditorState(), null!, compiler, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(new StubCatalog(), new EditorState(), console, null!, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(new StubCatalog(), new EditorState(), console, compiler, null!));
    }

    // SC4 -- Full pipeline: compile MoveToAndFire, trigger reload, verify coordinator applied it
    [Fact]
    public async Task QuickReloadService_TriggerAsync_FullPipeline_SucceedsAndAppliesReload()
    {
        var registry    = new BlueprintRegistry();
        var behReg      = new BehaviorRegistry();
        var coordinator = new AiHotReloadCoordinator(
            behReg, registry, new AiHotReloadCoordinatorOptions());
        var console     = new MockOutputConsole();
        var service     = new QuickReloadService(
            new StubCatalog(), new EditorState(), console,
            new BlueprintCompiler(), coordinator);

        // Build MoveToAndFire with BTreeAction + HsmAction hosting
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveToAndFire")
            .WithHostings(AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        // Ensure Fhsm.Kernel assembly is loaded into the AppDomain for Stage8 Roslyn compilation.
        // It is a transitive dependency that may not be loaded until first use; load it explicitly.
        var fhsmPath = Path.Combine(AppContext.BaseDirectory, "Fhsm.Kernel.dll");
        if (File.Exists(fhsmPath))
            System.Reflection.Assembly.LoadFrom(fhsmPath);

        var result = await service.TriggerAsync(asset);

        // Pipeline succeeded
        string diagMsg = string.Join("; ", console.ErrorMessages);
        Assert.True(result.Succeeded, $"Expected success but got: {result.ErrorMessage} | Console errors: {diagMsg}");
        Assert.Null(result.ErrorMessage);
        Assert.True(result.DurationMs >= 0);

        // Coordinator applied the reload: blueprint is now in the registry
        Assert.True(registry.TryGetByName("MoveToAndFire", out _),
            "Blueprint 'MoveToAndFire' should be registered after quick reload");

        // BTree action thunk was registered
        int expectedId = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(behReg.TryGetAction(expectedId, out var actionEntry),
            "BTree action thunk should be registered after quick reload");
        Assert.Equal("MoveToAndFire", actionEntry.Name);

        // Sibling signatures were built
        Assert.NotNull(service.LastSignaturesUsedForTesting);
        Assert.True(service.LastSignaturesUsedForTesting!.Count >= 1);
    }
}

