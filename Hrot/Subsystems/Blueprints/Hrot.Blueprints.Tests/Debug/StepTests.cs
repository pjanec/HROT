using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-003 step semantics: StepOver, StepInto, StepOut (SC1-SC4).
/// Steps use soft-pause (Patch 1): RequestStepOneTick is called immediately,
/// the session re-pauses when the matching OnNodeEnter fires in the stepped tick.
/// </summary>
public sealed class StepTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity E1 => new Entity(1, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession(MockTimeController? tc = null)
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            tc ?? new MockTimeController());

    /// <summary>Hit the given breakpoint node once to put the session into paused state.</summary>
    private static void HitBreakpoint(BlueprintDebugSession session, Entity entity, string nodeIdStr)
        => ((IBlueprintProbeSink)session).OnNodeEnter(entity, nodeIdStr);

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

    // ---- SC1: StepOver pauses on next same-or-shallower-depth node ------------

    /// <summary>
    /// StepOver must call RequestStepOneTick once, clear IsPaused, then re-pause
    /// when the next OnNodeEnter fires at the same (or shallower) call depth.
    /// </summary>
    [Fact]
    public void StepOver_RequestsOneTick_ThenPausesOnNextSameDepthNode()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        HitBreakpoint(session, E1, NodeId1.ToString("D"));

        Assert.True(session.IsPaused);

        session.StepOver();

        Assert.Equal(1, tc.StepRequestCount);
        Assert.False(session.IsPaused);

        // Next node at depth 0 (same as step-from depth 0) -- must match.
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "next-node");

        Assert.True(session.IsPaused);
    }

    // ---- SC2: StepInto pauses on very next node for the same entity -----------

    /// <summary>
    /// StepInto must re-pause at the next OnNodeEnter for the same entity,
    /// regardless of call depth.
    /// </summary>
    [Fact]
    public void StepInto_PausesOnNextNodeForSameEntity()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        HitBreakpoint(session, E1, NodeId1.ToString("D"));

        session.StepInto();

        Assert.False(session.IsPaused);

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "any-node");

        Assert.True(session.IsPaused);
    }

    // ---- SC3: StepOut pauses only when call depth becomes shallower -----------

    /// <summary>
    /// StepOut must NOT pause at nodes at the same depth as the step-from depth.
    /// It must pause when the depth becomes strictly shallower (after OnPeerCallExit).
    /// </summary>
    [Fact]
    public void StepOut_PausesOnlyAtShallerDepth()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        // Simulate entering a peer call to reach depth 1.
        ((IBlueprintProbeSink)session).OnPeerCallEnter(E1, "some-asset", "some-graph");

        // Hit breakpoint while at depth 1.
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        HitBreakpoint(session, E1, NodeId1.ToString("D"));

        Assert.True(session.IsPaused);

        session.StepOut();

        Assert.False(session.IsPaused);

        // Still inside the call frame (depth 1 -- same as step-from depth) -- must NOT pause.
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "still-deep-node");
        Assert.False(session.IsPaused);

        // Exit the call frame: depth drops to 0.
        ((IBlueprintProbeSink)session).OnPeerCallExit(E1, "some-asset", "some-graph");

        // Next node at depth 0 (strictly shallower than 1) -- must pause.
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "shallow-node");
        Assert.True(session.IsPaused);
    }

    // ---- SC4: StepOver issues exactly one RequestStepOneTick ------------------

    /// <summary>
    /// StepOver must call RequestStepOneTick exactly once per step invocation.
    /// </summary>
    [Fact]
    public void StepOver_StepRequestCount_IsExactlyOne()
    {
        var tc      = new MockTimeController();
        var session = MakeSession(tc);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        HitBreakpoint(session, E1, NodeId1.ToString("D"));

        session.StepOver();

        Assert.Equal(1, tc.StepRequestCount);
    }
}
