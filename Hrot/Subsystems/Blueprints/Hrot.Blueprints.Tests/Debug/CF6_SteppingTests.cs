using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for CF-6: Real stepping via temporary breakpoints.
/// Verifies ExecSuccessors utility, temp BP hit/auto-clear,
/// user BP suppression, Step() via temp BPs, and Continue cleanup.
/// </summary>
public sealed class CF6_SteppingTests
{
    private static readonly Guid AssetIdA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeA    = new("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid NodeB    = new("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid NodeC    = new("c0000000-0000-0000-0000-00000000000c");

    private static Entity E1 => new(1, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession(MockTimeController? tc = null)
        => new(
            new BlueprintRegistry(),
            new StubSimulationView(),
            tc ?? new MockTimeController());

    /// <summary>Simulate a node probe call.</summary>
    private static void Probe(BlueprintDebugSession session, Entity entity, string nodeIdStr)
        => ((IBlueprintProbeSink)session).OnNodeEnter(entity, nodeIdStr);

    /// <summary>Build a simple linear graph: NodeA → NodeB → NodeC.</summary>
    private static Graph BuildLinear3NodeGraph()
    {
        var pinAout = new Pin { Id = Guid.NewGuid(), Name = "Exec_out", Direction = "Out", IsExec = true };
        var pinBin  = new Pin { Id = Guid.NewGuid(), Name = "Exec_in",  Direction = "In",  IsExec = true };
        var pinBout = new Pin { Id = Guid.NewGuid(), Name = "Exec_out", Direction = "Out", IsExec = true };
        var pinCin  = new Pin { Id = Guid.NewGuid(), Name = "Exec_in",  Direction = "In",  IsExec = true };

        var nodeA = new FunctionCallNode
        {
            Id = NodeA, TargetTypeId = "TestLib", MethodName = "A",
            Pins = new List<Pin> { pinAout }
        };
        var nodeB = new FunctionCallNode
        {
            Id = NodeB, TargetTypeId = "TestLib", MethodName = "B",
            Pins = new List<Pin> { pinBin, pinBout }
        };
        var nodeC = new ReturnNode
        {
            Id = NodeC,
            Pins = new List<Pin> { pinCin }
        };

        var linkAB = new Link { FromNodeId = NodeA, FromPinId = pinAout.Id, ToNodeId = NodeB, ToPinId = pinBin.Id };
        var linkBC = new Link { FromNodeId = NodeB, FromPinId = pinBout.Id, ToNodeId = NodeC, ToPinId = pinCin.Id };

        return new Graph
        {
            Id = GraphId1,
            Name = "TestGraph",
            Kind = GraphKind.Event,
            Nodes = new List<Node> { nodeA, nodeB, nodeC },
            Links = new List<Link> { linkAB, linkBC },
        };
    }

    private sealed class StubSimulationView : ISimulationView
    {
        public uint  Tick => 0;
        public float Time => 0f;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public bool IsAlive(Entity e) => true;
        public bool HasComponent<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
        public bool HasManagedComponent<T>(Entity e) where T : class => throw new NotImplementedException();
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => throw new NotImplementedException();
        public QueryBuilder Query() => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    // ========================================================================
    // Test 1: ExecSuccessors — single successor (linear chain)
    // ========================================================================

    [Fact]
    public void ExecSuccessors_LinearChain_ReturnsSingleSuccessor()
    {
        var graph = BuildLinear3NodeGraph();

        // NodeA → NodeB
        var succA = ExecSuccessors.GetSuccessors(graph, NodeA);
        Assert.Single(succA);
        Assert.Equal(NodeB, succA[0]);

        // NodeB → NodeC
        var succB = ExecSuccessors.GetSuccessors(graph, NodeB);
        Assert.Single(succB);
        Assert.Equal(NodeC, succB[0]);
    }

    // ========================================================================
    // Test 2: ExecSuccessors — terminal node returns empty
    // ========================================================================

    [Fact]
    public void ExecSuccessors_TerminalNode_ReturnsEmpty()
    {
        var graph = BuildLinear3NodeGraph();

        // NodeC (Return) has no exec-out pin, so no successors
        var succ = ExecSuccessors.GetSuccessors(graph, NodeC);
        Assert.Empty(succ);
    }

    [Fact]
    public void ExecSuccessors_UnknownNode_ReturnsEmpty()
    {
        var graph = BuildLinear3NodeGraph();
        var succ = ExecSuccessors.GetSuccessors(graph, Guid.NewGuid());
        Assert.Empty(succ);
    }

    // ========================================================================
    // Test 3: Temp breakpoints hit and auto-clear
    // ========================================================================

    [Fact]
    public void TempBreakpoints_HitAndAutoClear()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Set a temp breakpoint on NodeA
        var target = new BreakpointTarget(AssetIdA, GraphId1, NodeA);
        session.SetTemporaryBreakpoints(new[] { target });

        Assert.True(session.HasTemporaryBreakpoints);

        // Simulate hitting the temp breakpoint's probe id
        Probe(session, E1, NodeA.ToString("D"));

        // Session should be paused
        Assert.True(session.IsPaused);
        // Temps should be auto-cleared
        Assert.False(session.HasTemporaryBreakpoints);
        // Pause was requested
        Assert.Equal(1, tc.PauseRequestCount);
    }

    // ========================================================================
    // Test 4: User BPs suppressed when temps active
    // ========================================================================

    [Fact]
    public void UserBreakpoints_SuppressedWhenTempsActive()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Set a REGULAR breakpoint on NodeA
        session.SetBreakpoint(AssetIdA, GraphId1, NodeA);

        // Set a TEMP breakpoint on NodeB
        var tempTarget = new BreakpointTarget(AssetIdA, GraphId1, NodeB);
        session.SetTemporaryBreakpoints(new[] { tempTarget });

        Assert.True(session.HasTemporaryBreakpoints);

        // Simulate hitting NodeA (regular breakpoint node) while temps active
        Probe(session, E1, NodeA.ToString("D"));

        // Regular BP must NOT cause a pause during temp suppression
        Assert.False(session.IsPaused);
        Assert.Equal(0, tc.PauseRequestCount);

        // Simulate hitting NodeB (temp breakpoint node)
        Probe(session, E1, NodeB.ToString("D"));

        // Temp BP must pause and clear
        Assert.True(session.IsPaused);
        Assert.False(session.HasTemporaryBreakpoints);
        Assert.Equal(1, tc.PauseRequestCount);

        // After temp hit + Continue, user BPs are restored
        var pausesBeforeContinue = tc.PauseRequestCount;
        session.Continue();
        Assert.False(session.HasTemporaryBreakpoints);

        // Hitting the regular BP should now cause a pause again
        Probe(session, E1, NodeA.ToString("D"));
        Assert.True(session.IsPaused);
        Assert.True(tc.PauseRequestCount > pausesBeforeContinue);
    }

    // ========================================================================
    // Test 5: Step from a node with known successors
    // ========================================================================

    [Fact]
    public void Step_FromNodeWithSuccessors_SetsTempBPsAndResumes()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Register the graph for stepping
        var graph = BuildLinear3NodeGraph();
        session.RegisterGraph(graph);

        // Set a breakpoint on NodeA and hit it to pause
        session.SetBreakpoint(AssetIdA, GraphId1, NodeA);
        Probe(session, E1, NodeA.ToString("D"));
        Assert.True(session.IsPaused);

        // Capture counts before step
        var resumeCountBefore = tc.ResumeCount;
        var stepCountBefore   = tc.StepRequestCount;

        // Now call Step()
        session.StepOver();

        // Verify temps were set (HasTemporaryBreakpoints == true)
        Assert.True(session.HasTemporaryBreakpoints);
        // Verify session resumed (RequestResume called, not RequestStepOneTick)
        Assert.Equal(resumeCountBefore + 1, tc.ResumeCount);
        Assert.Equal(stepCountBefore, tc.StepRequestCount);
        // Session should not be paused
        Assert.False(session.IsPaused);

        // Simulate hitting the successor (NodeB — the next node after NodeA)
        Probe(session, E1, NodeB.ToString("D"));

        // Session should re-pause and temps should be cleared
        Assert.True(session.IsPaused);
        Assert.False(session.HasTemporaryBreakpoints);
    }

