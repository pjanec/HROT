using System.Collections.Generic;
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;
using Xunit;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// BP-35 (D4) — <see cref="MultiplexingProbeSink"/> lets more than one observer watch a single run.
/// <c>DebugProbe.Sink</c> is a single reference, so attaching a second debugger used to mean
/// detaching the first.
/// </summary>
public sealed class MultiplexingProbeSinkTests
{
    /// <summary>Minimal recording sink; also a session so the OnNewTick fan-out can be observed.</summary>
    private sealed class RecordingSink : IBlueprintProbeSink
    {
        public List<string> Events { get; } = new();
        public void OnNodeEnter(Entity self, string nodeId) => Events.Add($"enter:{nodeId}");
        public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged
            => Events.Add($"pin:{pinId}={value}");
        public void OnPeerCallEnter(Entity self, string peer, string method) => Events.Add($"peer-in:{method}");
        public void OnPeerCallExit(Entity self, string peer, string method)  => Events.Add($"peer-out:{method}");
        public void OnCollectionWriteFailed(Entity self, string nodeId, string op, string reason)
            => Events.Add($"cwf:{op}:{reason}");
    }

    private sealed class ThrowingSink : IBlueprintProbeSink
    {
        public void OnNodeEnter(Entity self, string nodeId) => throw new InvalidOperationException("boom");
        public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
        public void OnPeerCallEnter(Entity self, string peer, string method) { }
        public void OnPeerCallExit(Entity self, string peer, string method) { }
    }

    // ---- fan-out ---------------------------------------------------------

    [Fact]
    public void ForwardsEveryProbeKind_ToEverySink()
    {
        var a = new RecordingSink();
        var b = new RecordingSink();
        var mux = new MultiplexingProbeSink(a, b);
        var e = default(Entity);

        mux.OnNodeEnter(e, "n1");
        mux.OnPinValueChanged(e, "p1", 42);
        mux.OnPeerCallEnter(e, "asset", "M");
        mux.OnPeerCallExit(e, "asset", "M");
        mux.OnCollectionWriteFailed(e, "n1", "Add", "op-rejected");

        var expected = new[] { "enter:n1", "pin:p1=42", "peer-in:M", "peer-out:M", "cwf:Add:op-rejected" };
        Assert.Equal(expected, a.Events);
        Assert.Equal(expected, b.Events);
    }

    /// <summary>
    /// <c>OnCollectionWriteFailed</c> has a default interface implementation. If the composite relied
    /// on that instead of forwarding explicitly, the never-silent collection-write diagnostic would
    /// be dropped for every inner sink.
    /// </summary>
    [Fact]
    public void CollectionWriteFailed_IsForwarded_NotSwallowedByTheDefaultImpl()
    {
        var sink = new RecordingSink();
        var mux  = new MultiplexingProbeSink(sink);

        mux.OnCollectionWriteFailed(default, "n", "SetAt", "component-absent");

        Assert.Contains("cwf:SetAt:component-absent", sink.Events);
    }

    // ---- attach / detach -------------------------------------------------

    [Fact]
    public void Add_IsIdempotent_SoADoubleAttachCannotDoubleDeliver()
    {
        var sink = new RecordingSink();
        var mux  = new MultiplexingProbeSink();

        Assert.True(mux.Add(sink));
        Assert.False(mux.Add(sink));
        Assert.Equal(1, mux.Count);

        mux.OnNodeEnter(default, "n");
        Assert.Single(sink.Events);
    }

    [Fact]
    public void Add_RejectsNullAndSelf()
    {
        var mux = new MultiplexingProbeSink();
        Assert.False(mux.Add(null));
        Assert.False(mux.Add(mux));          // would recurse forever
        Assert.Equal(0, mux.Count);
    }

