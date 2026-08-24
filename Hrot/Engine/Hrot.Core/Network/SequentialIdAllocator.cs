using System.Threading;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.Core.Network;

/// <summary>
/// Thread-safe sequential ID allocator for offline/headless environments.
/// Returns monotonically increasing IDs from a local counter without any network round-trip.
/// Used by offline and mock <see cref="INetworkFactory"/> implementations when no DDS
/// participant is available.
/// </summary>
public sealed class SequentialIdAllocator : INetworkIdAllocator, IRestorableIdAllocator
{
    private long _next = 1;

    /// <inheritdoc/>
    public long AllocateId() => Interlocked.Increment(ref _next);

    /// <inheritdoc/>
    public void Reset(long startId = 0) => Interlocked.Exchange(ref _next, startId);

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐ The position IS the counter. ⚠ Note what it MEANS here: <c>AllocateId</c> pre-increments, so
    /// <c>_next</c> is the <b>last id issued</b> — ⛔ the opposite of the editor's nested allocator, which
    /// post-increments and holds the <b>next to issue</b>. 📌 <c>DESIGN_Deterministic_Network_Ids.md</c>
    /// §4b: that disagreement is why the contract is <i>"restore my position"</i> and not
    /// <i>"read the next id"</i> — both meanings satisfy it, neither name would be true of both.
    /// </remarks>
    public object? CaptureIssuingPosition() => Interlocked.Read(ref _next);

    /// <inheritdoc/>
    public void RestoreIssuingPosition(object snapshot)
    {
        if (snapshot is long v) Interlocked.Exchange(ref _next, v);
    }

    /// <inheritdoc/>
    public void Dispose() { /* Nothing to release. */ }
}