    // ========================================================================
    // Test 6: Continue clears leftover temp breakpoints
    // ========================================================================

    [Fact]
    public void Continue_ClearsLeftoverTempBreakpoints()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Set temp breakpoints (simulating that a Step was initiated
        // but no temp was hit yet)
        var target = new BreakpointTarget(AssetIdA, GraphId1, NodeB);
        session.SetTemporaryBreakpoints(new[] { target });
        Assert.True(session.HasTemporaryBreakpoints);

        // Call Continue — should clear temps
        session.Continue();

        Assert.False(session.HasTemporaryBreakpoints);
        Assert.False(session.IsPaused);
        Assert.Equal(1, tc.ResumeCount);
    }

    // ========================================================================
    // Test 7: Step on terminal node resumes (no temps set)
    // ========================================================================

    [Fact]
    public void Step_TerminalNode_CallsContinue()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Register the graph
        var graph = BuildLinear3NodeGraph();
        session.RegisterGraph(graph);

        // Pause at the Return node (NodeC, terminal) by setting a breakpoint on it.
        session.SetBreakpoint(AssetIdA, GraphId1, NodeC);
        Probe(session, E1, NodeC.ToString("D"));

        Assert.True(session.IsPaused);
        var resumeCountBefore = tc.ResumeCount;
        var stepCountBefore   = tc.StepRequestCount;

