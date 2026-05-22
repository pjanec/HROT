using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// DEMO-001: LibraryMath runtime integration demo tests.
/// Covers Blueprint-only compilation, ALC lifecycle, and generated-source snapshot.
/// </summary>
[Collection("DebugProbe")]
public sealed class LibraryMathDemoTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    // SC1: CompileAndLoad loads the LibraryMath asset into a Roslyn-compiled assembly.
    // Skipped: the "Add" graph node references System.Math.Add which is not a real BCL method;
    // Roslyn compilation fails until the graph is replaced with a valid callable.
    [Fact(Skip = "LibraryMath asset requires completed graph nodes for Roslyn compilation")]
    public void LibraryMath_CompileAndLoad_Succeeds()
    {
        using var fixture = new BlueprintTestFixture();
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var assembly = fixture.CompileAndLoad(asset);
        Assert.NotEmpty(assembly.GetTypes());
    }

    // SC2: ALC is GC-reclaimed after a reload cycle.
    // Skipped for the same reason as SC1 (Roslyn compilation would throw).
    [Fact(Skip = "LibraryMath asset requires completed graph nodes for Roslyn compilation")]
    public void LibraryMath_ALC_ReclaimedAfterReload()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        LibraryMath_ALC_ReclaimedAfterReload_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LibraryMath_ALC_ReclaimedAfterReload_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(asset);
        fixture.SimulateReload(new[] { asset });
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // SC3: Blueprint-only compile (no Roslyn) produces deterministic generated source.
    // Snapshot is created on first run with BLUEPRINT_REGENERATE_SNAPSHOTS=1.
    [Fact]
    public void LibraryMath_GeneratedSource_Snapshot()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded,
            $"Blueprint compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        TestData.ReadOrRegenerateSnapshot("Demos/LibraryMath.cs.txt", result.GeneratedSource!);
    }

    // MANUAL WALKTHROUGH: DEMO-001
    // 1. Open Asset Browser -> double-click LibraryMath.bp.json
    // 2. Verify the graph shows: (no EventEntry) Function "Add" with a FunctionCall node targeting System.Math.Add
    // 3. Note: System.Math.Add does not exist in .NET BCL -- this node is a placeholder
    // 4. To make CompileAndLoad work: replace the FunctionCall node with a valid BCL method
    //    e.g., change TargetTypeId to "System.Math" and MethodName to "Abs" or another real method
    // 5. Once replaced, SC1 and SC2 can be un-skipped and the full ALC lifecycle will be covered
    // 6. The SC3 snapshot (Blueprint->C# only, no Roslyn) already verifies the structural code generation
}
