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
/// the C# oracle <c>HillAttackCommanderNodes.Condition_IsAreaQueryResolved</c> (~line 237-278) as an
/// AiPrimitive BTreeAction (<c>Assets/Blueprints/HillAssault2_IsAreaQueryResolved.bp.json</c>).
///
/// <para>
/// <b>DOCUMENTED DEVIATION:</b> despite the oracle's "Condition_" name, this graph is authored
/// <c>Intent=Action</c> (<c>Hostings=[BTreeAction]</c>), NOT <c>Intent=Condition</c> -- a Condition-
/// intent blueprint's <c>bool</c> wrapper (<c>TickCore(...)==NodeStatus.Success</c>) would collapse the
/// still-waiting <c>Running</c> poll to <c>false</c>; <c>V_AiPrimitiveIntent</c> hard-errors BP1100
/// ("Return Running is forbidden") for Condition intent, confirming Running is structurally
/// incompatible with that hosting. This mirrors <c>HillAssault2_RequestAreaQuery</c>'s stateless
/// <c>Return(Running)</c> poll pattern (proven there first, per architect Q#7-A), now exercised against
/// the timeout/area-clear/targets-found tri-branch.
/// </para>
///
/// <para>
/// Graph: <c>EventEntry -&gt; Compare(GetVariable(CachedEqsRequestId), Literal Int64 -1L, Equal) -&gt;
/// BranchGuard</c>: True -&gt; <c>Return(Failure)</c>. False -&gt; <c>FunctionCall
/// AreaQueryBatchOps.IsReady(...)</c> -&gt; BranchReady: False (not ready) -&gt;
/// <c>WorldOps.SimTime() - GetVariable(EqsRequestTime)</c> via <c>BinaryOp(Subtract)</c> -&gt;
/// <c>Compare(elapsed, 5f, GreaterThan)</c> -&gt; BranchTimeout: True -&gt;
/// <c>AreaQueryBatchOps.Free(...)</c> -&gt; reset ids to -1 -&gt; <c>Return(Failure)</c>; False -&gt;
/// <c>Return(Running)</c>. BranchReady.True (ready) -&gt; <c>AreaQueryBatchOps.TargetCount(...)</c> -&gt;
/// <c>Compare(count, 0, Equal)</c> -&gt; BranchClear: True (area clear) -&gt;
/// <c>AreaQueryBatchOps.Free(...)</c> -&gt; reset ids to -1, EqsRequestTime to 0f -&gt;
/// <c>Return(Failure)</c>; False (targets found) -&gt;
/// <c>SetVariable(CachedTargetGroupHandle&lt;-AreaQueryBatchOps.TargetGroupHandle(...))</c> -&gt;
/// <c>SetVariable(EqsRequestTime&lt;-0f)</c> -&gt; <c>Return(Success)</c>. Per SC-HA011-5,
/// <c>CachedEqsRequestId</c> is intentionally NOT cleared on this Success path.
/// </para>
///
/// <para>
/// Compiled by the REAL Roslyn source generator as part of <c>Hrot.AI.Behaviors</c>'s own build (the
/// same Stage5_Schedule compiler fix that <c>HillAssault2_RequestAreaQuery</c> introduced -- impure
/// curated <c>FunctionCall</c> lowering + cross-block statement-value persistence -- is exercised again
/// here by the two <c>AreaQueryBatchOps.Free</c> exec calls). Does not modify the C# oracle.
/// </para>
/// </summary>
public sealed class HillAssault2_IsAreaQueryResolved_ProofTests
{
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("HillAssault2IsAreaQueryResolved_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "HillAssault2_IsAreaQueryResolved.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.HillAssault2IsAreaQueryResolved_*_Bp class");
        return type!;
    }

    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2IsAreaQueryResolved_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2_IsAreaQueryResolved must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    private static Fbt.NodeStatus TickOnce(
        MethodInfo tickCore, object paramsInstance, ref object workingStateInstance,
        Entity self, EntityRepository world)
    {
        object?[] args = { paramsInstance, workingStateInstance, self, world, 0f };
        var result = tickCore.Invoke(null, args);
        workingStateInstance = args[1]!;
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

    /// <summary>
    /// Submits a real request via <see cref="AreaQueryBatchOps.Request"/> so the returned slot id
    /// is guaranteed consistent with <c>AreaQueryBatchHelper.ComputeSlot</c>, then returns that slot.
    /// </summary>
    private static long SubmitRequest(EntityRepository world, Entity self, Entity area)
        => AreaQueryBatchOps.Request(area, self, world);

    private static (object Ws, Type WsType) MakeWorkingState(Type bpType, long cachedId, int cachedHandle, float reqTime)
    {
        var wsType = bpType.GetNestedType("WorkingState")!;
        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("CachedEqsRequestId")!.SetValue(ws, cachedId);
        wsType.GetField("CachedTargetGroupHandle")!.SetValue(ws, cachedHandle);
        wsType.GetField("EqsRequestTime")!.SetValue(ws, reqTime);
        return (ws, wsType);
    }

    // ── Source-inspection ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_SourceContainsPollFreeTimeoutAndRunningPoll()
    {
        FindGeneratedBlueprintType();
        var source = FindGeneratedSourceText();

        source.Should().Contain("AreaQueryBatchOps.IsReady(",
            "the resolved-check must call the curated pure AreaQueryBatchOps.IsReady helper -- see below:\n" + source);
        source.Should().Contain("AreaQueryBatchOps.TargetCount(",
            "the area-clear check must call the curated pure AreaQueryBatchOps.TargetCount helper -- see below:\n" + source);
        source.Should().Contain("AreaQueryBatchOps.TargetGroupHandle(",
            "the targets-found path must call the curated pure AreaQueryBatchOps.TargetGroupHandle helper -- see below:\n" + source);
        source.Should().Contain("AreaQueryBatchOps.Free(",
            "both the timeout and area-clear paths must call the curated EXEC AreaQueryBatchOps.Free helper -- see below:\n" + source);
        source.Should().Contain("WorldOps.SimTime(",
            "the timeout check must read elapsed time via the curated pure WorldOps.SimTime helper -- see below:\n" + source);
        source.Should().Contain("global::Fbt.NodeStatus.Running",
            "the still-waiting path must return NodeStatus.Running -- see below:\n" + source);
    }

    // ── (a) guard: no cached request -> Failure ───────────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_NoCachedRequest_ReturnsFailure()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var p = Activator.CreateInstance(bpType.GetNestedType("Params")!)!;
            var (ws0, _) = MakeWorkingState(bpType, cachedId: -1L, cachedHandle: -1, reqTime: 0f);
            object ws = ws0;

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Failure,
                "CachedEqsRequestId == -1 is the guard case; should not occur in correct topology but " +
                "must fail safely");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    // ── (b) not ready, within timeout -> Running ──────────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_NotReady_WithinTimeout_ReturnsRunning()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();
            world.SetSimulationTime(10f);
            var slot = SubmitRequest(world, self, area);   // primes the slot with IsReady=false

