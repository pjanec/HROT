using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Events;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P1b (Hill-attack -&gt; Blueprints migration) proof for
/// <c>docs/blueprints/DispatchAllToBaseline_Slice_Design.md</c>. Rebuilds the C# oracle
/// <c>HillAttackCommanderNodes.Action_DispatchAllToBaseline</c> as a visually-authored AiPrimitive
/// BTreeAction (<c>Assets/Blueprints/HillAssault2_DispatchAllToBaseline.bp.json</c>), exercising an
/// in-body <c>Branch</c> INSIDE a <c>FlowForEach</c> body (P1b) together with the FlowForEach
/// loop-introspection out-pins <c>CurrentIndex</c>/<c>Count</c>, a managed <c>PublishEvent</c>
/// (<c>world.Bus.PublishManaged</c>), a context-aware (<c>TrailingContext=View</c>)
/// <c>FunctionCall</c> (<see cref="WorldOps.IsAlive"/>), and the curated pure helpers
/// <see cref="SegmentMath"/>/<see cref="MoveIntentJson"/>/<see cref="MaskOps"/>.
///
/// <para>
/// Graph: init WorkingState <c>ushort BaselineReservedMask = 0</c> (Literal -&gt; SetVariable), then
/// <c>FlowForEach</c> over the commander's <c>UnitRoster</c> whose Body branches on
/// <c>WorldOps.IsAlive(CurrentItem)</c>: the True arm publishes
/// <c>AssignTacticalIntentEvent{ IntentId="MoveToLocation" }</c> (JsonParams built by the curated
/// <see cref="MoveIntentJson.Build"/> from the curated baseline-interpolation helpers
/// <see cref="SegmentMath.LerpParam"/>/<see cref="SegmentMath.Lerp"/>) and then folds the current
/// index into <c>BaselineReservedMask</c> via the curated <see cref="MaskOps.WithBitSet"/> (which
/// bundles the oracle's <c>if (i&lt;16)</c> guard); the False arm is UNWIRED -- since
/// <c>Entity((ulong)0)</c> (the unpack of a <c>packed==0</c> slot) is never alive, the single
/// <c>IsAlive</c> branch covers BOTH of the oracle's <c>continue</c> cases.
/// </para>
///
/// <para>
/// Compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build. Does
/// not modify the C# oracle.
/// </para>
/// </summary>
public sealed class HillAssault2_DispatchAllToBaseline_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2DispatchAllToBaseline_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_DispatchAllToBaseline.bp.json must compile via the real Roslyn source " +
            "generator into a Hrot.AI.Behaviors.Generated.HillAssault2DispatchAllToBaseline_*_Bp class");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2DispatchAllToBaseline_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_DispatchAllToBaseline must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection.</summary>
    private static (Fbt.NodeStatus Status, object WorkingState) TickOnce(
        Type bpType, object paramsInstance, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var wsType = bpType.GetNestedType("WorkingState")!;
        object?[] args =
        {
            paramsInstance,
            Activator.CreateInstance(wsType),
            entity,
            world,
            0f,
        };
        var result = tickCore!.Invoke(null, args);
        return ((Fbt.NodeStatus)result!, args[1]!);
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<UnitRoster>();
        return world;
    }

    private static Entity AddSubordinate(EntityRepository world, ref UnitRoster roster)
    {
        var sub = world.CreateEntity();
        UnitRoster.Add(ref roster, (long)sub.PackedValue);
        return sub;
    }

    // ── Source-inspection: managed publish + curated helpers + string literal ────────────────

    [Fact]
    public void GeneratedTickCore_SourceContainsManagedPublishAndCuratedHelperCalls()
    {
        // Ensure the type actually built via the real generator before inspecting its source.
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("PublishManaged",
            "AssignTacticalIntentEvent is a Managed catalog entry -- PublishEvent must lower to " +
            "world.Bus.PublishManaged, not world.Bus.Publish -- see generated TickCore below:\n" + source);
        source.Should().Contain("SegmentMath.LerpParam(",
            "the loop-position interpolation parameter must be computed via the curated " +
            "SegmentMath.LerpParam helper -- see below:\n" + source);
        source.Should().Contain("SegmentMath.Lerp(",
            "the per-axis baseline position must be computed via the curated SegmentMath.Lerp " +
            "helper -- see below:\n" + source);
        source.Should().Contain("MoveIntentJson.Build(",
            "the MoveToLocationParams JSON payload must be built via the curated MoveIntentJson.Build " +
            "helper -- see below:\n" + source);
        source.Should().Contain("MaskOps.WithBitSet(",
            "the BaselineReservedMask accumulation (incl. the oracle's i<16 guard) must go through the " +
            "curated MaskOps.WithBitSet helper -- see below:\n" + source);
        source.Should().Contain("WorldOps.IsAlive(",
            "the in-body Branch's condition must be the context-aware WorldOps.IsAlive FunctionCall -- " +
            "see below:\n" + source);
        source.Should().Contain("\"MoveToLocation\"",
            "the IntentId Literal must bake the exact string \"MoveToLocation\" -- see below:\n" + source);

        // P1b core evidence: the in-body Branch emits as a nested inline `if` (IrOp_If) INSIDE the
        // `for` loop emitted by FlowForEach (IrOp_ForEach), not a BFS block split.
        int forIdx = source.IndexOf("for (", StringComparison.Ordinal);
        int ifIdx = source.IndexOf("if (", StringComparison.Ordinal);
        forIdx.Should().BeGreaterThan(-1, "FlowForEach must lower to an inline C# for loop (P1a).");
        ifIdx.Should().BeGreaterThan(forIdx,
            "the in-body Branch must lower to a nested inline `if` AFTER the `for (` header -- i.e. " +
            "nested in the loop body (P1b), not hoisted into a separate block -- see below:\n" + source);
    }

    // ── Behavioral parity vs the oracle's dispatch-to-baseline behavior ──────────────────────

    [Fact]
    public void GeneratedTickCore_ThreeAliveSubordinates_PublishesMoveIntentsAndSetsReservedMask()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        var roster = new UnitRoster();
        var sub0 = AddSubordinate(world, ref roster);
        var sub1 = AddSubordinate(world, ref roster);
        var sub2 = AddSubordinate(world, ref roster);
        world.AddComponent(commander, roster);

        var paramsType = bpType.GetNestedType("Params")!;
        var p = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("BaselineStartX")!.SetValue(p, 0f);
        paramsType.GetField("BaselineStartY")!.SetValue(p, 0f);
        paramsType.GetField("BaselineEndX")!.SetValue(p, 30f);
        paramsType.GetField("BaselineEndY")!.SetValue(p, 0f);

        var (status, ws) = TickOnce(bpType, p, commander, world);

        status.Should().Be(Fbt.NodeStatus.Success,
            "Action_DispatchAllToBaseline unconditionally returns Success once the roster is dispatched");

        world.Bus.SwapBuffers();
        var events = world.Bus.ReadManaged<AssignTacticalIntentEvent>();

        events.Count.Should().Be(3, "one AssignTacticalIntentEvent must be published per alive subordinate");

        var expectedEntities = new[] { sub0, sub1, sub2 };
        for (int i = 0; i < 3; i++)
        {
            events[i].IntentId.Should().Be("MoveToLocation",
                "every dispatched intent must carry IntentId=\"MoveToLocation\", matching the oracle");
            events[i].Entity.Should().Be(expectedEntities[i],
                "PublishEvent's Target pin is wired from FlowForEach.CurrentItem -- each event must " +
                "target the corresponding subordinate");
            events[i].JsonParams.Should().NotBeNullOrEmpty(
                "JsonParams must carry the MoveIntentJson.Build-serialized MoveToLocationParams payload");
        }

        // Baseline spans X=[0,30], Y=0 across 3 tanks: t = i/(count-1) = {0, 0.5, 1} -> X = {0, 15, 30}.
        events[0].JsonParams.Should().Contain("\"X\":0", "sub0 (i=0) sits at the baseline start (t=0 -> X=0)");
        events[2].JsonParams.Should().Contain("\"X\":30", "sub2 (i=2) sits at the baseline end (t=1 -> X=30)");

        var wsType = bpType.GetNestedType("WorkingState")!;
        var mask = (ushort)wsType.GetField("BaselineReservedMask")!.GetValue(ws)!;
        mask.Should().Be((ushort)0b111,
            "all 3 slots (i=0,1,2) must be folded into BaselineReservedMask via MaskOps.WithBitSet");
    }

    [Fact]
    public void GeneratedTickCore_EmptyRoster_ReturnsSuccess_PublishesNothing()
    {
        var bpType = FindGeneratedBlueprintType();

        using var world = CreateWorld();
        var commander = world.CreateEntity();
        world.AddComponent(commander, new UnitRoster()); // Count == 0

        var paramsType = bpType.GetNestedType("Params")!;
        var p = Activator.CreateInstance(paramsType)!;

        var (status, ws) = TickOnce(bpType, p, commander, world);

        status.Should().Be(Fbt.NodeStatus.Success,
            "an empty roster skips the loop body entirely, matching the oracle's vacuous-success path");

        world.Bus.SwapBuffers();
        var events = world.Bus.ReadManaged<AssignTacticalIntentEvent>();
        events.Count.Should().Be(0, "an empty roster must publish no AssignTacticalIntentEvent");

        var wsType = bpType.GetNestedType("WorkingState")!;
        var mask = (ushort)wsType.GetField("BaselineReservedMask")!.GetValue(ws)!;
        mask.Should().Be((ushort)0, "no slot is ever folded into BaselineReservedMask for an empty roster");
    }
}
