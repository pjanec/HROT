using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Patch 2 and Patch 4: verifies ResolveRegistrarArgument throws for forbidden types.
/// Tests that 2-parameter registrar (BlueprintRegistryStaging, BehaviorRegistry) is invoked correctly.
/// </summary>
[Collection("DebugProbe")]
public sealed class RegistrarInjectionTests
{
    [Fact]
    public void ResolveRegistrarArgument_BlueprintRegistry_ThrowsWithRcuMessage()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        ResolveRegistrarArgument_BlueprintRegistry_ThrowsWithRcuMessage_Body(out alcWeakRefs);
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
    private static void ResolveRegistrarArgument_BlueprintRegistry_ThrowsWithRcuMessage_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        // Compile an assembly with a registrar that requests BlueprintRegistry (forbidden, Patch 4).
        const string source = @"
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;

[BlueprintRegistrar]
public static class ForbiddenRegistrar
{
    public static void Register(BlueprintRegistryStaging staging, BlueprintRegistry registry)
    {
        // This should never be called.
    }
}
";
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var roslynCompiler = new InMemoryRoslynCompiler(resolver);
        var sink = new DiagnosticSink();
        var (assembly, alc) = roslynCompiler.CompileAndLoad(
            source, "ForbiddenReg.g.cs", "ForbiddenReg", sink);

        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => fixture.SimulateReloadFromAlc(alc, assembly));

        Assert.Contains("BlueprintRegistryStaging", ex.Message);
        Assert.Contains("RCU contract", ex.Message);
        alc      = null;
        assembly = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [Fact]
    public void ResolveRegistrarArgument_HsmActionDispatcher_ThrowsWithStaticClassMessage()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        ResolveRegistrarArgument_HsmActionDispatcher_ThrowsWithStaticClassMessage_Body(out alcWeakRefs);
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
    private static void ResolveRegistrarArgument_HsmActionDispatcher_ThrowsWithStaticClassMessage_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        // C# cannot compile HsmActionDispatcher as a method parameter type (CS0721:
        // static class cannot be used as a type argument / parameter). We must use
        // System.Reflection.Emit at IL level to create such a type.

        // Create a non-collectible dynamic assembly with a type that has a method
        // accepting HsmActionDispatcher as a parameter. The ALC we pass to
        // SimulateReloadFromAlc is a separate collectible fake (the coordinator
        // only needs an ALC to swap; the assembly itself is what gets scanned).
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var dynAssemblyName = new AssemblyName($"HsmDispatcherTest_{Guid.NewGuid():N}");
        var dynAssembly     = AssemblyBuilder.DefineDynamicAssembly(
            dynAssemblyName, AssemblyBuilderAccess.Run);
        var dynModule       = dynAssembly.DefineDynamicModule("MainModule");

        // Define the registrar type with the [BlueprintRegistrar] attribute.
        var typeBuilder = dynModule.DefineType(
            "HsmDispatcherRegistrar",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);  // static

        var attrCtor = typeof(BlueprintRegistrarAttribute).GetConstructor(Type.EmptyTypes)!;
        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(attrCtor, Array.Empty<object>()));

        // Define: public static void Register(BlueprintRegistryStaging staging, HsmActionDispatcher d)
        var methodBuilder = typeBuilder.DefineMethod(
            "Register",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            new[] { typeof(BlueprintRegistryStaging), typeof(HsmActionDispatcher) });

        var il = methodBuilder.GetILGenerator();
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();

        // Create a separate collectible ALC (coordinator expects a collectible one to swap).
        var fakeAlc = new AssemblyLoadContext($"FakeAlc_{Guid.NewGuid():N}", isCollectible: true);

        // Act & Assert: coordinator must reject the HsmActionDispatcher parameter.
        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => fixture.SimulateReloadFromAlc(fakeAlc, dynAssembly));

        Assert.Contains("static class", ex.Message);
        fakeAlc = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [Fact]
    public void AiPrimitive_TwoParameterRegistrar_IsInvokedCorrectly()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        AiPrimitive_TwoParameterRegistrar_IsInvokedCorrectly_Body(out alcWeakRefs);
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
    private static void AiPrimitive_TwoParameterRegistrar_IsInvokedCorrectly_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        // A valid AiPrimitive registrar has (BlueprintRegistryStaging, BehaviorRegistry).
        // Compiling MoveToAndFire invokes it without exception.
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

        // Should not throw.
        fixture.CompileAndLoad(asset);

        // The BlueprintRegistry should contain MoveToAndFire.
        Assert.True(fixture.Registry.TryGetByName("MoveToAndFire", out var def));
        Assert.Equal(BlueprintDispatchKind.AiPrimitive, def!.Kind);
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
