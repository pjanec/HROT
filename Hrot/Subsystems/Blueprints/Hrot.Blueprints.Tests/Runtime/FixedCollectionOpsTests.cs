using Fdp.Core;
using Hrot.AI.Behaviors;
using Hrot.AI.Behaviors.Brains;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// FC-0 (Fixed Collections, Q#20 "G1 resolution") -- the InlineArray write ROUND-TRIP GATE plus the
/// reference-accessor contract tests: every mutation lands in real ECS chunk storage when routed
/// through the curated <c>BpFixedListDemoOps</c> accessors on a <c>GetComponentRW</c> ref, the
/// tail-always-default invariant (G6) holds after every shrinking op, the false-on-overflow /
/// bounded-index contract holds, and the F2 defensive Count-clamp makes a garbage <c>Count</c>
/// harmless. The raw-<c>fixed</c>-buffer idiom (<c>BpCollectionDemoOps</c>) is exercised with the
/// same contract.
///
/// <para>
/// <b>Compiler-behavior documentation tests</b> (<see cref="NaiveRefLocalWrite_CurrentToolchain_Lands"/> /
/// <see cref="ValueCopyWrite_IsLost"/>): <c>EntityRepository.GetComponentRW</c>'s doc warns that
/// <c>ref var q = ref GetComponentRW&lt;T&gt;(e); q.Buf[0] = x;</c> silently loses the write
/// (ldobj defensive copy). Measured on the current toolchain (.NET SDK 8.0.4xx) that claim does
/// NOT reproduce -- a naive element write through a <c>ref</c> local LANDS; the only reproducible
/// loss mode is the missing-<c>ref</c> VALUE COPY (<c>var q = GetComponentRW&lt;T&gt;(e)</c>),
/// which loses scalar writes just the same and is not InlineArray-specific. These tests pin the
/// measured behavior so a future compiler change in either direction fails loudly here first. The
/// accessor + <c>Span&lt;T&gt;</c> convention stays mandated regardless (readonly-read defensive
/// copies, uniformity, generator template, Q#5-C off-graph rule).
/// </para>
/// </summary>
public sealed class FixedCollectionOpsTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity _entity;

    public FixedCollectionOpsTests()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponent<BpFixedListDemo>();
        _repo.RegisterComponent<BpCollectionDemo>();
        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, new BpFixedListDemo());
        _repo.AddComponent(_entity, new BpCollectionDemo());
    }

    public void Dispose() => _repo.Dispose();

    private ref BpFixedListDemo Rw() => ref _repo.GetComponentRW<BpFixedListDemo>(_entity);

    private BpFixedListDemo ReRead() => _repo.GetComponentRO<BpFixedListDemo>(_entity);

    /// <summary>Reads raw slot <paramref name="i"/> (0..Capacity) regardless of Count -- for tail-invariant assertions.</summary>
    private int RawSlot(int i)
    {
        var c = ReRead();
        ReadOnlySpan<int> s = c.Items;
        return s[i];
    }

    // -----------------------------------------------------------------------
    // The round-trip gate (R3): accessor writes through the RW ref reach chunk storage
    // -----------------------------------------------------------------------

    [Fact]
    public void AccessorWrites_ThroughRwRef_RoundTrip()
    {
        ref var c = ref Rw();
        Assert.True(BpFixedListDemoOps.Add(ref c, 10));
        Assert.True(BpFixedListDemoOps.Add(ref c, 20));
        Assert.True(BpFixedListDemoOps.Add(ref c, 30));

        var read = ReRead();
        Assert.Equal(3, BpFixedListDemoOps.Count(in read));
        Assert.Equal(10, BpFixedListDemoOps.Item(in read, 0));
        Assert.Equal(20, BpFixedListDemoOps.Item(in read, 1));
        Assert.Equal(30, BpFixedListDemoOps.Item(in read, 2));

        ref var c2 = ref Rw();
        Assert.True(BpFixedListDemoOps.SetAt(ref c2, 1, 99));
        Assert.Equal(99, BpFixedListDemoOps.Item(ReRead(), 1));
    }

    [Fact]
    public void InsertAt_ShiftsTailUp_AndAppendsAtCount()
    {
        ref var c = ref Rw();
        BpFixedListDemoOps.Add(ref c, 1);
        BpFixedListDemoOps.Add(ref c, 3);
        Assert.True(BpFixedListDemoOps.InsertAt(ref c, 1, 2));   // middle
        Assert.True(BpFixedListDemoOps.InsertAt(ref c, 3, 4));   // i == Count => append

        var read = ReRead();
        Assert.Equal(4, read.Count);
        Assert.Equal(1, BpFixedListDemoOps.Item(in read, 0));
        Assert.Equal(2, BpFixedListDemoOps.Item(in read, 1));
        Assert.Equal(3, BpFixedListDemoOps.Item(in read, 2));
        Assert.Equal(4, BpFixedListDemoOps.Item(in read, 3));
    }

    [Fact]
    public void RemoveAt_ShiftsTailDown()
    {
        ref var c = ref Rw();
        BpFixedListDemoOps.Add(ref c, 1);
        BpFixedListDemoOps.Add(ref c, 2);
        BpFixedListDemoOps.Add(ref c, 3);
        Assert.True(BpFixedListDemoOps.RemoveAt(ref c, 0));

        var read = ReRead();
        Assert.Equal(2, read.Count);
        Assert.Equal(2, BpFixedListDemoOps.Item(in read, 0));
        Assert.Equal(3, BpFixedListDemoOps.Item(in read, 1));
    }

    // -----------------------------------------------------------------------
    // G6 -- tail-always-default invariant
    // -----------------------------------------------------------------------

    [Fact]
    public void RemoveAt_ZeroesVacatedSlot()
    {
        ref var c = ref Rw();
        for (int i = 0; i < 4; i++) BpFixedListDemoOps.Add(ref c, 100 + i);
        Assert.True(BpFixedListDemoOps.RemoveAt(ref c, 1));

        Assert.Equal(3, ReRead().Count);
        Assert.Equal(0, RawSlot(3));                 // vacated tail slot re-zeroed
    }

    [Fact]
    public void Clear_ZeroesAllUsedSlots()
    {
        ref var c = ref Rw();
        for (int i = 0; i < 4; i++) BpFixedListDemoOps.Add(ref c, 100 + i);
        BpFixedListDemoOps.Clear(ref c);

        Assert.Equal(0, ReRead().Count);
        for (int i = 0; i < BpFixedListDemo.Capacity; i++)
            Assert.Equal(0, RawSlot(i));
    }

    [Fact]
    public void ResizeShrink_ZeroesDroppedTail_AndGrowNeedsNoFill()
    {
        ref var c = ref Rw();
        for (int i = 0; i < 4; i++) BpFixedListDemoOps.Add(ref c, 100 + i);
        Assert.True(BpFixedListDemoOps.Resize(ref c, 1));

        Assert.Equal(1, ReRead().Count);
        Assert.Equal(100, RawSlot(0));
        for (int i = 1; i < BpFixedListDemo.Capacity; i++)
            Assert.Equal(0, RawSlot(i));             // dropped tail re-zeroed

        // Grow back over the zeroed tail: slots must read as default WITHOUT any fill step.
        ref var c2 = ref Rw();
        Assert.True(BpFixedListDemoOps.Resize(ref c2, 3));
        var read = ReRead();
        Assert.Equal(3, read.Count);
        Assert.Equal(0, BpFixedListDemoOps.Item(in read, 1));
        Assert.Equal(0, BpFixedListDemoOps.Item(in read, 2));
    }

    // -----------------------------------------------------------------------
    // Overflow / bounds contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Add_OnFull_ReturnsFalse_StateUnchanged()
    {
        ref var c = ref Rw();
        for (int i = 0; i < BpFixedListDemo.Capacity; i++)
            Assert.True(BpFixedListDemoOps.Add(ref c, i));

        Assert.False(BpFixedListDemoOps.Add(ref c, 999));

        var read = ReRead();
        Assert.Equal(BpFixedListDemo.Capacity, read.Count);
        for (int i = 0; i < BpFixedListDemo.Capacity; i++)
            Assert.Equal(i, BpFixedListDemoOps.Item(in read, i));
    }

    [Fact]
    public void SetAt_OutOfLogicalRange_ReturnsFalse_NeverGrowsCount()
    {
        ref var c = ref Rw();
        BpFixedListDemoOps.Add(ref c, 1);

        Assert.False(BpFixedListDemoOps.SetAt(ref c, 1, 99));    // within capacity, beyond Count
        Assert.False(BpFixedListDemoOps.SetAt(ref c, -1, 99));

        var read = ReRead();
        Assert.Equal(1, read.Count);                              // SetAt never grew Count
        Assert.Equal(0, RawSlot(1));                              // and never wrote past it
    }

    [Fact]
    public void InsertAt_FullOrPastCount_ReturnsFalse_AndResizeBeyondCapacity_ReturnsFalse()
    {
        ref var c = ref Rw();
        BpFixedListDemoOps.Add(ref c, 1);
        Assert.False(BpFixedListDemoOps.InsertAt(ref c, 2, 99)); // i > Count
        Assert.False(BpFixedListDemoOps.RemoveAt(ref c, 1));     // i >= Count
        Assert.False(BpFixedListDemoOps.Resize(ref c, BpFixedListDemo.Capacity + 1));
        Assert.Equal(1, ReRead().Count);
    }

    // -----------------------------------------------------------------------
    // F2 -- defensive Count clamp: garbage Count can never drive an OOB access
    // -----------------------------------------------------------------------

    [Fact]
    public void GarbageCount_IsClampedByEveryOp_NoOutOfBoundsAccess()
    {
        ref var c = ref Rw();
        c.Count = 999_999;                                        // simulate corrupted/reused memory

        var read = ReRead();
        Assert.Equal(BpFixedListDemo.Capacity, BpFixedListDemoOps.Count(in read));

        ref var c2 = ref Rw();
        Assert.False(BpFixedListDemoOps.Add(ref c2, 1));          // clamped => full
        Assert.True(BpFixedListDemoOps.SetAt(ref c2, 2, 7));      // clamped bounds, no throw
        BpFixedListDemoOps.Clear(ref c2);                         // clamped range, no throw
        Assert.Equal(0, ReRead().Count);
    }

    // -----------------------------------------------------------------------
    // Compiler-behavior documentation (see class doc): the REAL loss mode is the
    // missing-`ref` value copy; the naive ref-local write lands on this toolchain.
    // -----------------------------------------------------------------------

    [Fact]
    public void NaiveRefLocalWrite_CurrentToolchain_Lands()
    {
        ref var c = ref Rw();
        c.Count = 1;
        c.Items[0] = 42;   // the shape GetComponentRW's doc claims is silently lost

        Assert.Equal(42, RawSlot(0));   // measured: it LANDS (doc's ldobj claim not reproducible)
    }

    [Fact]
    public void ValueCopyWrite_IsLost()
    {
        var copy = _repo.GetComponentRW<BpFixedListDemo>(_entity);   // missing `ref` -- struct copy
        copy.Count = 1;
        ((Span<int>)copy.Items)[0] = 42;

        var read = ReRead();
        Assert.Equal(0, read.Count);     // lost -- the actual hazard the accessor convention buries
        Assert.Equal(0, RawSlot(0));
    }

    // -----------------------------------------------------------------------
    // The raw-`fixed`-buffer idiom (BpCollectionDemoOps) -- same contract
    // -----------------------------------------------------------------------

    [Fact]
    public void FixedBufferIdiom_RoundTrip_Invariant_Overflow()
    {
        ref var c = ref _repo.GetComponentRW<BpCollectionDemo>(_entity);
        Assert.True(BpCollectionDemoOps.Add(ref c, 5));
        Assert.True(BpCollectionDemoOps.Add(ref c, 6));
        Assert.True(BpCollectionDemoOps.InsertAt(ref c, 1, 55));
        Assert.True(BpCollectionDemoOps.SetAt(ref c, 0, 50));

        var read = _repo.GetComponentRO<BpCollectionDemo>(_entity);
        Assert.Equal(3, BpCollectionDemoOps.Count(in read));
        Assert.Equal(50, BpCollectionDemoOps.Item(in read, 0));
        Assert.Equal(55, BpCollectionDemoOps.Item(in read, 1));
        Assert.Equal(6, BpCollectionDemoOps.Item(in read, 2));

        ref var c2 = ref _repo.GetComponentRW<BpCollectionDemo>(_entity);
        Assert.True(BpCollectionDemoOps.RemoveAt(ref c2, 0));
        var read2 = _repo.GetComponentRO<BpCollectionDemo>(_entity);
        Assert.Equal(2, read2.Count);
        Assert.Equal(0, BpCollectionDemoOps.Item(in read2, 2));  // G6: vacated slot zeroed

        ref var c3 = ref _repo.GetComponentRW<BpCollectionDemo>(_entity);
        Assert.True(BpCollectionDemoOps.Resize(ref c3, 4));
        Assert.False(BpCollectionDemoOps.Add(ref c3, 9));        // full
        BpCollectionDemoOps.Clear(ref c3);
        Assert.Equal(0, _repo.GetComponentRO<BpCollectionDemo>(_entity).Count);
    }

    // -----------------------------------------------------------------------
    // FC-0 DebugProbe overflow hook
    // -----------------------------------------------------------------------

    private sealed class CapturingCollectionSink : IBlueprintProbeSink
    {
        public readonly List<(Entity Self, string NodeId, string Op, string Reason)> Failures = new();
        public void OnNodeEnter(Entity self, string nodeId) { }
        public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
        public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
        public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }
        public void OnCollectionWriteFailed(Entity self, string nodeId, string op, string reason)
            => Failures.Add((self, nodeId, op, reason));
    }

    [Fact]
    public void DebugProbe_CollectionWriteFailed_RoutesToSink_AndDefaultsToNoOp()
    {
        var prev = DebugProbe.Sink;
        try
        {
            // No sink: must be a silent no-op.
            DebugProbe.Sink = null;
            DebugProbe.CollectionWriteFailed(_entity, "n1", "Add", "op-rejected");

            // Sink that does NOT override the member: default interface implementation no-ops.
            DebugProbe.Sink = NullProbeSink.Instance;
            DebugProbe.CollectionWriteFailed(_entity, "n1", "Add", "op-rejected");

            // Capturing sink: routed.
            var sink = new CapturingCollectionSink();
            DebugProbe.Sink = sink;
            DebugProbe.CollectionWriteFailed(_entity, "n1", "Add", "op-rejected");
            DebugProbe.CollectionWriteFailed(_entity, "n2", "SetAt", "component-absent");

            Assert.Equal(2, sink.Failures.Count);
            Assert.Equal((_entity, "n1", "Add", "op-rejected"), sink.Failures[0]);
            Assert.Equal((_entity, "n2", "SetAt", "component-absent"), sink.Failures[1]);
        }
        finally
        {
            DebugProbe.Sink = prev;
        }
    }
}
