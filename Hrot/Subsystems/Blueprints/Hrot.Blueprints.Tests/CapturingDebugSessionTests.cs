using Fdp.Core;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Contract tests for CapturingDebugSession (TH-008, SC1-SC6).
/// </summary>
public sealed class CapturingDebugSessionTests
{
    private static Entity E1 => new Entity(1, 0);
    private static Entity E2 => new Entity(2, 0);

    // SC1: OnNodeEnter records are appended in call order.
    [Fact]
    public void OnNodeEnter_AppendedInCallOrder()
    {
        var session = new CapturingDebugSession();

        session.OnNodeEnter(E1, "node-A");
        session.OnNodeEnter(E1, "node-B");
        session.OnNodeEnter(E1, "node-A");

        Assert.Equal(3, session.NodeEntries.Count);
        Assert.Equal("node-A", session.NodeEntries[0].NodeId);
        Assert.Equal("node-B", session.NodeEntries[1].NodeId);
        Assert.Equal("node-A", session.NodeEntries[2].NodeId);
    }

    // SC2: Hit/HitCount helpers reflect recorded entries.
    [Fact]
    public void HitHelpers_ReflectRecordedEntries()
    {
        var session = new CapturingDebugSession();

        session.OnNodeEnter(E1, "node-A");
        session.OnNodeEnter(E2, "node-B");
        session.OnNodeEnter(E1, "node-A");

        Assert.True(session.Hit("node-A"));
        Assert.False(session.Hit("node-X"));
        Assert.Equal(2, session.HitCount("node-A"));
        Assert.Equal(1, session.HitCount("node-B"));
    }

    // SC3: OnPinValueChanged records value and pin id.
    [Fact]
    public void OnPinValueChanged_RecordsPinAndValue()
    {
        var session = new CapturingDebugSession();

        session.OnPinValueChanged(E1, "pin-Out", 42);
        session.OnPinValueChanged(E1, "pin-Flag", true);

        Assert.Equal(2, session.PinValues.Count);
        Assert.Equal("pin-Out", session.PinValues[0].PinId);
        Assert.Equal(42, session.PinValues[0].Value);
        Assert.Equal("pin-Flag", session.PinValues[1].PinId);
        Assert.Equal(true, session.PinValues[1].Value);
    }

    // SC4: Breakpoint fires OnBreakpointHit when matching node is entered.
    [Fact]
    public void Breakpoint_FiresOnBreakpointHitForMatchingNode()
    {
        var session = new CapturingDebugSession();
        BreakpointHit? captured = null;
        session.OnBreakpointHit += h => captured = h;

        session.SetBreakpoint("node-A");
        Assert.True(session.IsAnyBreakpointActive);

        session.OnNodeEnter(E1, "node-A");

        Assert.NotNull(captured);
        Assert.Equal("node-A", captured!.NodeId);
        Assert.Equal(E1, captured.Self);
    }

    // SC5: Breakpoint does NOT fire for non-matching node.
    [Fact]
    public void Breakpoint_DoesNotFireForNonMatchingNode()
    {
        var session = new CapturingDebugSession();
        bool fired = false;
        session.OnBreakpointHit += _ => fired = true;

        session.SetBreakpoint("node-A");
        session.OnNodeEnter(E1, "node-B");

        Assert.False(fired);
    }

    // SC6: Clear resets all captured data.
    [Fact]
    public void Clear_ResetsAllCapturedData()
    {
        var session = new CapturingDebugSession();

        session.OnNodeEnter(E1, "node-A");
        session.OnPinValueChanged(E1, "pin-Out", 99);
        session.Clear();

        Assert.Empty(session.NodeEntries);
        Assert.Empty(session.PinValues);
    }

    // SC7: ClearBreakpoint removes breakpoint; IsAnyBreakpointActive reflects state.
    [Fact]
    public void ClearBreakpoint_RemovesBreakpoint()
    {
        var session = new CapturingDebugSession();

        session.SetBreakpoint("node-A");
        session.SetBreakpoint("node-B");
        Assert.True(session.IsAnyBreakpointActive);

        session.ClearBreakpoint("node-A");
        session.ClearBreakpoint("node-B");

        Assert.False(session.IsAnyBreakpointActive);
    }

    // SC8: HitsFor filters by entity.
    [Fact]
    public void HitsFor_FiltersByEntity()
    {
        var session = new CapturingDebugSession();

        session.OnNodeEnter(E1, "node-A");
        session.OnNodeEnter(E2, "node-B");
        session.OnNodeEnter(E1, "node-C");

        var e1Hits = session.HitsFor(E1);
        Assert.Equal(2, e1Hits.Count);
        Assert.All(e1Hits, r => Assert.Equal(E1, r.Self));
    }

    // Phase 3 integration test placeholder: requires compiled Blueprint assembly
    // to be wired through DebugProbe.Sink.
    [Fact(Skip = "Requires Phase 3 compiler")]
    [Trait("Category", "RequiresCompiler")]
    public void Debug_TraceMode_RecordsAllNodeEntries()
    {
        // Phase 3 body: compileAndLoad a trace-mode asset,
        // call fixture.TickFrame, assert session.NodeEntries covers all nodes.
        throw new NotImplementedException("Phase 3 compiler required.");
    }

    // Phase 3 integration test placeholder: requires compiled Blueprint assembly.
    [Fact(Skip = "Requires Phase 3 compiler")]
    [Trait("Category", "RequiresCompiler")]
    public void Debug_Breakpoint_FiresWhenNodeEntered()
    {
        // Phase 3 body: compileAndLoad asset, set breakpoint, tick, assert event fired.
        throw new NotImplementedException("Phase 3 compiler required.");
    }
}
