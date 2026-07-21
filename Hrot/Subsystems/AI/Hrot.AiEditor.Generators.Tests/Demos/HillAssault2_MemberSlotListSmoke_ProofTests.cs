using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Wave-core W1 de-risk proof (architect <c>Architect_Question_8_Wave_Core.md</c>, Q#8-A/D): proves a
/// custom curated blittable struct (<c>Hrot.AI.Behaviors.Brains.MemberSlotList</c>) held as a Blueprint
/// <c>WorkingState</c> var **round-trips by value through a curated <c>FunctionCall</c>** in real
/// generated code — the load-bearing assumption for the entire wave core. Nothing shipped before this
/// used a struct WorkingState var (scalars only).
///
/// <para>
/// It surfaced (and this proof locks) the one compiler enabler the wave core needed:
/// <c>StaticTypeRegistry.TryResolve</c> could not resolve an arbitrary curated struct FQN (BP1500 "Pin
/// type does not resolve") — the <c>global::</c> acceptance path only guesses a 4-byte enum size. Fixed
/// by registering <c>MemberSlotList</c> in the <c>TypeTable</c> with its real size (96), exactly like
/// <c>Entity</c>/<c>EqsSensorHandle</c>/<c>FixedString</c>. No by-ref capability was needed: the shipped
/// GetVariable→FunctionCall→SetVariable by-value machinery threads the struct correctly
/// (<c>ws.Tracker = MemberSlotListOps.Add(ws.Tracker, …)</c>).
/// </para>
///
/// <para>
/// Graph: <c>EventEntry → SetVariable(Tracker) ← FunctionCall MemberSlotListOps.Add(GetVariable(Tracker),
/// Literal 777L, Literal 3, Literal 5) → Return(Success)</c>. Starting from a default (zeroed) tracker,
/// one <c>Add</c> yields <c>Count == 1</c>. Compiled by the REAL Roslyn source generator as part of
/// <c>Hrot.AI.Behaviors</c>'s own build; does not modify the C# oracle.
/// </para>
/// </summary>
public sealed class HillAssault2_MemberSlotListSmoke_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2MemberSlotListSmoke_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_MemberSlotListSmoke.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2MemberSlotListSmoke_*_Bp class -- proving a " +
            "struct WorkingState var resolves (StaticTypeRegistry) and round-trips through a FunctionCall");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");
        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2MemberSlotListSmoke_*_Bp.g.cs").FirstOrDefault()
            : null;
        file.Should().NotBeNull($"the generated .g.cs must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void GeneratedSource_DeclaresStructWorkingStateField_AndByValueRoundtrip()
    {
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("global::Hrot.AI.Behaviors.Brains.MemberSlotList Tracker",
            "the struct must be declared as a WorkingState field of its real type -- see below:\n" + source);
        source.Should().Contain("global::Hrot.AI.Behaviors.Brains.MemberSlotListOps.Add(",
            "the curated helper must be invoked on the struct value -- see below:\n" + source);
        source.Should().Contain("ws.Tracker =",
            "the mutated struct must be written back to the WorkingState var (by-value roundtrip) -- see below:\n" + source);
    }

    [Fact]
    public void GeneratedTickCore_AddsOneEntry_TrackerCountBecomesOne()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        using var world = new EntityRepository();
        var self = world.CreateEntity();

        var p  = Activator.CreateInstance(paramsType)!;
        object ws = Activator.CreateInstance(wsType)!;

        object?[] args = { p, ws, self, world, 0f };
        var status = tickCore!.Invoke(null, args);
        ws = args[1]!;

        ((Fbt.NodeStatus)status!).Should().Be(Fbt.NodeStatus.Success);

        // Read ws.Tracker.Count via nested reflection: WorkingState.Tracker is a MemberSlotList struct.
        var tracker = wsType.GetField("Tracker")!.GetValue(ws)!;
        var count = (int)tracker.GetType().GetField("Count")!.GetValue(tracker)!;
        count.Should().Be(1,
            "a single MemberSlotListOps.Add on a default tracker must yield Count == 1 -- proving the " +
            "struct WorkingState var round-tripped by value through the curated FunctionCall");
    }
}
