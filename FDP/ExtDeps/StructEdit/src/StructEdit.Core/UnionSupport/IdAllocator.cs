namespace StructEdit.Core.UnionSupport;

/// <summary>
/// Mutable integer counter used to allocate sequential <see cref="EditNodeId"/> values
/// across a document build pass, including sub-trees built inside <see cref="BufferViewRequest"/>.
/// </summary>
internal sealed class IdAllocator
{
    private int _current;

    internal IdAllocator(int startFrom) => _current = startFrom;

    /// <summary>Allocates and returns the next ID value.</summary>
    public int Next() => ++_current;

    /// <summary>Current (last allocated) value.</summary>
    public int Current => _current;
}
