using System.Runtime.CompilerServices;
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-006: DebugProbe dispatch -- null-sink no-op, non-null forwarding,
/// and zero-allocation guarantee on the null-sink path (SC1-SC4).
/// </summary>
[Collection("DebugProbe")]
public sealed class ProbeDispatchTests : IDisposable
{
    // Save and restore the static Sink so tests do not interfere with each other.
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    public void Dispose() => DebugProbe.Sink = _savedSink;

    private static Entity E1 => new Entity(1, 0);

    // ---- SC1: null sink -- no exception, no state change ---------------------

    /// <summary>
    /// When DebugProbe.Sink is null, NodeEnter must complete without exception and
    /// must not change any observable state.
    /// </summary>
    [Fact]
    public void DebugProbe_NullSink_OnNodeEnter_IsNoOp()
    {
        DebugProbe.Sink = null;

        // Must not throw.
        DebugProbe.NodeEnter(E1, "some-id");
    }

    // ---- SC2: non-null sink -- forwards to sink ------------------------------

    /// <summary>
    /// When DebugProbe.Sink is set to a CapturingDebugSession, NodeEnter must forward
    /// the call so that the session records the entry.
    /// </summary>
    [Fact]
    public void DebugProbe_NonNullSink_OnNodeEnter_ForwardsToSink()
    {
        var capturing  = new CapturingDebugSession();
        DebugProbe.Sink = capturing;

        DebugProbe.NodeEnter(E1, "some-id");

        Assert.Contains(capturing.NodeEntries, r => r.Self == E1 && r.NodeId == "some-id");
    }

    // ---- SC3: null sink -- zero allocation on PinValueChanged ----------------

    /// <summary>
    /// Calling DebugProbe.PinValueChanged with a null Sink must allocate zero bytes on the heap.
    /// </summary>
    [Fact]
    public void DebugProbe_NullSink_OnPinValueChanged_ZeroAllocation()
    {
        DebugProbe.Sink = null;

        // Warm up to let JIT settle.
        for (int i = 0; i < 10; i++)
            CallPinValueChanged(E1, "pin-id", 42);

        long before = GC.GetAllocatedBytesForCurrentThread();
        CallPinValueChanged(E1, "pin-id", 42);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    // ---- SC4: null sink -- zero allocation on NodeEnter ----------------------

    /// <summary>
    /// Calling DebugProbe.NodeEnter with a null Sink must allocate zero bytes on the heap.
    /// </summary>
    [Fact]
    public void DebugProbe_NullSink_OnNodeEnter_ZeroAllocation()
    {
        DebugProbe.Sink = null;

        // Warm up to let JIT settle.
        for (int i = 0; i < 10; i++)
            CallNodeEnter(E1, "node-id");

        long before = GC.GetAllocatedBytesForCurrentThread();
        CallNodeEnter(E1, "node-id");
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    // ---- NoInlining helpers --------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallPinValueChanged(Entity entity, string pinId, int value)
        => DebugProbe.PinValueChanged(entity, pinId, value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallNodeEnter(Entity entity, string nodeId)
        => DebugProbe.NodeEnter(entity, nodeId);
}