        // Step from a terminal node — should call Continue (resume), not set temps
        session.StepOver();

        Assert.False(session.IsPaused);
        Assert.False(session.HasTemporaryBreakpoints);
        Assert.Equal(resumeCountBefore + 1, tc.ResumeCount);
        Assert.Equal(stepCountBefore, tc.StepRequestCount);
    }

    // ========================================================================
    // Test 8: Temp BPs are invisible (not in GetBreakpoints)
    // ========================================================================

    [Fact]
    public void TempBreakpoints_NotExposedInGetBreakpoints()
    {
        var session = MakeSession();

        // Set a regular breakpoint
        session.SetBreakpoint(AssetIdA, GraphId1, NodeA);

        // Set a temp breakpoint
        var target = new BreakpointTarget(AssetIdA, GraphId1, NodeB);
        session.SetTemporaryBreakpoints(new[] { target });

        // GetBreakpoints must NOT include temp BPs
        var bps = session.GetBreakpoints();
        Assert.Single(bps);
        Assert.Equal(NodeA.ToString("D"), bps[0].NodeId);
    }

    // ========================================================================
    // Test 9: Step with no graph registered falls back to single-tick
    // ========================================================================

    [Fact]
    public void Step_NoGraphRegistered_FallsBackToSingleTick()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Pause via breakpoint hit
        session.SetBreakpoint(AssetIdA, GraphId1, NodeA);
        Probe(session, E1, NodeA.ToString("D"));
        Assert.True(session.IsPaused);

        var stepCountBefore = tc.StepRequestCount;
        var resumeCountBefore = tc.ResumeCount;

        // Call Step — no graph registered, should fall back to single-tick
        session.StepOver();

        Assert.Equal(stepCountBefore + 1, tc.StepRequestCount);
        Assert.Equal(resumeCountBefore, tc.ResumeCount);
        Assert.False(session.IsPaused);
        Assert.False(session.HasTemporaryBreakpoints);
    }
}
