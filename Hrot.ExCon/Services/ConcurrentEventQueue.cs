using System.Collections.Concurrent;

namespace Hrot.ExCon.Services;

/// <summary>
/// Default in-memory implementation of <see cref="IEventQueue{T}"/> backed
/// by a <see cref="ConcurrentQueue{T}"/>.
///
/// <para>Used in production to buffer DDS samples that arrive on background
/// DDS threads until the main application thread dequeues them in
/// <c>ExConLogic.Update</c>.  Also used directly in unit tests as a simple
/// controllable event source.</para>
/// </summary>
public sealed class ConcurrentEventQueue<T> : IEventQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();

    /// <inheritdoc/>
    public bool TryDequeue(out T item) => _queue.TryDequeue(out item!);

    /// <inheritdoc/>
    public void Enqueue(T item) => _queue.Enqueue(item);
}
