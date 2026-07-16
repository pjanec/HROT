using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using InMemoryRoslynCompiler = Hrot.Blueprints.Core.Compiler.Roslyn.InMemoryRoslynCompiler;
using MetadataReferenceResolver = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Slice 1 proof for the Hill-attack -> Blueprints migration
/// (<c>docs/blueprints/HillAssault_Blueprint_Migration.md</c>): a from-scratch, blueprint-authored
/// rebuild of the C# oracle <c>HillAttackTankNodes.Condition_HasTarget</c> (HillAttackTankNodes.cs
/// ~line 113), proving the P7 context-aware <c>FunctionCall</c> (self/view auto-append) end-to-end
/// against real Hill-attack logic (<c>NetworkEntityMap</c> singleton resolution + a
/// <c>TargetMemory</c> threat-score scan).
///
/// <para>
/// <b>Why this test does NOT use the normal "reflect the real generated type out of
/// Hrot.AI.Behaviors.dll" pattern</b> (contrast <see cref="HillAssault2_AbortEngagement_ProofTests"/>):
/// the committed asset lives at <c>Recipes/Blueprints/HillAssault2_HasTarget.bp.json</c>, NOT
/// <c>Assets/Blueprints/</c>, because putting it there breaks Hrot.AI.Behaviors's real build. This is
/// a confirmed, reproducible compiler gap (see the asset's own <c>EditorMetadata.Description</c> and
/// the migration report's FRICTION section): <c>Stage0_Rehydrate.ResolveMethod</c> /
/// <c>Stage5_Schedule.ResolveClrMethodForContext</c> resolve a FunctionCall's target method via
/// runtime <c>System.Reflection</c> (<c>Type.GetType</c> + an AppDomain scan) INSIDE the Roslyn
/// incremental-generator's own process, which never has <c>Hrot.AI.Behaviors.dll</c> (or any other
/// game/engine assembly, same-project or prebuilt-cross-project alike) loaded -- so the emitted call
/// silently drops the self/view context args, producing uncompilable C# (CS7036: missing required
/// parameter 'self'). This is NOT a first-build/stale-cache artifact -- reproduced across a
/// build-server-shutdown + fresh-csc-process retry.
/// </para>
/// <para>
/// This test instead drives the SAME real compiler pipeline (<see cref="BlueprintCompiler.Compile"/> --
/// the exact class/method the MSBuild generator itself calls) in-process, where
/// <see cref="HillAssault2TankOps"/> IS already loaded (this test project references
/// <c>Hrot.AI.Behaviors</c>), then Roslyn-compiles the resulting C# and loads it into a collectible
/// <see cref="AssemblyLoadContext"/> via <see cref="InMemoryRoslynCompiler"/> -- the identical
/// mechanism <c>BlueprintTestFixture.CompileAndLoad</c> uses for its own E2E tests. This is a fully
/// real compile-and-run proof (real Roslyn, real generated TickCore, real FastBTree
/// <see cref="Fbt.NodeStatus"/>); it just does not depend on the MSBuild AdditionalFiles pipeline that
/// is currently broken for this exact scenario.
/// </para>
/// </summary>
public sealed class HillAssault2_HasTarget_ProofTests
{
    private readonly ITestOutputHelper _output;
    public HillAssault2_HasTarget_ProofTests(ITestOutputHelper output) => _output = output;

    private const string BpJsonPath =
        "Hrot.AI.Behaviors/Recipes/Blueprints/HillAssault2_HasTarget.bp.json";

