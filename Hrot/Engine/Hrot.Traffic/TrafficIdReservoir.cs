using System.Collections.Concurrent;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 §12 N2 (R-4054) — the network ids a SERVING node's SUMO hands its new cars, allocated on the MAIN thread and taken on the
/// stepping thread.
/// </summary>
/// <remarks>
/// ⛔ The cluster's allocator is not thread-safe (<c>DdsIdAllocator.AllocateId</c> dequeues a plain <c>Queue</c> and reads its DDS
/// reader), and the node's entity-creation pack calls it on the main thread — so SUMO, which spawns on the stepping thread, never
/// calls it. The main thread keeps this reserve topped up each frame (<see cref="TopUp"/>); the stepping thread only dequeues.
/// </remarks>
public sealed class TrafficIdReservoir
{
    /// <summary>How long the stepping thread waits for a top-up before it gives up on a car.</summary>
    public static readonly TimeSpan TakeTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<long> _allocate;
    private readonly ConcurrentQueue<long> _ids = new();

    public TrafficIdReservoir(Func<long> allocate) => _allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));

    /// <summary>Ids ready to hand out.</summary>
    public int Count => _ids.Count;

    /// <summary>Allocates until <paramref name="highWater"/> ids are ready. ⛔ Main thread only (it calls the allocator).</summary>
    public void TopUp(int highWater)
    {
        while (_ids.Count < highWater) _ids.Enqueue(_allocate());
    }

    /// <summary>The next id. ⭐ The stepping thread's only call; waits briefly for the main thread when the reserve ran dry.</summary>
    public long Take()
    {
        if (_ids.TryDequeue(out long id)) return id;
        var spin = new SpinWait();
        var until = DateTime.UtcNow + TakeTimeout;
        while (DateTime.UtcNow < until)
        {
            spin.SpinOnce();
            if (_ids.TryDequeue(out id)) return id;
        }
        throw new InvalidOperationException(
            $"[Traffic] no network id for a new SUMO car within {TakeTimeout.TotalSeconds:0}s: the main thread did not top the reserve up.");
    }
}
