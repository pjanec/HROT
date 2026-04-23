using System;
using System.Collections.Concurrent;

namespace Hrot.Core.Network;

/// <summary>
/// Thread-safe, in-memory <see cref="IEntityCreationRequestSource"/> backed by a
/// <see cref="ConcurrentQueue{T}"/>.
///
/// <para><b>Threading:</b> <see cref="Enqueue"/> may be called from any thread
/// (e.g. the orchestration/load-handler thread). <see cref="ProcessRequests"/> is
/// called from the ECS tick thread by <c>CreateEntityRequestSystem</c>.</para>
///
/// <para><b>Drain cap:</b> <see cref="ProcessRequests"/> drains at most
/// <c>maxRequestsPerTick</c> items per call to prevent tick overrun.</para>
/// </summary>
public sealed class ScenarioEntityCreationRequestSource : IEntityCreationRequestSource
{
    private readonly int _maxRequestsPerTick;
    private readonly ConcurrentQueue<EntityCreationRequest> _queue = new();

    /// <param name="maxRequestsPerTick">
    /// Maximum number of requests drained per <see cref="ProcessRequests"/> call.
    /// Defaults to <c>500</c> (matching <c>CreateEntityRequestSystem.MaxRequestsPerTick</c>).
    /// </param>
    public ScenarioEntityCreationRequestSource(int maxRequestsPerTick = 500)
    {
        if (maxRequestsPerTick <= 0)
            throw new ArgumentException("maxRequestsPerTick must be positive.", nameof(maxRequestsPerTick));
        _maxRequestsPerTick = maxRequestsPerTick;
    }

    /// <summary>
    /// Enqueues a request for processing on the next ECS tick.
    /// Thread-safe: may be called from the orchestration thread.
    /// </summary>
    public void Enqueue(EntityCreationRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        _queue.Enqueue(request);
    }

    /// <summary>
    /// Returns <see langword="true"/> when no requests are pending in the queue.
    /// Thread-safe.
    /// </summary>
    public bool IsEmpty => _queue.IsEmpty;

    /// <inheritdoc/>
    public void ProcessRequests(Action<EntityCreationRequest> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        for (int i = 0; i < _maxRequestsPerTick; i++)
        {
            if (!_queue.TryDequeue(out var request))
                break;
            handler(request);
        }
    }
}
