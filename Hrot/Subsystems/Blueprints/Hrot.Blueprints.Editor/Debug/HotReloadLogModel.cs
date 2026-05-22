namespace Hrot.Blueprints.Editor.Debug;

/// <summary>Ring-buffer model for hot reload log entries. Max 1000 entries.</summary>
public sealed class HotReloadLogModel
{
    public const int MaxEntries = 1000;
    private readonly Queue<ReloadLogEntry> _entries = new(MaxEntries + 1);

    public IReadOnlyCollection<ReloadLogEntry> Entries => _entries;
    public int Count => _entries.Count;

    public void AddEntry(ReloadLogEntry entry)
    {
        _entries.Enqueue(entry);
        if (_entries.Count > MaxEntries)
            _entries.Dequeue();
    }

    public void Clear() => _entries.Clear();
}
