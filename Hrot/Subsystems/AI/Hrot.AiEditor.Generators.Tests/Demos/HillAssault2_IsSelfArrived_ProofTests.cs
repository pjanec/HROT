using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P2 proof for the Hill-attack -> Blueprints migration
/// (<c>docs/blueprints/HillAssault_Blueprint_Migration.md</c>): a from-scratch, blueprint-authored
/// AiPrimitive BTreeCondition proving the NEW visually-native <c>GetComponent</c> node end-to-end --
/// an ECS component field read (<c>Fdp.Toolkit.Navigation.NavigationStatus.Result</c> on
/// <c>self</c>) with NO CLR reflection at generation time (the Roslyn incremental generator runs as
/// a netstandard2.0 analyzer that cannot load game assemblies to inspect a real CLR type).
///
/// <para>
/// <b>P2 design:</b> <c>GetComponentNode</c> lowers in <c>Stage5_Schedule</c> to three EXISTING IR
/// ops chained together -- <c>IrOp_Self</c> -> <c>IrOp_GetComponentRO</c> -> <c>IrOp_FieldRead</c> --
/// the same sequence already used inline by <c>WaitLowering_AiPrimitive</c>'s channel-check block.
/// <c>ComponentTypeFqn</c>/<c>FieldName</c>/<c>FieldTypeFqn</c> are baked strings authored at edit
/// time (mirrors <c>GetSharedNode.SharedTypeId</c> and the P7.1 <c>FunctionCallNode.TrailingContext</c>
/// bake), so no CLR reflection is ever attempted to build them.
/// </para>
///
/// <para>
/// <b>GAP-12 stopgap:</b> blueprints have no native comparison/equality node yet, so the
/// enum-equality check that turns the GetComponent field read into a bool condition lives in the
/// tiny pure <see cref="HillAssault2NavOps.IsArrived"/> helper, called via a <c>FunctionCall</c> node
/// with <c>"TrailingContext": "None"</c> -- a CONTEXTLESS helper (no self/view trailing args at
/// all), proving the P7.1 baked-context path also handles the "no context" case with zero
/// reflection.
/// </para>
///
/// <para>
/// Mirrors <see cref="HillAssault2_HasTarget_ProofTests"/>: reflects the REAL generated
/// <c>Hrot.AI.Behaviors.Generated.HillAssault2IsSelfArrived_*_Bp</c> class out of the built
/// <c>Hrot.AI.Behaviors.dll</c> (this test project references that project) rather than driving
/// <c>BlueprintCompiler.Compile</c> in-process. <c>dotnet build
/// Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj</c> is the actual proof that the new GetComponent node
/// survives a real MSBuild/Roslyn-incremental-generator build.
/// </para>
/// </summary>
public sealed class HillAssault2_IsSelfArrived_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class
    /// (<c>Hrot.AI.Behaviors.Generated.HillAssault2IsSelfArrived_*_Bp</c>) by name pattern rather
    /// than hardcoding the BlueprintId hash baked into the class name. Finding this type at all is
    /// itself strong evidence the real MSBuild generator compiled the new GetComponent node
    /// successfully.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2IsSelfArrived_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_IsSelfArrived.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2IsSelfArrived_*_Bp class");
        return type!;
    }

    /// <summary>
    /// Walks up from the test's runtime directory to find the actual generated source file under
    /// Hrot.AI.Behaviors's own <c>obj/GeneratedFiles</c> (wired via
    /// <c>EmitCompilerGeneratedFiles</c>/<c>CompilerGeneratedFilesOutputPath</c> in its .csproj) --
    /// mirrors <see cref="HillAssault2_HasTarget_ProofTests"/>'s convention. Reading the actual
    /// emitted C# is the most direct proof of the GetComponent lowering.
    /// </summary>
    private static string ResolveGeneratedSourcePath()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidateRoot = Path.Combine(dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "obj", "GeneratedFiles");
            if (Directory.Exists(candidateRoot))
            {
                var matches = Directory.GetFiles(candidateRoot, "HillAssault2IsSelfArrived_*_Bp.g.cs",
                    SearchOption.AllDirectories);
                if (matches.Length > 0)
                    return matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(
            "Could not locate the generated HillAssault2IsSelfArrived_*_Bp.g.cs under " +
            "Hrot.AI.Behaviors/obj/GeneratedFiles by walking up from " + AppContext.BaseDirectory);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection.</summary>
    private static Fbt.NodeStatus TickOnce(Type bpType, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        // NavigationStatus carries no WorkingState field for this condition -- Params/WorkingState
        // are both empty structs (mirrors HasTarget's TickOnce, minus the WS field set).
        object?[] args =
        {
            Activator.CreateInstance(paramsType),
            Activator.CreateInstance(wsType),
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
        world.RegisterComponent<NavigationStatus>();
        return world;
    }

    [Fact]
    public void GeneratedTickCore_ReadsNavigationStatusResult_ViaGetComponentRO_NoReflection()
    {
        // Ensure the type actually built via the real generator before inspecting its source.
        FindGeneratedBlueprintType();

        // P2 proof: the emitted call reads the NavigationStatus component off `self` via
        // GetComponentRO<global::...> (baked FQN -- no CLR reflection at generation time), then
        // textually accesses `.Result`, then feeds it into the GAP-12 stopgap comparator.
        var generatedSource = File.ReadAllText(ResolveGeneratedSourcePath());
        generatedSource.Should().Contain(
            "GetComponentRO<global::Fdp.Toolkit.Navigation.NavigationStatus>",
            "GetComponentNode must lower to a reflection-free GetComponentRO<global::FQN> call -- " +
            "see generated TickCore below:\n" + generatedSource);
        generatedSource.Should().Contain(
            ".Result",
            "GetComponentNode's FieldRead must textually access the authored FieldName -- see " +
            "generated TickCore below:\n" + generatedSource);
        generatedSource.Should().Contain(
            "HillAssault2NavOps.IsArrived(",
            "the GAP-12 stopgap comparator must be called with the field-read value -- see " +
            "generated TickCore below:\n" + generatedSource);
    }

    [Fact]
    public void GeneratedTickCore_SelfArrived_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();

        var world = CreateWorld();
        var self  = world.CreateEntity();
        world.AddComponent(self, new NavigationStatus { Result = NavigationResult.Arrived });

        TickOnce(bpType, self, world).Should().Be(Fbt.NodeStatus.Success,
            "NavigationStatus.Result == Arrived must satisfy HillAssault2NavOps.IsArrived, matching " +
            "the GAP-12 stopgap comparator's semantics");

        world.Dispose();
    }

    [Fact]
    public void GeneratedTickCore_SelfInProgress_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();

        var world = CreateWorld();
        var self  = world.CreateEntity();
        world.AddComponent(self, new NavigationStatus { Result = NavigationResult.InProgress });

        TickOnce(bpType, self, world).Should().Be(Fbt.NodeStatus.Failure,
            "NavigationStatus.Result == InProgress must NOT satisfy HillAssault2NavOps.IsArrived");

        world.Dispose();
    }
}
