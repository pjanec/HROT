using Fdp.Core;

namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Passive subscriber to tracer output. Multiple observers may be attached
/// per subsystem simultaneously. Does not control execution.
/// </summary>
public interface IAiTraceObserver
{
    /// <summary>
    /// Begins emitting trace records for all entities running this asset.
    /// Reference-counted internally so multiple observers can request the same asset.
    /// </summary>
    void BeginObservingAsset(Guid assetId, TraceLevel level);
    void EndObservingAsset(Guid assetId);

    /// <summary>Returns all entities currently running this asset.</summary>
    IReadOnlyList<Entity> GetActiveEntities(Guid assetId);
}
