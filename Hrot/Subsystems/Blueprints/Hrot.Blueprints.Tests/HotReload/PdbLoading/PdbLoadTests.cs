using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// PDB loading: when LoadPdbOnDeveloperMode=true, assembly loads with PDB symbols accessible.
/// </summary>
[Collection("DebugProbe")]
public sealed class PdbLoadTests
{
    [Fact]
    public void CompileWithPdb_AiPrimitive_AssemblyLoadsSuccessfully()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        CompileWithPdb_AiPrimitive_AssemblyLoadsSuccessfully_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed after 20 retries.");
    }

    // [NoInlining] confines all ALC-touching locals (including fixture) to this frame so
    // the GC loop in the [Fact] runs with no ALC-holding roots on the stack (DEBT-009).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CompileWithPdb_AiPrimitive_AssemblyLoadsSuccessfully_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        // Use Debug compiler mode (embeds PDB source).
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

        // CompileAndLoad in Debug mode (already the default) embeds PDB.
        var assembly = fixture.CompileAndLoad(asset, CompilerMode.Debug);

        // Assembly should be non-null and have the expected type.
        Assert.NotNull(assembly);
        var types = assembly.GetTypes();
        Assert.Contains(types, t => t.Name.Contains("MoveToAndFire") && t.Name.EndsWith("_Bp"));
        types    = null;
        assembly = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