    [Fact]
    public void Remove_DetachesOnlyThatSink_AndPreservesOrder()
    {
        var a = new RecordingSink();
        var b = new RecordingSink();
        var c = new RecordingSink();
        var mux = new MultiplexingProbeSink(a, b, c);

        Assert.True(mux.Remove(b));
        Assert.False(mux.Remove(b));
        Assert.Equal(new IBlueprintProbeSink[] { a, c }, mux.Sinks);

        mux.OnNodeEnter(default, "n");
        Assert.Single(a.Events);
        Assert.Empty(b.Events);
        Assert.Single(c.Events);
    }

    [Fact]
    public void Clear_DetachesEverything()
    {
        var sink = new RecordingSink();
        var mux  = new MultiplexingProbeSink(sink);

        mux.Clear();
        mux.OnNodeEnter(default, "n");

        Assert.Equal(0, mux.Count);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void EmptyMultiplexer_IsANoOp()
    {
        var mux = new MultiplexingProbeSink();
        mux.OnNodeEnter(default, "n");            // must not throw
        Assert.Equal(0, mux.Count);
    }

    /// <summary>Mutating during iteration must not disturb the in-flight event (copy-on-write).</summary>
    [Fact]
    public void RemovingDuringDispatch_DoesNotAffectTheInFlightEvent()
    {
        var later = new RecordingSink();
        var mux   = new MultiplexingProbeSink();
        var mutator = new MutatingSink(() => mux.Remove(later));
        mux.Add(mutator);
        mux.Add(later);

        mux.OnNodeEnter(default, "n");

        Assert.Single(later.Events);   // still received the event it was attached for
        Assert.Equal(1, mux.Count);    // but is detached afterwards
    }

    private sealed class MutatingSink : IBlueprintProbeSink
    {
        private readonly Action _onEnter;
        public MutatingSink(Action onEnter) => _onEnter = onEnter;
        public void OnNodeEnter(Entity self, string nodeId) => _onEnter();
        public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
        public void OnPeerCallEnter(Entity self, string peer, string method) { }
        public void OnPeerCallExit(Entity self, string peer, string method) { }
    }

    // ---- exception policy ------------------------------------------------

    /// <summary>
    /// A throwing sink propagates, exactly as it would wired directly to <c>DebugProbe.Sink</c>.
    /// Swallowing would hide a broken observer, which is the opposite of what a debug facility
    /// should do. Pinned so the policy is a decision, not an accident.
    /// </summary>
    [Fact]
    public void ThrowingSink_Propagates_RatherThanBeingSwallowed()
    {
        var mux = new MultiplexingProbeSink(new ThrowingSink(), new RecordingSink());

        Assert.Throws<InvalidOperationException>(() => mux.OnNodeEnter(default, "n"));
    }

    // ---- the DebugProbe.NewTick trap ------------------------------------

    /// <summary>
    /// <c>DebugProbe.NewTick</c> resolves the session via <c>Sink as IBlueprintDebugSession</c>.
    /// The multiplexer is a probe sink, not a session, so without an explicit fan-out every session
    /// behind it would silently stop receiving <c>OnNewTick</c> — quietly breaking per-frame
    /// breakpoint dedup. This is the regression guard for that.
    /// </summary>
    [Fact]
    public void DebugProbe_NewTick_ReachesSessionsBehindTheMultiplexer()
    {
        var session = new CapturingDebugSession();
        var mux     = new MultiplexingProbeSink(session);

        var previous = DebugProbe.Sink;
        try
        {
            DebugProbe.Sink = mux;
            DebugProbe.NewTick();
            DebugProbe.NewTick();
        }
        finally { DebugProbe.Sink = previous; }

        Assert.Equal(2, session.NewTickCount);
    }

    [Fact]
    public void DebugProbe_NewTick_StillReachesADirectlyAttachedSession()
    {
        var session = new CapturingDebugSession();

        var previous = DebugProbe.Sink;
        try
        {
            DebugProbe.Sink = session;
            DebugProbe.NewTick();
        }
        finally { DebugProbe.Sink = previous; }

        Assert.Equal(1, session.NewTickCount);
    }
}
