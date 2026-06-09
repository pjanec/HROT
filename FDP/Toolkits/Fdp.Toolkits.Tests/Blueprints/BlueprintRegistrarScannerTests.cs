using System;
using System.Reflection;
using System.Reflection.Emit;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;
using Xunit;

namespace Fdp.Toolkit.Blueprints.Tests;

/// <summary>
/// BSA-REGSCAN Task 1: Unit tests for <see cref="BlueprintRegistrarScanner.Scan"/>.
/// Verifies that the scanner correctly invokes registrar methods, populates staging buffers,
/// and throws for forbidden parameter types.
/// </summary>
public sealed class BlueprintRegistrarScannerTests
{
    // ── Test registrar types defined in this test assembly ────────────────────

    private const int KnownBlueprintId = unchecked((int)0xDEAD_BEEFu);

    /// <summary>
    /// Registrar that adds one blueprint definition to the staging buffer.
    /// Proves the scanner discovers and invokes BlueprintRegistryStaging-only registrars.
    /// </summary>
    [BlueprintRegistrar]
    public static class TestBlueprintRegistrar
    {
        public static void Register(BlueprintRegistryStaging staging)
        {
            staging.Add(KnownBlueprintId, new BlueprintDefinition
            {
                Name          = "ScannerTestBlueprint",
                Kind          = BlueprintDispatchKind.Instance,
                StateSize     = 32,
                StructureHash = 0xABCD1234_56789ABCuL,
            });
        }
    }

    private const int KnownBehaviorId = 9901;

    /// <summary>
    /// Registrar that adds one behavior definition to the behavior staging buffer.
    /// Proves the scanner discovers and invokes BehaviorRegistry-only registrars.
    /// </summary>
    [BlueprintRegistrar]
    public static class TestBehaviorRegistrar
    {
        public static void Register(BehaviorRegistry staging)
        {
            staging.Register(KnownBehaviorId, "ScannerTestBehavior",
                new BehaviorDefinition { Name = "ScannerTestBehavior", BrainTier = 1 });
        }
    }

    /// <summary>
    /// Registrar that adds one blueprint AND one behavior.
    /// Proves the scanner handles two-parameter registrars correctly.
    /// </summary>
    [BlueprintRegistrar]
    public static class TestDualRegistrar
    {
        private const int DualBpId   = unchecked((int)0xCAFE_BABEu);
        private const int DualBehId  = 9902;

        public static void Register(BlueprintRegistryStaging staging, BehaviorRegistry behaviors)
        {
            staging.Add(DualBpId, new BlueprintDefinition
            {
                Name          = "DualTestBlueprint",
                Kind          = BlueprintDispatchKind.Instance,
                StateSize     = 8,
                StructureHash = 0x1122334455667788uL,
            });
            behaviors.Register(DualBehId, "DualTestBehavior",
                new BehaviorDefinition { Name = "DualTestBehavior", BrainTier = 1 });
        }

        public static int ExpectedBpId  => DualBpId;
        public static int ExpectedBehId => DualBehId;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (BlueprintRegistryStaging bp, BehaviorRegistry beh) CreateStaging()
        => (new BlueprintRegistryStaging(), new BehaviorRegistry());

    // ── Test 1: blueprint staging receives the registered definition ──────────

    /// <summary>
    /// Scanning an assembly whose registrar calls staging.Add must result in the
    /// definition being present in the staging buffer after the scan.
    /// </summary>
    [Fact]
    public void Scan_BlueprintRegistrar_PopulatesStagingWithKnownDefinition()
    {
        var (staging, behaviors) = CreateStaging();

        BlueprintRegistrarScanner.Scan(
            typeof(TestBlueprintRegistrar).Assembly,
            staging,
            behaviors);

        // The definition must be reachable via CommitStaging.
        var registry = new BlueprintRegistry();
        registry.CommitStaging(staging);

        Assert.True(registry.TryGetById(KnownBlueprintId, out var def),
            $"Expected blueprint with id 0x{KnownBlueprintId:X8} to be registered.");
        Assert.NotNull(def);
        Assert.Equal("ScannerTestBlueprint", def!.Name);
        Assert.Equal(32, def.StateSize);
    }

    // ── Test 2: behavior registry receives the registered behavior ────────────

    /// <summary>
    /// Scanning an assembly whose registrar calls BehaviorRegistry.Register must
    /// result in the behavior being present after the scan.
    /// </summary>
    [Fact]
    public void Scan_BehaviorRegistrar_PopulatesBehaviorStagingWithKnownBehavior()
    {
        var (staging, behaviors) = CreateStaging();

        BlueprintRegistrarScanner.Scan(
            typeof(TestBehaviorRegistrar).Assembly,
            staging,
            behaviors);

        Assert.True(behaviors.TryGetId("ScannerTestBehavior", out int id),
            "Expected behavior 'ScannerTestBehavior' to be registered.");
        Assert.Equal(KnownBehaviorId, id);
    }

    // ── Test 3: dual-parameter registrar populates both buffers ──────────────

