namespace Hrot.Blueprints.Editor;

public sealed class DirtyTracker
{
    private readonly HashSet<Guid> _dirty = new();

    public void MarkDirty(Guid assetId)  => _dirty.Add(assetId);
    public void MarkClean(Guid assetId)  => _dirty.Remove(assetId);
    public bool IsDirty(Guid assetId)    => _dirty.Contains(assetId);
    public IReadOnlySet<Guid> DirtyAssets => _dirty;
}
