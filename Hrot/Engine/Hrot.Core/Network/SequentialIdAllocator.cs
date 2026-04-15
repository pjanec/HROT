using System.Threading;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.Core.Network;

/// <summary>
/// Thread-safe sequential ID allocator for offline/headless environments.
/// Returns monotonically increasing IDs from a local counter without any network round-trip.
/// Used by offline and mock <see cref="INetworkFactory"/> implementations when no DDS
/// participant is available.
/// </summary>
public sealed class SequentialIdAllocator : INetworkIdAllocator
{
    private long _next = 1;

    /// <inheritdoc/>
    public long AllocateId() => Interlocked.Increment(ref _next);

    /// <inheritdoc/>
    public void Reset(long startId = 0) => Interlocked.Exchange(ref _next, startId);

    /// <inheritdoc/>
    public void Dispose() { /* Nothing to release. */ }
}
