using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P1b proof (<c>docs/blueprints/CalculateSegments_Slice_Design.md</c>): the committed blueprint
/// <c>Assets/Blueprints/HillAssault2_CalculateSegments.bp.json</c> (AiPrimitive, Intent=Action,
/// Hostings=[BTreeAction]) is a from-scratch, blueprint-authored rebuild of the C# oracle
/// <c>HillAttackCommanderNodes.Action_CalculateSegments</c> (~line 49), using only shipped nodes
/// (<c>GetParameter</c>, <c>SetVariable</c>, <c>Literal</c>, <c>Return</c>) plus one small curated
/// helper (<see cref="SegmentMath.TotalSlots"/>) for the distance/clamp/spacing-default math -- there
/// is no visual node for that arithmetic. Graph: <c>EventEntry</c> -&gt; <c>SetVariable</c>(TotalSlots)
/// [Value wired from the pure <c>FunctionCall SegmentMath.TotalSlots</c>, whose five In pins are fed by
/// five <c>GetParameter</c> reads of StartX/StartY/EndX/EndY/TankSpacing] -&gt; eight more
/// <c>SetVariable</c> nodes (each fed by a <c>Literal</c>) zeroing/initializing the remaining
/// WorkingState fields -&gt; <c>Return(Success)</c>.
///
/// <para>
/// It is compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build
/// (<c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2CalculateSegments_*_Bp.g.cs</c>).
/// Mirrors <c>HillAssault2_ReverseToBaseline_ProofTests</c>'s reflection-based invocation style, driving
/// the generated <c>TickCore</c> directly (bypassing the BTree/Blackboard1024 rail).
/// </para>
/// </summary>
public sealed class HillAssault2_CalculateSegments_ProofTests
{
    /// <summary>
    /// Locates the real generated blueprint class
    /// (<c>Hrot.AI.Behaviors.Generated.HillAssault2CalculateSegments_*_Bp</c>) by name pattern rather
    /// than hardcoding the BlueprintId hash baked into the class name.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2CalculateSegments_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_CalculateSegments.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2CalculateSegments_*_Bp class");
        return type!;
    }

    /// <summary>Returns the generated <c>.g.cs</c> source text for the compiled blueprint (source-inspection evidence).</summary>
    private static string FindGeneratedSourceText()
    {
        var generatedDir = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = System.IO.Directory.Exists(generatedDir)
            ? System.IO.Directory.GetFiles(generatedDir, "HillAssault2CalculateSegments_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_CalculateSegments must exist under {generatedDir}");
        return System.IO.File.ReadAllText(file!);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection, threading Params/WorkingState across ticks.</summary>
    private static Fbt.NodeStatus TickOnce(
        MethodInfo tickCore, object paramsInstance, ref object workingStateInstance, Entity entity, EntityRepository world)
    {
        object?[] args = { paramsInstance, workingStateInstance, entity, world, 0f };
        var result = tickCore.Invoke(null, args);
        workingStateInstance = args[1]!;   // WorkingState is a ref parameter -- Invoke writes the mutated struct back.
        return (Fbt.NodeStatus)result!;
    }

    [Fact]
    public void GeneratedTickCore_SourceContainsSegmentMathCallAndParameterReads()
    {
        var source = FindGeneratedSourceText();

        source.Should().Contain("SegmentMath.TotalSlots(",
            "the TotalSlots WorkingState field must be computed via the curated SegmentMath.TotalSlots " +
            "helper (no visual node expresses the distance/clamp/spacing-default math) -- see generated TickCore below:\n" + source);
        source.Should().Contain("p.StartX",
            "StartX must be read via GetParameter (p.StartX) -- see generated TickCore below:\n" + source);
        source.Should().Contain("p.TankSpacing",
            "TankSpacing must be read via GetParameter (p.TankSpacing) -- see generated TickCore below:\n" + source);
    }

    [Fact]
    public void GeneratedTickCore_ComputesTotalSlotsAndInitializesAllWorkingState_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        using var world = new EntityRepository();
        var entity = world.CreateEntity();

        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("StartX")!.SetValue(p, 0f);
        paramsType.GetField("StartY")!.SetValue(p, 0f);
        paramsType.GetField("EndX")!.SetValue(p, 100f);
        paramsType.GetField("EndY")!.SetValue(p, 0f);
        paramsType.GetField("TankSpacing")!.SetValue(p, 10f);
        object ws = Activator.CreateInstance(wsType)!;

        var status = TickOnce(tickCore!, p, ref ws, entity, world);

        status.Should().Be(Fbt.NodeStatus.Success, "Action_CalculateSegments returns Success unconditionally");

        // distance(start,end) = 100, spacing = 10 -> totalSlots = max(1, 100/10) = 10, within [1,16].
        wsType.GetField("TotalSlots")!.GetValue(ws).Should().Be(10);
        wsType.GetField("BurnedSlotsMask")!.GetValue(ws).Should().Be((ushort)0);
        wsType.GetField("WaveUsedSlotsMask")!.GetValue(ws).Should().Be((ushort)0);
        wsType.GetField("BaselineReservedMask")!.GetValue(ws).Should().Be((ushort)0);
        wsType.GetField("ActiveAttackerCount")!.GetValue(ws).Should().Be(0);
        wsType.GetField("CurrentWave")!.GetValue(ws).Should().Be((byte)0);
        wsType.GetField("CachedEqsRequestId")!.GetValue(ws).Should().Be(-1L);
        wsType.GetField("CachedTargetGroupHandle")!.GetValue(ws).Should().Be(-1);
        wsType.GetField("EqsRequestTime")!.GetValue(ws).Should().Be(0f);
    }
}