    [Fact]
    public void Scan_DualRegistrar_PopulatesBothBlueprintAndBehaviorStaging()
    {
        var (staging, behaviors) = CreateStaging();

        BlueprintRegistrarScanner.Scan(
            typeof(TestDualRegistrar).Assembly,
            staging,
            behaviors);

        var registry = new BlueprintRegistry();
        registry.CommitStaging(staging);

        Assert.True(registry.TryGetById(TestDualRegistrar.ExpectedBpId, out _),
            "Dual registrar must populate blueprint staging.");
        Assert.True(behaviors.TryGetId("DualTestBehavior", out int behId),
            "Dual registrar must populate behavior staging.");
        Assert.Equal(TestDualRegistrar.ExpectedBehId, behId);
    }

    // ── Test 4: BlueprintRegistry direct param guard ──────────────────────────

    /// <summary>
    /// A registrar method that requests a live <see cref="BlueprintRegistry"/> directly
    /// violates the RCU contract and must cause <see cref="HotReloadRegistrarException"/>.
    /// Uses IL emit to create the forbidden signature at runtime.
    /// </summary>
    [Fact]
    public void Scan_RegistrarRequestingBlueprintRegistryDirect_ThrowsHotReloadRegistrarException()
    {
        var assembly = BuildDynamicAssemblyWithRegistrar(
            "BlueprintRegistryDirectRegistrar",
            new[] { typeof(BlueprintRegistryStaging), typeof(BlueprintRegistry) });

        var (staging, behaviors) = CreateStaging();

        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => BlueprintRegistrarScanner.Scan(assembly, staging, behaviors));

        Assert.Contains("BlueprintRegistryStaging", ex.Message);
        Assert.Contains("RCU contract", ex.Message);
    }

    // ── Test 5: HsmActionDispatcher param guard ───────────────────────────────

    /// <summary>
    /// A registrar method that requests <c>HsmActionDispatcher</c> as a parameter
    /// (a static class that cannot be injected) must cause <see cref="HotReloadRegistrarException"/>.
    /// Uses IL emit to create the forbidden signature at runtime.
    /// </summary>
    [Fact]
    public void Scan_RegistrarRequestingHsmActionDispatcher_ThrowsHotReloadRegistrarException()
    {
        var assembly = BuildDynamicAssemblyWithRegistrar(
            "HsmDispatcherRegistrar",
            new[] { typeof(BlueprintRegistryStaging), typeof(HsmActionDispatcher) });

        var (staging, behaviors) = CreateStaging();

        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => BlueprintRegistrarScanner.Scan(assembly, staging, behaviors));

        Assert.Contains("static class", ex.Message);
    }

    // ── Test 6: Unknown param type guard ─────────────────────────────────────

    /// <summary>
    /// A registrar method with an entirely unknown parameter type must cause
    /// <see cref="HotReloadRegistrarException"/> with the type name in the message.
    /// </summary>
    [Fact]
    public void Scan_RegistrarWithUnknownParamType_ThrowsHotReloadRegistrarException()
    {
        // Use System.IO.Stream as a plausible unknown param type.
        var assembly = BuildDynamicAssemblyWithRegistrar(
            "UnknownParamRegistrar",
            new[] { typeof(BlueprintRegistryStaging), typeof(System.IO.Stream) });

        var (staging, behaviors) = CreateStaging();

        var ex = Assert.Throws<HotReloadRegistrarException>(
            () => BlueprintRegistrarScanner.Scan(assembly, staging, behaviors));

        Assert.Contains("System.IO.Stream", ex.Message);
    }

    // ── Test 7: Null argument guards ─────────────────────────────────────────

    [Fact]
    public void Scan_NullAssembly_ThrowsArgumentNullException()
    {
        var (staging, behaviors) = CreateStaging();
        Assert.Throws<ArgumentNullException>(
            () => BlueprintRegistrarScanner.Scan(null!, staging, behaviors));
    }

    [Fact]
    public void Scan_NullBlueprintStaging_ThrowsArgumentNullException()
    {
        var (_, behaviors) = CreateStaging();
        Assert.Throws<ArgumentNullException>(
            () => BlueprintRegistrarScanner.Scan(GetType().Assembly, null!, behaviors));
    }

    [Fact]
    public void Scan_NullBehaviorStaging_ThrowsArgumentNullException()
    {
        var (staging, _) = CreateStaging();
        Assert.Throws<ArgumentNullException>(
            () => BlueprintRegistrarScanner.Scan(GetType().Assembly, staging, null!));
    }

    // ── IL emit helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a dynamic assembly containing a <c>[BlueprintRegistrar]</c> class whose
    /// <c>Register</c> static method has the specified parameter types.
    /// The method body is a no-op (ret).
    /// </summary>
    private static Assembly BuildDynamicAssemblyWithRegistrar(
        string typeName,
        Type[] paramTypes)
    {
        var name    = new AssemblyName($"{typeName}_{Guid.NewGuid():N}");
        var dynAsm  = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var dynMod  = dynAsm.DefineDynamicModule("Main");
        var tb      = dynMod.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var attrCtor = typeof(BlueprintRegistrarAttribute).GetConstructor(Type.EmptyTypes)!;
        tb.SetCustomAttribute(new CustomAttributeBuilder(attrCtor, Array.Empty<object>()));

        var mb = tb.DefineMethod(
            "Register",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            paramTypes);

        var il = mb.GetILGenerator();
        il.Emit(OpCodes.Ret);

        tb.CreateType();
        return dynAsm;
    }
}
