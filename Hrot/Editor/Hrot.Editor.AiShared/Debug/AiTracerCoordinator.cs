namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Reference-counted asset observation tracker.
/// When multiple observers request the same asset, the effective TraceLevel
/// is the bitwise OR (union) of all requested levels.
/// On refcount reaching zero, EndObservingAssetImpl is called.
/// Subsystem coordinators derive and override BeginObservingAssetImpl/EndObservingAssetImpl
/// to talk to their kernel.
/// </summary>
public class AiTracerCoordinator
{
    // Key: assetId. Value: (refcount, effective TraceLevel)
    private readonly Dictionary<Guid, (int RefCount, TraceLevel Level)> _observed = new();

    /// <summary>Increments refcount for the asset. Calls BeginObservingAssetImpl on first call.</summary>
    public void AddObserver(Guid assetId, TraceLevel level)
    {
        if (_observed.TryGetValue(assetId, out var existing))
        {
            _observed[assetId] = (existing.RefCount + 1, existing.Level | level);
        }
        else
        {
            _observed[assetId] = (1, level);
            BeginObservingAssetImpl(assetId, level);
        }
    }

    /// <summary>Decrements refcount. Calls EndObservingAssetImpl on reaching zero.</summary>
    public void RemoveObserver(Guid assetId)
    {
        if (!_observed.TryGetValue(assetId, out var existing)) return;

        if (existing.RefCount <= 1)
        {
            _observed.Remove(assetId);
            EndObservingAssetImpl(assetId);
        }
        else
        {
            _observed[assetId] = (existing.RefCount - 1, existing.Level);
        }
    }

    /// <summary>Effective TraceLevel for the asset (union of all observer levels). Zero if not observed.</summary>
    public TraceLevel GetEffectiveLevel(Guid assetId) =>
        _observed.TryGetValue(assetId, out var v) ? v.Level : TraceLevel.None;

    /// <summary>True if at least one observer is watching this asset.</summary>
    public bool IsObserving(Guid assetId) => _observed.ContainsKey(assetId);

    /// <summary>
    /// Called on first observer for an asset.
    /// Override in subsystem-specific subclasses to set DebugState.Flags on matching entities.
    /// Default: no-op (test-friendly).
    /// </summary>
    protected virtual void BeginObservingAssetImpl(Guid assetId, TraceLevel level) { }

    /// <summary>Called when refcount reaches zero. Override to clear DebugState.Flags.</summary>
    protected virtual void EndObservingAssetImpl(Guid assetId) { }
}
