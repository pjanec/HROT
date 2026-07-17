using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Spatial.Eqs;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// P1b (Hill-attack -&gt; Blueprints migration, wave-core slice) proof for
/// <c>docs/blueprints/WaveCore_Slice_Design.md</c>, architect <c>Architect_Question_8_Wave_Core.md</c>.
/// Rebuilds the C# oracle <c>HillAttackCommanderNodes.Action_DispatchWaveWithTargets</c> (line 287-437) as
/// a visually-authored AiPrimitive BTreeAction. See the design note for the full oracle-line-&gt;node
/// mapping.
///
/// <para>
/// <b>Compiler fix (Stage-5 emit) this slice surfaced.</b> The WorkingState field <c>CurrentWave</c> is
/// <c>System.Byte</c> (matching the oracle's <c>byte CurrentWave</c>), while the curated helpers
/// <see cref="WaveDispatchOps.ShouldConsider"/>, <see cref="SlotOps.PickRandomFreeSlot"/>, and
/// <see cref="WaveParityOps.NextWave"/> take <c>currentWave</c> as <c>System.Int32</c>. Reading
/// <c>GetVariable(CurrentWave):Byte</c> into an <c>Int32</c> argument makes
/// <c>Stage3_Normalize.InsertImplicitCasts</c> insert a byte-&gt;int <c>CastNode</c>, which
/// <c>Stage5_Schedule</c> lowered to <c>IrOp_PureCall("Cast.System.Int32", ...)</c> -&gt; an unresolvable
/// <c>global::Cast.System.Int32(...)</c> call (CS0400 x3) -- a never-exercised path (no prior slice had an
/// implicit coercion). Fixed by intercepting the synthesized <c>Cast.&lt;Type&gt;</c> op in
/// <c>StatementEmitter</c> and emitting a native C# cast <c>(global::&lt;Type&gt;)arg</c> (Stage3 only
/// inserts a CastNode when <c>ITypeRegistry.TryGetCoercion</c> succeeds, so the target is always a scalar
/// numeric/enum type). See <c>docs/blueprints/WaveCore_Slice_Design.md</c>.
/// </para>
///
/// <para>
/// Graph (once unblocked): <c>EventEntry -&gt; SetVariable(Runners &lt;- MemberSlotListOps.Empty()) -&gt;
/// SetVariable(WaveUsedSlotsMask &lt;- 0) -&gt; FlowForEach</c> over the commander's <c>UnitRoster</c>.
/// Body -&gt; Branch1(<see cref="WaveDispatchOps.ShouldConsider"/>) -&gt; True -&gt;
/// Branch2(<see cref="SlotOps.PickRandomFreeSlot"/> != -1) -&gt; True: computes firing/baseline world
/// positions and the round-robin target, publishes <c>AssignTacticalIntentEvent</c>, and folds the runner
/// into <c>Runners</c>/<c>WaveUsedSlotsMask</c>/<c>BaselineReservedMask</c>. Post-loop: resets the EQS
/// cache, frees the batch slot, and flips <c>CurrentWave</c>.
/// </para>
/// </summary>
public sealed class HillAssault2_DispatchWaveWithTargets_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2DispatchWaveWithTargets_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_DispatchWaveWithTargets.bp.json must compile via the real Roslyn source " +
            "generator into a Hrot.AI.Behaviors.Generated.HillAssault2DispatchWaveWithTargets_*_Bp class");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2DispatchWaveWithTargets_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_DispatchWaveWithTargets must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    private static (Fbt.NodeStatus Status, object WorkingState) TickOnce(
        Type bpType, object paramsInstance, object workingStateInstance, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        object?[] args = { paramsInstance, workingStateInstance, entity, world, 0f };
        var result = tickCore!.Invoke(null, args);
        return ((Fbt.NodeStatus)result!, args[1]!);
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<UnitRoster>();
        world.SetSingleton(new AreaQueryBatchData
        {
            Results = new Fdp.Core.Collections.NativeArray<AreaQueryResult>(
                AreaQueryBatchData.DefaultCapacity, Fdp.Core.Collections.Allocator.Persistent),
        });
        return world;
    }

    private static void DisposeWorld(EntityRepository world)
    {
        if (world.HasSingleton<AreaQueryBatchData>())
        {
            ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
            if (batch.Results.IsCreated) batch.Results.Dispose();
        }
        world.Dispose();
    }

    // ── Source-inspection: curated helper calls + managed publish + P1b depth-2 nesting ──────

    [Fact]
    public void GeneratedTickCore_SourceContainsCuratedHelpersAndNestedInBodyBranches()
    {
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("WaveDispatchOps.ShouldConsider(",
            "Branch1's condition must be the combined per-tank gate -- see below:\n" + source);
        source.Should().Contain("SlotOps.PickRandomFreeSlot(",
            "the deterministic seeded slot pick must replace Random.Shared -- see below:\n" + source);
        source.Should().Contain("MemberSlotListOps.AddRunner(",
            "a dispatched tank must be folded into Runners via the curated AddRunner helper -- see below:\n" + source);
        source.Should().Contain("PublishManaged",
            "AssignTacticalIntentEvent is a Managed catalog entry -- see below:\n" + source);
        source.Should().Contain("WaveParityOps.NextWave(",
            "the post-loop wave flip must go through the curated parity helper -- see below:\n" + source);

        // P1b depth-2: an in-body Branch nested INSIDE another in-body Branch, both inside the
        // FlowForEach's `for` loop -- i.e. two nested `if (` after the `for (` header.
        int forIdx = source.IndexOf("for (", StringComparison.Ordinal);
        int if1Idx = source.IndexOf("if (", StringComparison.Ordinal);
        forIdx.Should().BeGreaterThan(-1, "FlowForEach must lower to an inline C# for loop.");
        if1Idx.Should().BeGreaterThan(forIdx, "Branch1 must nest inside the for loop.");
        int if2Idx = source.IndexOf("if (", if1Idx + 1, StringComparison.Ordinal);
        if2Idx.Should().BeGreaterThan(if1Idx, "Branch2 must nest inside Branch1's arm (P1b depth 2).");
    }

    // ── Behavioral parity vs the oracle's wave-dispatch behavior ─────────────────────────────

    [Fact]
    public void GeneratedTickCore_ThreeSubordinates_DispatchesAllAndFlipsWave()
    {
        var bpType = FindGeneratedBlueprintType();
        var world = CreateWorld();
        try
        {
            var commander = world.CreateEntity();
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, (long)world.CreateEntity().PackedValue);
            UnitRoster.Add(ref roster, (long)world.CreateEntity().PackedValue);
            UnitRoster.Add(ref roster, (long)world.CreateEntity().PackedValue);
            world.AddComponent(commander, roster);   // rosterCount == 3 -> all participate regardless of parity

            var paramsType = bpType.GetNestedType("Params")!;
            var p = Activator.CreateInstance(paramsType)!;
            paramsType.GetField("StartX")!.SetValue(p, 0f);
            paramsType.GetField("StartY")!.SetValue(p, 0f);
            paramsType.GetField("EndX")!.SetValue(p, 30f);
            paramsType.GetField("EndY")!.SetValue(p, 0f);
            paramsType.GetField("BaselineStartX")!.SetValue(p, 0f);
            paramsType.GetField("BaselineStartY")!.SetValue(p, 10f);
            paramsType.GetField("BaselineEndX")!.SetValue(p, 30f);
            paramsType.GetField("BaselineEndY")!.SetValue(p, 10f);
            paramsType.GetField("AttackDirX")!.SetValue(p, 1f);
            paramsType.GetField("AttackDirY")!.SetValue(p, 0f);

            var wsType = bpType.GetNestedType("WorkingState")!;
            object ws = Activator.CreateInstance(wsType)!;
            wsType.GetField("TotalSlots")!.SetValue(ws, 4);
            wsType.GetField("CurrentWave")!.SetValue(ws, (byte)0);
            wsType.GetField("CachedTargetGroupHandle")!.SetValue(ws, -1);
            wsType.GetField("CachedEqsRequestId")!.SetValue(ws, -1L);

            var (status, wsAfter) = TickOnce(bpType, p, ws, commander, world);

            status.Should().Be(Fbt.NodeStatus.Success,
                "Action_DispatchWaveWithTargets unconditionally returns Success once the wave is dispatched");

            world.Bus.SwapBuffers();
            var events = world.Bus.ReadManaged<AssignTacticalIntentEvent>();
            events.Count.Should().Be(3,
                "rosterCount <= 3 means every subordinate participates in every wave (WaveParityOps." +
                "ShouldParticipate), and 4 free slots easily cover 3 tanks");
            foreach (var e in events)
                e.IntentId.Should().Be("HullDownAttack");

            var runners = wsType.GetField("Runners")!.GetValue(wsAfter)!;
            var runnerCount = (int)runners.GetType().GetField("Count")!.GetValue(runners)!;
            runnerCount.Should().Be(3, "all 3 dispatched tanks must be folded into Runners");

            var nextWave = (byte)wsType.GetField("CurrentWave")!.GetValue(wsAfter)!;
            nextWave.Should().Be((byte)1, "WaveParityOps.NextWave must flip 0 -> 1");

            var cachedEqs = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(wsAfter)!;
            cachedEqs.Should().Be(-1L, "the EQS request slot must be freed and reset at the end of dispatch");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    [Fact]
    public void GeneratedTickCore_EmptyRoster_ReturnsSuccess_DispatchesNothing()
    {
        var bpType = FindGeneratedBlueprintType();
        var world = CreateWorld();
        try
        {
            var commander = world.CreateEntity();
            world.AddComponent(commander, new UnitRoster());

            var paramsType = bpType.GetNestedType("Params")!;
            var p = Activator.CreateInstance(paramsType)!;

            var wsType = bpType.GetNestedType("WorkingState")!;
            object ws = Activator.CreateInstance(wsType)!;
            wsType.GetField("TotalSlots")!.SetValue(ws, 4);
            wsType.GetField("CachedTargetGroupHandle")!.SetValue(ws, -1);
            wsType.GetField("CachedEqsRequestId")!.SetValue(ws, -1L);

            var (status, wsAfter) = TickOnce(bpType, p, ws, commander, world);

            status.Should().Be(Fbt.NodeStatus.Success, "an empty roster still completes the dispatch pass");

            world.Bus.SwapBuffers();
            world.Bus.ReadManaged<AssignTacticalIntentEvent>().Count.Should().Be(0);

            var runners = wsType.GetField("Runners")!.GetValue(wsAfter)!;
            ((int)runners.GetType().GetField("Count")!.GetValue(runners)!).Should().Be(0);
        }
        finally
        {
            DisposeWorld(world);
        }
    }
}
