using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Core.Network;

namespace Hrot.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ScenarioEntityCreationRequestSource"/> (TASK-C001)
/// and <see cref="CompositeEntityCreationRequestSource"/> (TASK-C002).
/// </summary>
public class EntityCreationRequestSourceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityCreationRequest MakeRequest() =>
        new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 1,
            TkbType            = 42L,
            DisType            = 0x0100_0000_0000_0001UL,
        };

    // ══════════════════════════════════════════════════════════════════════════
    // TASK-C001: ScenarioEntityCreationRequestSource
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// C001 success condition 1: 3 enqueued requests are drained in FIFO order;
    /// queue is empty after the call.
    /// </summary>
    [Fact]
    public void ScenarioSource_BasicEnqueueDrain_FifoOrder()
    {
        var source = new ScenarioEntityCreationRequestSource();
        var r1 = MakeRequest();
        var r2 = MakeRequest();
        var r3 = MakeRequest();

        source.Enqueue(r1);
        source.Enqueue(r2);
        source.Enqueue(r3);

        var received = new List<EntityCreationRequest>();
        source.ProcessRequests(received.Add);

        Assert.Equal(3, received.Count);
        Assert.Same(r1, received[0]);
        Assert.Same(r2, received[1]);
        Assert.Same(r3, received[2]);

        // Queue is now empty - second drain yields nothing.
        var secondDrain = new List<EntityCreationRequest>();
        source.ProcessRequests(secondDrain.Add);
        Assert.Empty(secondDrain);
    }

    /// <summary>
    /// C001 success condition 2: drain cap of 500 is enforced over two calls.
    /// </summary>
    [Fact]
    public void ScenarioSource_MaxItemsPerTick_Cap500()
    {
        var source = new ScenarioEntityCreationRequestSource(maxRequestsPerTick: 500);
        for (int i = 0; i < 600; i++)
            source.Enqueue(MakeRequest());

        int firstDrainCount = 0;
        source.ProcessRequests(_ => firstDrainCount++);
        Assert.Equal(500, firstDrainCount);

        int secondDrainCount = 0;
        source.ProcessRequests(_ => secondDrainCount++);
        Assert.Equal(100, secondDrainCount);

        // Now empty.
        int thirdDrainCount = 0;
        source.ProcessRequests(_ => thirdDrainCount++);
        Assert.Equal(0, thirdDrainCount);
    }

    /// <summary>
    /// C001 success condition 3: calling ProcessRequests on an empty source
    /// is a no-op (handler never called, no exception).
    /// </summary>
    [Fact]
    public void ScenarioSource_EmptyQueue_NoOp()
    {
        var source = new ScenarioEntityCreationRequestSource();
        int callCount = 0;

        var ex = Record.Exception(() => source.ProcessRequests(_ => callCount++));

        Assert.Null(ex);
        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// C001 success condition 4: concurrent enqueueing from 4 tasks and
    /// concurrent draining from a 5th task -- total processed == 1000,
    /// no InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task ScenarioSource_ConcurrentSafety_1000Items()
    {
        var source = new ScenarioEntityCreationRequestSource(maxRequestsPerTick: 10_000);
        int processedCount = 0;
        var drainDone = new ManualResetEventSlim(false);

        // Start 4 writer tasks.
        var writerTasks = new Task[4];
        for (int w = 0; w < 4; w++)
        {
            writerTasks[w] = Task.Run(() =>
            {
                for (int i = 0; i < 250; i++)
                    source.Enqueue(MakeRequest());
            });
        }

        // Start 1 reader task that drains in a loop until signalled.
        var readerTask = Task.Run(() =>
        {
            while (!drainDone.IsSet || Volatile.Read(ref processedCount) < 1000)
            {
                source.ProcessRequests(_ => Interlocked.Increment(ref processedCount));
                Thread.SpinWait(10);
            }
        });

        await Task.WhenAll(writerTasks);
        drainDone.Set();
        await readerTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Final drain sweep.
        source.ProcessRequests(_ => Interlocked.Increment(ref processedCount));

        Assert.Equal(1000, Volatile.Read(ref processedCount));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TASK-C002: CompositeEntityCreationRequestSource
    // ══════════════════════════════════════════════════════════════════════════

    // Helper: a simple list-backed source for composite tests.
    private sealed class ListSource : IEntityCreationRequestSource
    {
        private readonly List<EntityCreationRequest> _items = new();
        public void Add(EntityCreationRequest r) => _items.Add(r);
        public void ProcessRequests(Action<EntityCreationRequest> handler)
        {
            foreach (var r in _items)
                handler(r);
            _items.Clear();
        }
    }

    // Helper: a source that throws.
    private sealed class ThrowingSource : IEntityCreationRequestSource
    {
        public void ProcessRequests(Action<EntityCreationRequest> handler)
            => throw new InvalidOperationException("deliberate test failure");
    }

    /// <summary>
    /// C002 success condition 1: two inner sources -- R1 from source A, R2+R3 from source B;
    /// composite yields R1, R2, R3 in order.
    /// </summary>
    [Fact]
    public void CompositeSource_BothSourcesDrained_InOrder()
    {
        var sourceA = new ListSource();
        var sourceB = new ListSource();

        var r1 = MakeRequest();
        var r2 = MakeRequest();
        var r3 = MakeRequest();

        sourceA.Add(r1);
        sourceB.Add(r2);
        sourceB.Add(r3);

        var composite = new CompositeEntityCreationRequestSource(
            new IEntityCreationRequestSource[] { sourceA, sourceB });

        var received = new List<EntityCreationRequest>();
        composite.ProcessRequests(received.Add);

        Assert.Equal(3, received.Count);
        Assert.Same(r1, received[0]);
        Assert.Same(r2, received[1]);
        Assert.Same(r3, received[2]);
    }

    /// <summary>
    /// C002 success condition 2: two empty sources -- handler never called, no exception.
    /// </summary>
    [Fact]
    public void CompositeSource_EmptySources_NoOp()
    {
        var composite = new CompositeEntityCreationRequestSource(
            new IEntityCreationRequestSource[] { new ListSource(), new ListSource() });

        int callCount = 0;
        var ex = Record.Exception(() => composite.ProcessRequests(_ => callCount++));

        Assert.Null(ex);
        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// C002 success condition 3: single inner source with 5 requests -- all 5 surfaces.
    /// </summary>
    [Fact]
    public void CompositeSource_SingleSource_Passthrough()
    {
        var inner = new ListSource();
        for (int i = 0; i < 5; i++)
            inner.Add(MakeRequest());

        var composite = new CompositeEntityCreationRequestSource(
            new IEntityCreationRequestSource[] { inner });

        int callCount = 0;
        composite.ProcessRequests(_ => callCount++);

        Assert.Equal(5, callCount);
    }

    /// <summary>
    /// C002 success condition 4: constructing with an empty list throws ArgumentException.
    /// </summary>
    [Fact]
    public void CompositeSource_EmptyListConstructor_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[0]));

        Assert.Contains("innerSources", ex.Message);
    }

    /// <summary>
    /// C002 extra: exception from an inner source propagates to the caller (not swallowed).
    /// </summary>
    [Fact]
    public void CompositeSource_InnerSourceThrows_PropagatesToCaller()
    {
        var composite = new CompositeEntityCreationRequestSource(
            new IEntityCreationRequestSource[] { new ThrowingSource() });

        Assert.Throws<InvalidOperationException>(() =>
            composite.ProcessRequests(_ => { }));
    }
}
