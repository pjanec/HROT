using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Spatial.Eqs;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// EQS request/poll slice proof (<c>docs/blueprints/EQS_RequestPoll_Slice_Design.md</c>,
/// architect <c>Architect_Question_7_EQS_Slice.md</c>): a from-scratch, blueprint-authored rebuild of
/// the C# oracle <c>HillAttackCommanderNodes.Action_RequestAreaQuery</c> (~line 188-227) as an
/// AiPrimitive BTreeAction (<c>Assets/Blueprints/HillAssault2_RequestAreaQuery.bp.json</c>).
///
/// <para>
/// This is the FIRST proof of the stateless <c>Return(Running)</c> poll pattern (architect Q#7-A): the
/// BTree host re-ticks an AiPrimitive Action from the top each frame while it returns Running, with all
/// poll state living in WorkingState (no <c>__phase</c> field). It is also the first blueprint to use a
/// curated IMPURE (EXEC) <c>FunctionCall</c> node (<see cref="AreaQueryBatchOps.Request"/>) whose
/// non-void Return value is consumed from a LATER scheduler block (after the batch-full Branch) --
/// this exposed and fixed a real compiler bug (see below).
/// </para>
///
/// <para>
/// <b>Compiler fix (Hrot.Blueprints.Compiler, Stage5_Schedule.cs):</b> an impure (<c>IsPure=false</c>)
/// <c>FunctionCallNode</c> with an empty <c>TargetGraphId</c> (i.e. an ordinary curated CLR helper call,
/// not a call into another Library-dispatch blueprint) was being lowered via <c>IrOp_LibraryCall(0, ...)</c>,
/// which resolves to a nonexistent <c>__LibBp_00000000_Bp</c> class (CS0103) -- <c>IrOp_LibraryCall</c>'s
/// actual purpose is calling into another blueprint by a real <c>LibraryBlueprintId</c>, not invoking a
/// plain CLR static method. Fixed to lower like the pure-FunctionCall case (<c>IrOp_PureCall</c> ->
/// <c>global::{TargetTypeId}.{MethodName}(...)</c>), scheduled eagerly as a statement instead of resolved
/// lazily. A second, related bug: the per-block pin-value cache (<c>_pinValueCache</c>, cleared at the
/// start of every scheduler block -- correct for pure/recomputable reads) was the ONLY cache consulted,
/// so a value produced by an impure exec statement (materialized exactly once as a real C# local) fell
/// back to a bogus <c>default</c> literal (CS8716) when referenced from a later block. Fixed by adding a
/// cross-block <c>_statementPinCache</c> for statement-produced (non-recomputable) values, consulted
/// before the per-block cache / value-resolution switch. Neither fix touches the C# oracle.
/// </para>
///
/// <para>
/// Graph: <c>EventEntry -&gt; Compare(GetVariable(CachedEqsRequestId), Literal Int64 -1L, NotEqual) -&gt;
/// Branch1</c>. Branch1.True (request in flight): <c>FunctionCall AreaQueryBatchOps.IsReady(...)</c>
/// [IsPure, TrailingContext=View] -&gt; Branch2: True -&gt; <c>Return(Success)</c>; False -&gt;
/// <c>Return(Running)</c>. Branch1.False (no request in flight): <c>FunctionCall
/// WorldOps.IsAlive(GetParameter(TargetAreaEntity))</c> [IsPure, TrailingContext=View] -&gt; Branch3:
/// False -&gt; <c>Return(Failure)</c>; True -&gt; <c>FunctionCall AreaQueryBatchOps.Request(...)</c>
/// [IsPure=false/EXEC, TrailingContext=SelfAndView] -&gt; id:Int64 -&gt; Compare(id, -1L, Equal) -&gt;
/// Branch4: True (batch full) -&gt; <c>Return(Running)</c>; False -&gt;
/// <c>SetVariable(CachedEqsRequestId&lt;-id)</c> -&gt; <c>FunctionCall WorldOps.SimTime()</c> [IsPure,
/// TrailingContext=View] -&gt; <c>SetVariable(EqsRequestTime&lt;-now)</c> -&gt; <c>Return(Success)</c>.
/// </para>
///
/// <para>
/// Compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build. Does
/// not modify the C# oracle.
/// </para>
/// </summary>
public sealed class HillAssault2_RequestAreaQuery_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2RequestAreaQuery_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_RequestAreaQuery.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2RequestAreaQuery_*_Bp class");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2RequestAreaQuery_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_RequestAreaQuery must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection, threading Params/WorkingState across ticks.</summary>
    private static Fbt.NodeStatus TickOnce(
        MethodInfo tickCore, object paramsInstance, ref object workingStateInstance,
        Entity self, EntityRepository world)
    {
        object?[] args = { paramsInstance, workingStateInstance, self, world, 0f };
        var result = tickCore.Invoke(null, args);
        workingStateInstance = args[1]!;   // WorkingState is a ref parameter -- Invoke writes the mutated struct back.
        return (Fbt.NodeStatus)result!;
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.SetSingleton(new AreaQueryBatchData
        {
            Results = new NativeArray<AreaQueryResult>(AreaQueryBatchData.DefaultCapacity, Allocator.Persistent),
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

    // ── Source-inspection: EXEC Request + pure IsReady/SimTime + Running poll ────────────────

    [Fact]
    public void GeneratedTickCore_SourceContainsRequestIsReadySimTimeAndRunningPoll()
    {
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("AreaQueryBatchOps.Request(",
            "the fresh-submit path must call the curated EXEC AreaQueryBatchOps.Request helper -- see below:\n" + source);
        source.Should().Contain("AreaQueryBatchOps.IsReady(",
            "the in-flight poll path must call the curated pure AreaQueryBatchOps.IsReady helper -- see below:\n" + source);
        source.Should().Contain("WorldOps.SimTime(",
            "the submitted-request tail must timestamp via the curated pure WorldOps.SimTime helper -- see below:\n" + source);
        source.Should().Contain("global::Fbt.NodeStatus.Running",
            "both the in-flight-poll and batch-full paths must return NodeStatus.Running -- see below:\n" + source);
        source.Should().Contain("p.TargetAreaEntity",
            "TargetAreaEntity must be read via GetParameter (p.TargetAreaEntity) -- see below:\n" + source);
    }

    // ── Behavioral: tri-state poll across ticks ───────────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_FreshRequest_AliveArea_FreeSlot_ReturnsSuccess_AndCachesRequest()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();
            world.SetSimulationTime(12.5f);

            var p = Activator.CreateInstance(paramsType)!;
            paramsType.GetField("TargetAreaEntity")!.SetValue(p, area);

            object ws = Activator.CreateInstance(wsType)!;
            wsType.GetField("CachedEqsRequestId")!.SetValue(ws, -1L);
            wsType.GetField("EqsRequestTime")!.SetValue(ws, 0f);

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Success,
                "a fresh submit with an alive TargetAreaEntity and a free batch slot must succeed");

            var cachedId = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;
            cachedId.Should().BeGreaterThanOrEqualTo(0, "the submitted request's slot id must be cached");

            var reqTime = (float)wsType.GetField("EqsRequestTime")!.GetValue(ws)!;
            reqTime.Should().Be(12.5f, "EqsRequestTime must be stamped from WorldOps.SimTime at submission");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    [Fact]
    public void GeneratedTickCore_InFlightRequest_NotReady_ReturnsRunning_ThenReady_ReturnsSuccess()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();

            var p = Activator.CreateInstance(paramsType)!;
            paramsType.GetField("TargetAreaEntity")!.SetValue(p, area);

            object ws = Activator.CreateInstance(wsType)!;
            wsType.GetField("CachedEqsRequestId")!.SetValue(ws, -1L);
            wsType.GetField("EqsRequestTime")!.SetValue(ws, 0f);

            // Tick 1: submit -> Success, caches a slot id.
            var tick1 = TickOnce(tickCore!, p, ref ws, self, world);
            tick1.Should().Be(Fbt.NodeStatus.Success, "sanity: tick 1 must submit and succeed");
            var slot = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;

            // Tick 2: same request, slot NOT yet resolved by the (fake) solver -> Running.
            var tick2 = TickOnce(tickCore!, p, ref ws, self, world);
            tick2.Should().Be(Fbt.NodeStatus.Running,
                "polling an in-flight request whose slot is not yet ready must return Running -- " +
                "this is the stateless Return(Running) poll: the BTree host re-ticks the Action from " +
                "the top each frame, and WorkingState alone (CachedEqsRequestId) carries the poll state");

            var slotAfterRunning = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;
            slotAfterRunning.Should().Be(slot, "the cached request id must be unchanged while polling");

            // Simulate the EQS solver resolving the slot.
            ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
            batch.Results[(int)slot] = new AreaQueryResult
            {
                RequestId = slot,
                IsReady = true,
                TargetCount = 3,
                TargetGroupHandle = 7,
            };

            // Tick 3: same request, now ready -> Success (re-ticked from the top, per Q#7-A).
            var tick3 = TickOnce(tickCore!, p, ref ws, self, world);
            tick3.Should().Be(Fbt.NodeStatus.Success,
                "once the polled slot resolves, re-ticking from the top must advance to Success");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    [Fact]
    public void GeneratedTickCore_NullTargetArea_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();

            var p = Activator.CreateInstance(paramsType)!;
            paramsType.GetField("TargetAreaEntity")!.SetValue(p, default(Entity));

            object ws = Activator.CreateInstance(wsType)!;
            wsType.GetField("CachedEqsRequestId")!.SetValue(ws, -1L);
            wsType.GetField("EqsRequestTime")!.SetValue(ws, 0f);

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Failure,
                "a null/dead TargetAreaEntity must fail the WorldOps.IsAlive guard and return Failure, " +
                "matching the oracle's IsNull||!IsAlive combined guard");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    [Fact]
    public void GeneratedTickCore_DeadTargetArea_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull();

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();
            world.DestroyEntity(area);

            var p = Activator.CreateInstance(paramsType)!;
            paramsType.GetField("TargetAreaEntity")!.SetValue(p, area);

            object ws = Activator.CreateInstance(wsType)!;
            wsType.GetField("CachedEqsRequestId")!.SetValue(ws, -1L);
            wsType.GetField("EqsRequestTime")!.SetValue(ws, 0f);

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Failure,
                "a destroyed TargetAreaEntity must fail the WorldOps.IsAlive guard and return Failure");
        }
        finally
        {
            DisposeWorld(world);
        }
    }
}
