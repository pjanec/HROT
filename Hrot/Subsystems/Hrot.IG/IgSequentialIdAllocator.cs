using System.Threading;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.IG;

/// <summary>
/// Minimal thread-safe sequential ID allocator for use inside the IG process.
///
/// IG is a ghost-only (read-only) node — it never creates entities authoritatively.
/// <see cref="NetworkSpawningSystem"/> requires a non-null <see cref="INetworkIdAllocator"/>
/// even for pure ghost nodes, so this class satisfies the contract without connecting
/// to an external ID service.
///
/// IDs produced here are never transmitted to the network; they are local placeholders
/// in the rare case where the spawning system requests one internally.
/// </summary>
internal sealed class IgSequentialIdAllocator : INetworkIdAllocator, IRestorableIdAllocator
{
    private long _nextId = 1;

    /// <inheritdoc/>
    public long AllocateId() => Interlocked.Increment(ref _nextId);

    /// <inheritdoc/>
    public void Reset(long startId = 0) => Interlocked.Exchange(ref _nextId, startId);

    /// <inheritdoc/>
    /// <remarks>⭐ Scalar: the position is the counter. Pre-increment, so this is the last id issued.</remarks>
    public object? CaptureIssuingPosition() => Interlocked.Read(ref _nextId);

    /// <inheritdoc/>
    public void RestoreIssuingPosition(object snapshot)
    {
        if (snapshot is long v) Interlocked.Exchange(ref _nextId, v);
    }

    /// <inheritdoc/>
    public void Dispose() { /* Nothing to release. */ }
}