            var p = Activator.CreateInstance(bpType.GetNestedType("Params")!)!;
            var (ws0, _) = MakeWorkingState(bpType, cachedId: slot, cachedHandle: -1, reqTime: 10f);
            object ws = ws0;

            // Still at t=10, EqsRequestTime=10 -> elapsed 0s, well within the 5s timeout.
            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Running,
                "an unresolved slot within the 5s timeout window must return Running");

            var idAfter = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;
            idAfter.Should().Be(slot, "the cached request id must be unchanged while still polling");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    // ── (c) not ready, timeout exceeded -> Failure + ids cleared ─────────────────────────────

    [Fact]
    public void GeneratedTickCore_NotReady_TimeoutExceeded_ReturnsFailure_AndClearsCachedIds()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();
            world.SetSimulationTime(0f);
            var slot = SubmitRequest(world, self, area);   // primes the slot with IsReady=false

            var p = Activator.CreateInstance(bpType.GetNestedType("Params")!)!;
            var (ws0, _) = MakeWorkingState(bpType, cachedId: slot, cachedHandle: 42, reqTime: 0f);
            object ws = ws0;

            // Advance sim time past the 5s window (still unresolved -- no result was written).
            world.SetSimulationTime(5.1f);

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Failure,
                "an unresolved slot past the 5s timeout must fail and free the batch slot");

            var idAfter = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;
            var handleAfter = (int)wsType.GetField("CachedTargetGroupHandle")!.GetValue(ws)!;
            idAfter.Should().Be(-1L, "CachedEqsRequestId must be reset to -1 on timeout");
            handleAfter.Should().Be(-1, "CachedTargetGroupHandle must be reset to -1 on timeout");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    // ── (d) ready, area clear -> Failure + ids cleared ────────────────────────────────────────

    [Fact]
    public void GeneratedTickCore_Ready_AreaClear_ReturnsFailure_AndClearsCachedIds()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();
            var slot = SubmitRequest(world, self, area);

            ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
            batch.Results[(int)slot] = new AreaQueryResult
            {
                RequestId = slot,
                IsReady = true,
                TargetCount = 0,
                TargetGroupHandle = -1,
            };

            var p = Activator.CreateInstance(bpType.GetNestedType("Params")!)!;
            var (ws0, _) = MakeWorkingState(bpType, cachedId: slot, cachedHandle: -1, reqTime: 3f);
            object ws = ws0;

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Failure,
                "a resolved slot with TargetCount == 0 means the area is clear -- Failure breaks the Repeater");

            var idAfter = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;
            var handleAfter = (int)wsType.GetField("CachedTargetGroupHandle")!.GetValue(ws)!;
            idAfter.Should().Be(-1L, "CachedEqsRequestId must be reset to -1 when the area is clear");
            handleAfter.Should().Be(-1, "CachedTargetGroupHandle must be reset to -1 when the area is clear");
        }
        finally
        {
            DisposeWorld(world);
        }
    }

    // ── (e) ready, targets found -> Success, handle cached, id LEFT SET (SC-HA011-5) ────────

    [Fact]
    public void GeneratedTickCore_Ready_TargetsFound_ReturnsSuccess_CachesHandle_AndLeavesRequestIdSet()
    {
        var bpType = FindGeneratedBlueprintType();
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        var wsType = bpType.GetNestedType("WorkingState")!;

        var world = CreateWorld();
        try
        {
            var self = world.CreateEntity();
            var area = world.CreateEntity();
            var slot = SubmitRequest(world, self, area);

            const int expectedHandle = 17;
            ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
            batch.Results[(int)slot] = new AreaQueryResult
            {
                RequestId = slot,
                IsReady = true,
                TargetCount = 4,
                TargetGroupHandle = expectedHandle,
            };

            var p = Activator.CreateInstance(bpType.GetNestedType("Params")!)!;
            var (ws0, _) = MakeWorkingState(bpType, cachedId: slot, cachedHandle: -1, reqTime: 3f);
            object ws = ws0;

            var status = TickOnce(tickCore!, p, ref ws, self, world);

            status.Should().Be(Fbt.NodeStatus.Success,
                "a resolved slot with TargetCount > 0 means targets were found");

            var handleAfter = (int)wsType.GetField("CachedTargetGroupHandle")!.GetValue(ws)!;
            handleAfter.Should().Be(expectedHandle, "CachedTargetGroupHandle must cache the resolved TargetGroupHandle");

            var idAfter = (long)wsType.GetField("CachedEqsRequestId")!.GetValue(ws)!;
            idAfter.Should().Be(slot,
                "per SC-HA011-5, CachedEqsRequestId is intentionally NOT cleared on the targets-found " +
                "Success path -- Action_DispatchWaveWithTargets consumes it afterward");

            var reqTimeAfter = (float)wsType.GetField("EqsRequestTime")!.GetValue(ws)!;
            reqTimeAfter.Should().Be(0f, "EqsRequestTime must be reset to 0f once the request is consumed");
        }
        finally
        {
            DisposeWorld(world);
        }
    }
}
