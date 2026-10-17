using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Core.Network;

namespace Hrot.Core.Tests;

/// <summary>
/// Unit tests for <see cref="BusEntityCreationRequestSource"/> (CE-3228; replaced TASK-C001's queue)
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
    // ⭐⭐ CE-3228 — BusEntityCreationRequestSource (replaces the TASK-C001 queue singleton)
    //    📄 docs/DESIGN_Entity_Creation_Unification.md §3.4d
    // ══════════════════════════════════════════════════════════════════════════

    private static Fdp.Core.FdpEventBus NewBus()
    {
        var bus = new Fdp.Core.FdpEventBus();
        bus.RegisterManaged<EntityCreationRequest>();
        return bus;
    }

    /// <summary>⭐ A request published this frame is read after the next swap, in publish order, exactly once.</summary>
    [Fact]
    public void BusSource_DrainsWhatWasPublished_LastFrame()
    {
        var bus    = NewBus();
        var source = new BusEntityCreationRequestSource(bus);
        var r1 = MakeRequest();
        var r2 = MakeRequest();
        var r3 = MakeRequest();
        bus.PublishManaged(r1);
        bus.PublishManaged(r2);
        bus.PublishManaged(r3);

        var beforeSwap = new List<EntityCreationRequest>();
        source.ProcessRequests(beforeSwap.Add);
        Assert.Empty(beforeSwap);   // not visible until the frame's swap

        bus.SwapBuffers();
        var received = new List<EntityCreationRequest>();
        source.ProcessRequests(received.Add);
        Assert.Equal(new[] { r1, r2, r3 }, received);

        bus.SwapBuffers();   // the next frame: nothing new was published
        var nextFrame = new List<EntityCreationRequest>();
        source.ProcessRequests(nextFrame.Add);
        Assert.Empty(nextFrame);
    }

    /// <summary>⭐ An empty bus is a no-op (handler never called, no exception).</summary>
    [Fact]
    public void BusSource_EmptyBus_NoOp()
    {
        var bus    = NewBus();
        var source = new BusEntityCreationRequestSource(bus);
        bus.SwapBuffers();
        int callCount = 0;

        var ex = Record.Exception(() => source.ProcessRequests(_ => callCount++));

        Assert.Null(ex);
        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// ⭐⭐ Under strict mode (<c>FdpConfig.EnforceExplicitEventRegistration</c>, set by every production host) registering a
    /// managed event compiles its serializer. The request carries an object list and a dictionary of tuples — this pins that the
    /// compile succeeds, or every host would throw at startup.
    /// </summary>
    [Fact]
    public void BusSource_TheRequestCompilesUnderStrictRegistration()
    {
        var ex = Record.Exception(() => Fdp.Core.FlightRecorder.FdpAutoSerializer.EnsureCompiled<EntityCreationRequest>());
        Assert.Null(ex);
    }

    /// <summary>⭐ Publishing is lock-safe: 4 writer threads × 250, all 1000 read after one swap.</summary>
    [Fact]
    public async Task BusSource_ConcurrentPublish_1000Items()
    {
        var bus    = NewBus();
        var source = new BusEntityCreationRequestSource(bus);

        var writerTasks = new Task[4];
        for (int w = 0; w < 4; w++)
            writerTasks[w] = Task.Run(() =>
            {
                for (int i = 0; i < 250; i++)
                    bus.PublishManaged(MakeRequest());
            });
        await Task.WhenAll(writerTasks);

        bus.SwapBuffers();
        int processedCount = 0;
        source.ProcessRequests(_ => processedCount++);

        Assert.Equal(1000, processedCount);
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