    private static string ResolveBpJsonPath()
    {
        // Walk up from the test's runtime directory to find the repo-relative asset -- mirrors how
        // other Demos tests locate committed assets without hardcoding an absolute machine path.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "Hrot", "Subsystems", BpJsonPath);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(
            $"Could not locate {BpJsonPath} by walking up from {AppContext.BaseDirectory}");
    }

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Release,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// Compiles the committed HillAssault2_HasTarget asset through the REAL
    /// <see cref="BlueprintCompiler"/> (the same class/method
    /// <c>BlueprintIncrementalGenerator.CompileOneAsset</c> calls), then Roslyn-compiles the
    /// resulting C# and loads it into a fresh collectible ALC. Returns the generated blueprint Type
    /// (nested Params/WorkingState + static TickCore) and the raw generated source (for asserting
    /// on the self/view-appended call site).
    /// </summary>
    private (Type BpType, string GeneratedSource, AssemblyLoadContext Alc) CompileAndLoad()
    {
        // Force Hrot.AI.Behaviors.dll to actually be loaded into this process before scanning
        // AppDomain.CurrentDomain.GetAssemblies() below -- .NET loads referenced assemblies lazily
        // on first use, and nothing else in this test touches the Brains namespace directly.
        // Without this, MetadataReferenceResolver.ForRuntimeAssemblies would miss it and the dynamic
        // Roslyn compile would fail with CS0234 ("Brains does not exist in the namespace").
        // Force Hrot.AI.Behaviors.dll to actually LOAD into this process (not just resolve at
        // compile time) before scanning AppDomain.CurrentDomain.GetAssemblies() below. A bare
        // discarded `typeof(HillAssault2TankOps)` is NOT enough -- empirically confirmed the JIT
        // treats an unused typeof() as dead code and skips loading the assembly (the AppDomain scan
        // came back with zero Hrot.AI.Behaviors entries); routing the result through actual output
        // forces the load.
        _output.WriteLine($"[Setup] Forcing load of {typeof(HillAssault2TankOps).AssemblyQualifiedName}");

        var json = File.ReadAllText(ResolveBpJsonPath());
        var asset = BlueprintJsonServices.Deserialize(json);
        asset.Should().NotBeNull("HillAssault2_HasTarget.bp.json must deserialize");

        var compiler = new BlueprintCompiler();
        var result = compiler.Compile(asset!, DefaultOptions());
        result.Diagnostics.Should().NotContain(
            d => d.Severity == DiagnosticSeverity.Error,
            "compiling the committed asset through the real BlueprintCompiler must not produce errors: "
            + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        result.Succeeded.Should().BeTrue();
        result.GeneratedSource.Should().NotBeNullOrEmpty();

        var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var roslynCompiler = new InMemoryRoslynCompiler(resolver);
        var sink = new DiagnosticSink();
        var (assembly, alc) = roslynCompiler.CompileAndLoad(
            result.GeneratedSource!, "HillAssault2_HasTarget.g.cs", "HillAssault2_HasTarget_Dynamic", sink);

        var bpType = assembly.GetTypes().Single(t =>
            t.Namespace == "Hrot.AI.Behaviors.Generated"
            && t.Name.StartsWith("HillAssault2HasTarget_", StringComparison.Ordinal)
            && t.Name.EndsWith("_Bp", StringComparison.Ordinal));

        return (bpType, result.GeneratedSource!, alc);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection.</summary>
    private static Fbt.NodeStatus TickOnce(Type bpType, uint targetNetworkId, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        var ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("TargetNetworkId")!.SetValue(ws, targetNetworkId);

        object?[] args =
        {
            Activator.CreateInstance(paramsType),
            ws,
            entity,
            world,
            0f,
        };
        var result = tickCore!.Invoke(null, args);
        return (Fbt.NodeStatus)result!;
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<TargetMemory>();
        return world;
    }

    [Fact]
    public void GeneratedTickCore_AppendsSelfAndView_NotAsVisiblePins()
    {
        var (_, generatedSource, alc) = CompileAndLoad();
        try
        {
            // P7 proof: the emitted call passes self/world (the AiPrimitive read-only-surfaced
            // ISimulationView) as EXTRA trailing arguments -- not as ordinary wired data pins.
            // Only "targetNetworkId" (read from WorkingState) is an author-visible pin.
            generatedSource.Should().Contain(
                "HillAssault2TankOps.HasTarget(__t0, self, world)",
                "P7 must auto-append self/view to the FunctionCall -- see generated TickCore below:\n"
                + generatedSource);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public void GeneratedTickCore_TargetPresentWithPositiveThreat_ReturnsSuccess()
    {
        var (bpType, _, alc) = CompileAndLoad();
        try
        {
            var world  = CreateWorld();
            var self   = world.CreateEntity();
            var target = world.CreateEntity();

            var map = new NetworkEntityMap();
            map.Register(netId: 42, entity: target);
            world.SetSingletonManaged(map);

            world.AddComponent(self, default(TargetMemory));
            ref var mem = ref world.GetComponentRW<TargetMemory>(self);
            TargetMemory.AddOrUpdateTarget(
                ref mem, entityId: (long)target.PackedValue,
                posX: 0f, posY: 0f, scoreBoost: 1.0f, tick: 1);

            TickOnce(bpType, targetNetworkId: 42, self, world).Should().Be(Fbt.NodeStatus.Success,
                "target is resolvable via NetworkEntityMap and tracked in TargetMemory with positive threat, "
                + "matching the C# oracle Condition_HasTarget");

            world.Dispose();
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public void GeneratedTickCore_TargetNotInNetworkMap_ReturnsFailure()
    {
        var (bpType, _, alc) = CompileAndLoad();
        try
        {
            var world = CreateWorld();
            var self  = world.CreateEntity();

            // NetworkEntityMap singleton present but empty -- TargetNetworkId=42 cannot resolve.
            world.SetSingletonManaged(new NetworkEntityMap());
            world.AddComponent(self, default(TargetMemory));

            TickOnce(bpType, targetNetworkId: 42, self, world).Should().Be(Fbt.NodeStatus.Failure,
                "TargetNetworkId cannot be resolved via NetworkEntityMap, matching the C# oracle's "
                + "graceful Failure when the target has not replicated yet");

            world.Dispose();
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public void GeneratedTickCore_TargetResolvedButZeroThreat_ReturnsFailure()
    {
        var (bpType, _, alc) = CompileAndLoad();
        try
        {
            var world  = CreateWorld();
            var self   = world.CreateEntity();
            var target = world.CreateEntity();

            var map = new NetworkEntityMap();
            map.Register(netId: 42, entity: target);
            world.SetSingletonManaged(map);

            // Tracked, but with a ZERO threat score -- oracle requires a STRICTLY positive score.
            world.AddComponent(self, default(TargetMemory));
            ref var mem = ref world.GetComponentRW<TargetMemory>(self);
            TargetMemory.AddOrUpdateTarget(
                ref mem, entityId: (long)target.PackedValue,
                posX: 0f, posY: 0f, scoreBoost: 0f, tick: 1);

            TickOnce(bpType, targetNetworkId: 42, self, world).Should().Be(Fbt.NodeStatus.Failure,
                "a tracked target with ThreatScore == 0 must NOT count as a live target, matching the "
                + "C# oracle's `> 0f` (strictly positive) threat check");

            world.Dispose();
        }
        finally
        {
            alc.Unload();
        }
    }
}
