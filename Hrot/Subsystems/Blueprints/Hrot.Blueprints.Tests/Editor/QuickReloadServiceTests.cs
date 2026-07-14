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
    // A dedicated EMPTY directory so the peer-source yields no siblings (matching the prior
    // in-memory empty IAssetCatalog stub). Pointing at Path.GetTempPath() would scan the whole
    // user temp tree — hitting inaccessible dirs and unrelated fixture *.bp.json (duplicate AssetIds).
    private static readonly BlueprintPeerSource StubCatalog =
        new BlueprintPeerSource(MakeEmptyPeerRoot());

    private static string MakeEmptyPeerRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qrs_peer_stub_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
            StubCatalog,
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
            new QuickReloadService(StubCatalog, null!, console, compiler, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(StubCatalog, new EditorState(), null!, compiler, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(StubCatalog, new EditorState(), console, null!, coordinator));
        Assert.Throws<ArgumentNullException>(() =>
            new QuickReloadService(StubCatalog, new EditorState(), console, compiler, null!));
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
            StubCatalog, new EditorState(), console,
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

        // I1: the BTree action thunk is now registered into the FastBTree ActionRegistry
        // (string-keyed, the registry the Interpreter binds from) rather than the orphaned
        // BehaviorRegistry int-dict. The ActionRegistry is internal to the scan/ApplyQuickReload
        // path and not surfaced by the coordinator, so it is not asserted here; the real
        // interpreter-binding proof is MoveToAndFire_InterpreterTick_Tests (I1 canary).

        // Sibling signatures were built
        Assert.NotNull(service.LastSignaturesUsedForTesting);
        Assert.True(service.LastSignaturesUsedForTesting!.Count >= 1);
    }

    // SC5 -- TriggerFromSourcesAsync: valid source → SUCCESS; broken source → FAILURE
    [Fact]
    public async Task QuickReloadService_TriggerFromSourcesAsync_ValidSucceeds_BrokenFails()
    {
        var registry    = new BlueprintRegistry();
        var behReg      = new BehaviorRegistry();
        var coordinator = new AiHotReloadCoordinator(
            behReg, registry, new AiHotReloadCoordinatorOptions());
        var console     = new MockOutputConsole();
        var service     = new QuickReloadService(
            StubCatalog, new EditorState(), console,
            new BlueprintCompiler(), coordinator);

        // Ensure Fhsm.Kernel assembly is loaded into the AppDomain for Roslyn compilation.
        var fhsmPath = Path.Combine(AppContext.BaseDirectory, "Fhsm.Kernel.dll");
        if (File.Exists(fhsmPath))
            System.Reflection.Assembly.LoadFrom(fhsmPath);

        // Valid source: minimal compilable C# class.
        var result = await service.TriggerFromSourcesAsync(
            new[] { ("public class TestFoo { }", "test.cs") },
            "TestAssembly_Valid");

        Assert.True(result.Succeeded, $"Expected success but got: {result.ErrorMessage}");
        Assert.Null(result.ErrorMessage);
        Assert.True(result.DurationMs >= 0);

        // Broken source: syntax error.
        var failResult = await service.TriggerFromSourcesAsync(
            new[] { ("class Broken { error!!! }", "broken.cs") },
            "TestAssembly_Broken");

        Assert.False(failResult.Succeeded);
        Assert.NotNull(failResult.ErrorMessage);
        Assert.True(failResult.DurationMs >= 0);
    }
}

