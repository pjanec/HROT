namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Per-entity execution history ring-buffer.
/// Pre-allocated; Record() is zero-allocation on write.
/// Entries are stored in insertion order; GetRecent returns chronological (oldest first) order.
/// </summary>
internal sealed class ExecutionHistory
{
    private readonly NodeHistoryEntry[] _buffer;
    private int _head;
    private int _count;

    public ExecutionHistory(int capacity = 256)
    {
        _buffer = new NodeHistoryEntry[capacity];
    }

    /// <summary>Record an entry. Does not allocate heap memory (writes reference into pre-allocated array).</summary>
    public void Record(NodeHistoryEntry entry)
    {
        _buffer[_head % _buffer.Length] = entry;
        _head++;
        if (_count < _buffer.Length) _count++;
    }

    /// <summary>
    /// Returns up to <paramref name="maxCount"/> recent entries in chronological order (oldest first).
    /// </summary>
    public IReadOnlyList<NodeHistoryEntry> GetRecent(int maxCount)
    {
        var take = Math.Min(maxCount, _count);
        var result = new NodeHistoryEntry[take];
        var start = _head - take;
        for (int i = 0; i < take; i++)
            result[i] = _buffer[(start + i + _buffer.Length * 2) % _buffer.Length];
        return result;
    }
}
