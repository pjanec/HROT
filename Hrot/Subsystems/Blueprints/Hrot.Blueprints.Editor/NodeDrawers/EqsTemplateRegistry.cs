namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Editor-side registry of known EQS template assets.
/// Populated at editor startup from the project's EQS template catalog.
/// Distinct from the runtime IEqsTemplateRegistry (which maps by uint blueprintId).
/// </summary>
public sealed class EqsTemplateRegistry
{
    private readonly List<EqsTemplateEntry> _entries = new();

    public void Register(EqsTemplateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    public IReadOnlyList<EqsTemplateEntry> EnumerateAll() => _entries;

    public EqsTemplateEntry? TryGet(Guid assetId)
        => _entries.Find(e => e.AssetId == assetId);
}
