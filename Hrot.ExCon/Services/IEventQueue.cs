namespace Hrot.ExCon.Services;

/// <summary>
/// Thread-safe pull queue for DDS event samples.
///
/// <para>In production this is backed by a thin wrapper around a
/// <c>CycloneDDS.Runtime.DdsReader&lt;T&gt;</c> whose callback enqueues to a
/// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>.  In unit
/// tests it is fulfilled by a plain
/// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>-backed stub
/// that lets tests push events directly without a live DDS participant.</para>
/// </summary>
public interface IEventQueue<T>
{
    /// <summary>
    /// Attempts to dequeue the next pending event.
    /// Returns <c>false</c> when the queue is empty.
    /// </summary>
    bool TryDequeue(out T item);

    /// <summary>
    /// Enqueues an event directly.  Used by production ingress adapters and
    /// test stubs alike.
    /// </summary>
    void Enqueue(T item);
}
