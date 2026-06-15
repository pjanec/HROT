using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;
using FluentAssertions;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

// Fully qualify the Roslyn MetadataReferenceResolver to avoid collision with
// Microsoft.CodeAnalysis.MetadataReferenceResolver.
using RoslynMRR = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// PU-203: Integration tests for the <c>[BlueprintRegistrar]</c> self-registration bridge.
///
/// Strategy: generate the topology core + bridge C# source from a fixture asset's JSON
/// using the IncrementalGenerator, compile it in-memory via Roslyn (in a collectible ALC),
/// then run <c>AiHotReloadCoordinator.ScanForRegistrars</c> and verify:
/// - The bridge class is discovered (it carries <c>[BlueprintRegistrar]</c>).
/// - Invoking <c>Register</c> into a staging <c>BehaviorRegistry</c> registers the
///   JSON-owned definition AND (for BTree) action/condition thunks.
/// - The registered tree is tickable (interpreter runs without throwing).
/// - The HSM bridge registers the definition via <c>BehaviorRegistry</c>.
/// - Negative: bridge does NOT carry <c>[FbtRegistrar]</c>/<c>[HsmActionRegistrar]</c>.
/// - Negative: bridge does NOT request <c>BlueprintRegistry</c>/<c>HsmActionDispatcher</c>
///   as params (coordinator throws on those — §14 item 4).
///
/// Uses the FDP-layer <c>AiHotReloadCoordinator</c> (Fdp.Toolkit.Behavior) which owns
/// <c>ScanForRegistrars</c> + <c>ResolveRegistrarArgument</c>.
/// </summary>
public sealed class BlueprintRegistrarBridgeIntegrationTests : IDisposable
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    private readonly BehaviorRegistry  _liveRegistry      = new();
    private readonly BlueprintRegistry _blueprintRegistry = new();

    public void Dispose() => _liveRegistry.Clear();

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static BehaviorTreeAsset LoadBTree(string name)
    {
        var c = new BTreeAssetContributor();
        c.LoadFrom(BehaviorsAssembly);
        var a = c.Enumerate().FirstOrDefault(x => x.Name == name);
        if (a is null) throw new InvalidOperationException($"BTree '{name}' not found");
        return (BehaviorTreeAsset)a;
    }

    /// <summary>
    /// Maps the BTree asset to a DTO and fills in type names from the assembly's
    /// CreateBuilder reflection when the DTO has empty names (pre-existing limitation:
    /// BTreeAssetContributor.RegisterBlobCore passes string.Empty for type names).
    /// </summary>
    private static BehaviorTreeAssetDto ToDtoWithTypeNames(BehaviorTreeAsset asset, string className)
    {
        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        if (string.IsNullOrEmpty(dto.BlackboardTypeName) || string.IsNullOrEmpty(dto.ContextTypeName))
        {
            // Reflect on the asset's CreateBuilder return type generic args
            var type = BehaviorsAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == className &&
                    t.GetMethod("CreateBuilder", BindingFlags.Public | BindingFlags.Static) != null);

            if (type != null)
            {
                var cb = type.GetMethod("CreateBuilder", BindingFlags.Public | BindingFlags.Static);
                if (cb?.ReturnType.IsGenericType == true)
                {
                    var args = cb.ReturnType.GetGenericArguments();
                    if (args.Length >= 2)
                    {
                        if (string.IsNullOrEmpty(dto.BlackboardTypeName))
                            dto.BlackboardTypeName = args[0].FullName ?? args[0].Name;
                        if (string.IsNullOrEmpty(dto.ContextTypeName))
                            dto.ContextTypeName = args[1].FullName ?? args[1].Name;
                    }
                }
            }
        }

        return dto;
    }

    private static HsmAsset LoadHsm(string name)
    {
        var c = new HsmAssetContributor();
        c.LoadFrom(BehaviorsAssembly);
        var a = c.Enumerate().FirstOrDefault(x => x.Name == name);
        if (a is null) throw new InvalidOperationException($"HSM '{name}' not found");
        return (HsmAsset)a;
    }

    /// <summary>
    /// Generates both source files for a BTree asset and returns them as a string array
    /// [topology-core, bridge] so they can be compiled as separate syntax trees.
    /// </summary>
    private static string[] GenerateBTreeSources(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        var compilation = CSharpCompilation.Create(
            "Gen",
            Array.Empty<SyntaxTree>(),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty($"generator must not produce diagnostics for '{assetName}'");
        result.GeneratedTrees.Should().HaveCount(2, "must produce topology core + bridge");
        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    /// <summary>
    /// Generates both source files for an HSM asset and returns them as a string array.
    /// </summary>
    private static string[] GenerateHsmSources(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.hsm.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new HsmJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        var compilation = CSharpCompilation.Create(
            "Gen",
            Array.Empty<SyntaxTree>(),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty($"generator must not produce diagnostics for '{assetName}'");
        result.GeneratedTrees.Should().HaveCount(2, "must produce topology core + bridge");
        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    /// <summary>
    /// Compiles multiple C# source strings (each as its own SyntaxTree) + loads into a
    /// collectible ALC. Passes all runtime assemblies as metadata references so compiled
    /// code can use Fdp.Toolkits, Fbt, Fhsm.Kernel, etc.
    /// </summary>
    private static (Assembly Assembly, AssemblyLoadContext Alc) CompileMultiAndLoad(
        string[] sources, string assemblyName)
    {
        var resolver = RoslynMRR.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var refs = resolver.Resolve();

        var syntaxTrees = sources
            .Select((src, i) =>
            {
                var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(
                    src, System.Text.Encoding.UTF8);
                return CSharpSyntaxTree.ParseText(
                    sourceText,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: $"{assemblyName}_{i}.g.cs");
            })
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true,
                allowUnsafe: true));

        using var peStream  = new System.IO.MemoryStream();
        using var pdbStream = new System.IO.MemoryStream();
        var result = compilation.Emit(peStream, pdbStream);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var loc = d.Location.GetMappedLineSpan();
                    return $"{d.Id}({loc.Path}:{loc.StartLinePosition.Line+1}): {d.GetMessage()}";
                }));
            throw new InvalidOperationException(
                $"In-memory compilation of '{assemblyName}' failed:\n{errors}");
        }

        peStream.Position  = 0;
        pdbStream.Position = 0;
        var alc = new AssemblyLoadContext($"BridgeTest_{assemblyName}", isCollectible: true);
        var asm = alc.LoadFromStream(peStream, pdbStream);
        return (asm, alc);
    }

    /// <summary>
    /// Creates the FDP-layer AiHotReloadCoordinator (owns ScanForRegistrars + InvokeRegistrar).
    /// </summary>
    private AiHotReloadCoordinator CreateCoordinator() =>
        new AiHotReloadCoordinator(_liveRegistry, _blueprintRegistry,
            new AiHotReloadCoordinatorOptions());

    // ─────────────────────────────────────────────────────────────────────────────
    // PU-203 BTree: bridge discovered, registered, tickable
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BTree_SampleScout_Bridge_IsDiscoveredByScanForRegistrars()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_SampleScout_Bridge_IsDiscoveredByScanForRegistrars_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_SampleScout_Bridge_IsDiscoveredByScanForRegistrars_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // Arrange: generate + compile the SampleScout topology core + bridge
        var model  = LoadBTree("SampleScout");
        var dto    = ToDtoWithTypeNames(model, "SampleScout");
        string json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateBTreeSources(json, "SampleScout");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleScoutBridgeTest");

        // Act: scan for registrars in the compiled assembly
        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);

        // Assert: exactly one [BlueprintRegistrar] class found (the bridge)
        var bridge = registrars.FirstOrDefault(r =>
            r.DeclaringType.Name == "SampleScoutRegistrar");
        bridge.Should().NotBeNull(
            "ScanForRegistrars must discover the SampleScoutRegistrar bridge class");

        // Bridge method is Register (not RegisterAll)
        bridge!.RegisterMethod.Name.Should().Be("Register",
            "the bridge method is named Register per the coordinator-injectable signature");

        // Parameters: BehaviorRegistry, BlueprintRegistryStaging, ActionRegistry (in that order)
        bridge.Parameters.Should().HaveCount(3,
            "bridge Register(BehaviorRegistry, BlueprintRegistryStaging, ActionRegistry<BrainBlackboard,BTreeContext>) has 3 params");
        bridge.Parameters[0].ParameterType.Should().Be(typeof(BehaviorRegistry),
            "first param must be BehaviorRegistry (injectable by coordinator)");
        bridge.Parameters[1].ParameterType.Should().Be(typeof(BlueprintRegistryStaging),
            "second param must be BlueprintRegistryStaging (injectable by coordinator)");
        bridge.Parameters[2].ParameterType.Should().Be(typeof(ActionRegistry<BrainBlackboard, BTreeContext>),
            "third param must be the BTree action registry (injected, populated from [FbtRegistrar])");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    [Fact]
    public void BTree_SampleScout_Bridge_Register_RegistersDefinitionInBehaviorRegistry()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_SampleScout_Bridge_Register_RegistersDefinitionInBehaviorRegistry_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_SampleScout_Bridge_Register_RegistersDefinitionInBehaviorRegistry_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadBTree("SampleScout");
        var dto    = ToDtoWithTypeNames(model, "SampleScout");

        // Verify the DTO has nodes before generating source
        dto.Nodes.Count.Should().BeGreaterThan(0,
            $"DTO must have nodes for SampleScout (got {dto.Nodes.Count}; BB='{dto.BlackboardTypeName}', Ctx='{dto.ContextTypeName}')");

        string json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateBTreeSources(json, "SampleScout");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleScoutRegDefTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);
        var bridge = registrars.First(r => r.DeclaringType.Name == "SampleScoutRegistrar");

        // Act: manually invoke the bridge's Register into a staging BehaviorRegistry
        var stagingRegistry = new BehaviorRegistry();
        var bpStaging       = _blueprintRegistry.BeginStaging();

        // Invoke via reflection (mirrors InvokeRegistrar internals)
        var args = bridge.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry) ? (object)stagingRegistry
                       : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>) ? new ActionRegistry<BrainBlackboard, BTreeContext>()
                       : bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        // Assert: "SampleScout" is registered in the staging registry
        stagingRegistry.TryGetId("SampleScout", out int id)
            .Should().BeTrue("beh.Register must have been called with name='SampleScout'");
        stagingRegistry.TryGetDefinition(id, out var def)
            .Should().BeTrue("definition must be retrievable by its stable ID");
        def!.BrainTier.Should().Be(BehaviorConstants.BrainTierBTree,
            "BTree asset must have BrainTier=BrainTierBTree");
        def.BTreeInterpreter.Should().NotBeNull(
            "BTreeInterpreter must be non-null for a BTree definition");
        def.Name.Should().Be("SampleScout",
            "definition name must match the asset name");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    [Fact]
    public void BTree_SampleScout_Bridge_Register_TreeIsTickable()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_SampleScout_Bridge_Register_TreeIsTickable_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_SampleScout_Bridge_Register_TreeIsTickable_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadBTree("SampleScout");
        var dto    = ToDtoWithTypeNames(model, "SampleScout");
        string json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateBTreeSources(json, "SampleScout");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleScoutTickTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);
        var bridge     = registrars.First(r => r.DeclaringType.Name == "SampleScoutRegistrar");

        var stagingRegistry = new BehaviorRegistry();
        var bpStaging       = _blueprintRegistry.BeginStaging();
        var args = bridge.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry) ? (object)stagingRegistry
                       : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>) ? new ActionRegistry<BrainBlackboard, BTreeContext>()
                       : bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        // Assert: the interpreter is tickable
        stagingRegistry.TryGetId("SampleScout", out int id).Should().BeTrue();
        stagingRegistry.TryGetDefinition(id, out var def).Should().BeTrue();

        var interpreter = def!.BTreeInterpreter!;
        var bb    = default(BrainBlackboard);
        var state = new BehaviorTreeState();
        var ctx   = default(BTreeContext);

        // Tick must NOT throw (SampleScout uses only Wait nodes — no action lookups)
        NodeStatus status = default;
        var act = () => { status = interpreter.Tick(ref bb, ref state, ref ctx); };
        act.Should().NotThrow("ticking a Wait-only tree must not throw");

        // Status must be Running or Success (Wait returns Running until timer expires)
        var validStatuses = new[] { NodeStatus.Running, NodeStatus.Success };
        status.Should().BeOneOf(validStatuses,
            "SampleScout Wait tree returns Running or Success on first tick");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PU-203 HSM: bridge discovered, registered, definition populated
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Hsm_SampleGuard_Bridge_IsDiscoveredByScanForRegistrars()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        Hsm_SampleGuard_Bridge_IsDiscoveredByScanForRegistrars_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Hsm_SampleGuard_Bridge_IsDiscoveredByScanForRegistrars_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadHsm("SampleGuard");
        var dto    = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);
        var srcs = GenerateHsmSources(json, "SampleGuard");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleGuardBridgeTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);

        var bridge = registrars.FirstOrDefault(r =>
            r.DeclaringType.Name == "SampleGuardRegistrar");
        bridge.Should().NotBeNull(
            "ScanForRegistrars must discover the SampleGuardRegistrar bridge class");
        bridge!.RegisterMethod.Name.Should().Be("Register");
        bridge.Parameters.Should().HaveCount(2);
        bridge.Parameters[0].ParameterType.Should().Be(typeof(BehaviorRegistry));
        bridge.Parameters[1].ParameterType.Should().Be(typeof(BlueprintRegistryStaging));

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    [Fact]
    public void Hsm_SampleGuard_Bridge_Register_RegistersHsmDefinition()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        Hsm_SampleGuard_Bridge_Register_RegistersHsmDefinition_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Hsm_SampleGuard_Bridge_Register_RegistersHsmDefinition_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadHsm("SampleGuard");
        var dto    = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);
        var srcs = GenerateHsmSources(json, "SampleGuard");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleGuardRegDefTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);
        var bridge     = registrars.First(r => r.DeclaringType.Name == "SampleGuardRegistrar");

        var stagingRegistry = new BehaviorRegistry();
        var bpStaging       = _blueprintRegistry.BeginStaging();
        var args = bridge.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry) ? (object)stagingRegistry
                       : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>) ? new ActionRegistry<BrainBlackboard, BTreeContext>()
                       : bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        // Assert: "SampleGuard" registered as HSM
        stagingRegistry.TryGetId("SampleGuard", out int id)
            .Should().BeTrue("beh.Register must have been called with name='SampleGuard'");
        stagingRegistry.TryGetDefinition(id, out var def)
            .Should().BeTrue("HSM definition must be retrievable");
        def!.BrainTier.Should().Be(BehaviorConstants.BrainTierHsm,
            "HSM asset must have BrainTier=BrainTierHsm");
        def.HsmDefinition.Should().NotBeNull(
            "HsmDefinition must be non-null for an HSM definition");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PU-203 Negative: bridge carries only [BlueprintRegistrar], no forbidden attrs/params
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BTree_Bridge_DoesNotCarry_FbtRegistrar_Or_HsmActionRegistrar()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_Bridge_DoesNotCarry_FbtRegistrar_Or_HsmActionRegistrar_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_Bridge_DoesNotCarry_FbtRegistrar_Or_HsmActionRegistrar_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadBTree("SampleScout");
        var dto    = ToDtoWithTypeNames(model, "SampleScout");
        string json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateBTreeSources(json, "SampleScout");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleScoutNegAttrTest");

        // The bridge class
        var bridgeType = asm.GetTypes().First(t => t.Name == "SampleScoutRegistrar");

        // Must carry [BlueprintRegistrar]
        bridgeType.GetCustomAttribute<BlueprintRegistrarAttribute>().Should().NotBeNull(
            "bridge must carry [BlueprintRegistrar]");

        // Must NOT carry [FbtRegistrar] or [HsmActionRegistrar]
        // (these have no static type here, so check by name)
        var allAttrs = bridgeType.GetCustomAttributes(inherit: false)
            .Select(a => a.GetType().Name)
            .ToList();
        allAttrs.Should().NotContain("FbtRegistrarAttribute",
            "bridge must NOT carry [FbtRegistrar] (§14 item 4)");
        allAttrs.Should().NotContain("HsmActionRegistrarAttribute",
            "bridge must NOT carry [HsmActionRegistrar] (§14 item 4)");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    [Fact]
    public void BTree_Bridge_DoesNotRequestForbiddenParams_BlueprintRegistry_Or_HsmActionDispatcher()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_Bridge_DoesNotRequestForbiddenParams_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_Bridge_DoesNotRequestForbiddenParams_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadBTree("SampleScout");
        var dto    = ToDtoWithTypeNames(model, "SampleScout");
        string json = BTreeJsonServices.Serialize(dto);
        var srcs = GenerateBTreeSources(json, "SampleScout");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleScoutNegParamTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);
        var bridge = registrars.First(r => r.DeclaringType.Name == "SampleScoutRegistrar");

        var paramTypes = bridge.Parameters.Select(p => p.ParameterType).ToList();

        // Verify that BlueprintRegistry is NOT a parameter (coordinator throws on it)
        paramTypes.Should().NotContain(typeof(BlueprintRegistry),
            "bridge must NOT request BlueprintRegistry (coordinator throws — §14 item 4)");

        // Verify that HsmActionDispatcher is NOT a parameter
        // (it's a static class; coordinator throws — §14 item 4)
        paramTypes.Should().NotContain(typeof(HsmActionDispatcher),
            "bridge must NOT request HsmActionDispatcher (static class — §14 item 4)");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    [Fact]
    public void Hsm_Bridge_DoesNotRequestForbiddenParams()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        Hsm_Bridge_DoesNotRequestForbiddenParams_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Hsm_Bridge_DoesNotRequestForbiddenParams_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var model  = LoadHsm("SampleGuard");
        var dto    = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);
        var srcs = GenerateHsmSources(json, "SampleGuard");
        var (asm, alc) = CompileMultiAndLoad(srcs, "SampleGuardNegParamTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);
        var bridge = registrars.First(r => r.DeclaringType.Name == "SampleGuardRegistrar");

        var paramTypes = bridge.Parameters.Select(p => p.ParameterType).ToList();
        paramTypes.Should().NotContain(typeof(BlueprintRegistry));
        paramTypes.Should().NotContain(typeof(HsmActionDispatcher),
            "HSM bridge calls HsmActionDispatcher STATICALLY, not via injection");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PU-203 Bridge shape: emitted as separate class, topology core unaffected
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BTree_Bridge_IsAdditiveClass_TopologyCoreClassUnchanged()
    {
        // Verify the bridge is a SEPARATE class from the topology-core class (§14 item 3).
        var model  = LoadBTree("SampleScout");
        var dto    = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);

        var text   = new StringAdditionalText("/p/SampleScout.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        var compilation = CSharpCompilation.Create(
            "Gen",
            Array.Empty<SyntaxTree>(),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        var coreSource   = result.GeneratedTrees.First(t => !t.FilePath.Contains("Registrar")).ToString();
        var bridgeSource = result.GeneratedTrees.First(t =>  t.FilePath.Contains("Registrar")).ToString();

        // Core source must NOT contain [BlueprintRegistrar] — that's the bridge's job
        coreSource.Should().NotContain("[BlueprintRegistrar]",
            "topology-core file must not contain [BlueprintRegistrar] (additive separation)");
        coreSource.Should().NotContain("Register(BehaviorRegistry",
            "topology-core file must not contain the Register method");

        // Bridge source contains [BlueprintRegistrar] and Register
        bridgeSource.Should().Contain("[BlueprintRegistrar]");
        bridgeSource.Should().Contain("Register(BehaviorRegistry");

        // Bridge does NOT contain [BTreeDefinition] — that's the core's job
        bridgeSource.Should().NotContain("[BTreeDefinition(",
            "bridge must not duplicate the [BTreeDefinition] thunk");
    }

    [Fact]
    public void BTree_EmitBridge_IsDeterministic()
    {
        // Bridge emit must be deterministic (same DTO → same output, same as topology core).
        var model  = LoadBTree("SampleScout");
        var dto    = BehaviorTreeAssetMapper.ToDto(model);

        string first  = BTreeBridgeEmitCore.EmitBridge(dto);
        string second = BTreeBridgeEmitCore.EmitBridge(dto);

        first.Should().Be(second, "BTreeBridgeEmitCore.EmitBridge must be deterministic");
    }

    [Fact]
    public void Hsm_EmitBridge_IsDeterministic()
    {
        var model  = LoadHsm("SampleGuard");
        var dto    = HsmAssetMapper.ToDto(model);

        string first  = HsmBridgeEmitCore.EmitBridge(dto);
        string second = HsmBridgeEmitCore.EmitBridge(dto);

        first.Should().Be(second, "HsmBridgeEmitCore.EmitBridge must be deterministic");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ALC unload helper (DEBT-009 pattern)
    // ─────────────────────────────────────────────────────────────────────────────

    private static void AwaitAlcCollection(WeakReference<AssemblyLoadContext>[] refs)
    {
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (refs.All(w => !w.TryGetTarget(out _))) return;
            System.Threading.Thread.Sleep(50);
        }
        // Non-fatal: log rather than fail (ALC collection is best-effort in tests)
    }
}
