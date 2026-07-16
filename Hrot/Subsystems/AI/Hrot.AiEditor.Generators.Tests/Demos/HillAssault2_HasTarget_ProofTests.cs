using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Slice 1 proof for the Hill-attack -> Blueprints migration
/// (<c>docs/blueprints/HillAssault_Blueprint_Migration.md</c>): a from-scratch, blueprint-authored
/// rebuild of the C# oracle <c>HillAttackTankNodes.Condition_HasTarget</c> (HillAttackTankNodes.cs
/// ~line 113), proving the P7.1 context-aware <c>FunctionCall</c> (self/view auto-append, baked at
/// author time -- no CLR reflection at generation time) end-to-end against real Hill-attack logic
/// (<c>NetworkEntityMap</c> singleton resolution + a <c>TargetMemory</c> threat-score scan).
///
/// <para>
/// <b>P7.1 update:</b> the committed asset now lives at the normal
/// <c>Assets/Blueprints/HillAssault2_HasTarget.bp.json</c> location (previously it had to live under
/// <c>Recipes/Blueprints/</c>, deliberately NOT wired into Hrot.AI.Behaviors's real
/// AdditionalFiles/Roslyn-incremental-generator build -- see GAP-9). The root cause was that
/// <c>Stage0_Rehydrate.ResolveMethod</c> / <c>Stage5_Schedule.ResolveClrMethodForContext</c> resolved
/// a FunctionCall's target method via runtime <c>System.Reflection</c> (<c>Type.GetType</c> + an
/// <c>AppDomain</c> scan) INSIDE the Roslyn incremental-generator's own netstandard2.0 analyzer
/// process, which never has <c>Hrot.AI.Behaviors.dll</c> (or any other game assembly) loaded -- so
/// the emitted call silently dropped the self/view context args, producing uncompilable C#
/// (CS7036: missing required parameter 'self'). P7.1 fixes this by baking the trailing-context
/// decision directly into the FunctionCall node's JSON at author time (<c>"TrailingContext":
/// "SelfAndView"</c>, with a <c>Pins</c> array that already excludes self/view) so
/// <c>Stage5_Schedule</c> honors the baked flag with NO reflection at generation time.
/// </para>
///
/// <para>
/// This test now mirrors <see cref="HillAssault2_AbortEngagement_ProofTests"/>: it reflects the REAL
/// generated <c>Hrot.AI.Behaviors.Generated.HillAssault2HasTarget_*_Bp</c> class out of the built
/// <c>Hrot.AI.Behaviors.dll</c> (this test project references that project -- see its .csproj) rather
/// than driving <c>BlueprintCompiler.Compile</c> in-process. <c>dotnet build
/// Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj</c> is the actual proof that P7.1 fixed the real-build
/// gap: it must now emit a compiling <c>HillAssault2HasTarget_*_Bp</c> whose <c>TickCore</c> calls
/// <c>HillAssault2TankOps.HasTarget(__t0, self, world)</c>.
/// </para>
/// </summary>
public sealed class HillAssault2_HasTarget_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class (<c>Hrot.AI.Behaviors.Generated.HillAssault2HasTarget_*_Bp</c>)
    /// by name pattern rather than hardcoding the BlueprintId hash baked into the class name. Finding
    /// this type at all is itself strong evidence the real MSBuild generator compiled successfully --
    /// prior to P7.1, placing this asset under Assets/Blueprints/ made Hrot.AI.Behaviors fail to
    /// build entirely (CS7036), so this type (and this whole test project, which references it)
    /// would not have been buildable.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2HasTarget_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_HasTarget.bp.json must compile via the real Roslyn source generator into a " +
            "Hrot.AI.Behaviors.Generated.HillAssault2HasTarget_*_Bp class");
        return type!;
    }

    /// <summary>
    /// Walks up from the test's runtime directory to find the actual generated source file under
    /// Hrot.AI.Behaviors's own <c>obj/GeneratedFiles</c> (wired via
    /// <c>EmitCompilerGeneratedFiles</c>/<c>CompilerGeneratedFilesOutputPath</c> in its .csproj) --
    /// mirrors <c>HillAssault2_HasTarget_ProofTests</c>'s prior <c>ResolveBpJsonPath</c> convention.
    /// Reading the actual emitted C# is the most direct proof of the P7.1 self/view append.
    /// </summary>
    private static string ResolveGeneratedSourcePath()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidateRoot = Path.Combine(dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "obj", "GeneratedFiles");
            if (Directory.Exists(candidateRoot))
            {
                var matches = Directory.GetFiles(candidateRoot, "HillAssault2HasTarget_*_Bp.g.cs",
                    SearchOption.AllDirectories);
                if (matches.Length > 0)
                    return matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(
            "Could not locate the generated HillAssault2HasTarget_*_Bp.g.cs under " +
            "Hrot.AI.Behaviors/obj/GeneratedFiles by walking up from " + AppContext.BaseDirectory);
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
        // Ensure the type actually built via the real generator before inspecting its source.
        FindGeneratedBlueprintType();

        // P7.1 proof: the emitted call passes self/world (the AiPrimitive read-only-surfaced
        // ISimulationView) as EXTRA trailing arguments -- baked via the FunctionCall node's
        // "TrailingContext": "SelfAndView" field, with NO reflection at generation time. Only
        // "targetNetworkId" (read from WorkingState) is an author-visible pin.
        var generatedSource = File.ReadAllText(ResolveGeneratedSourcePath());
        generatedSource.Should().Contain(
            "HillAssault2TankOps.HasTarget(__t0, self, world)",
            "P7.1 must auto-append self/view to the FunctionCall via the baked TrailingContext flag " +
            "-- see generated TickCore below:\n" + generatedSource);
    }

    [Fact]
    public void GeneratedTickCore_TargetPresentWithPositiveThreat_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();

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

    [Fact]
    public void GeneratedTickCore_TargetNotInNetworkMap_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();

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

    [Fact]
    public void GeneratedTickCore_TargetResolvedButZeroThreat_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();

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
}
