namespace StructEdit.Core.UnionSupport;

/// <summary>
/// The result produced by <see cref="IBufferViewProvider.CreateView"/>.
/// <see cref="Node"/> replaces the raw <see cref="EditNodeKind.FixedBuffer"/> node
/// in the document tree.
/// </summary>
public sealed class BufferViewResult
{
    /// <summary>Display name for the view (e.g. "BallisticPayload").</summary>
    public required string ViewName { get; init; }

    /// <summary>The CLR type projected over the buffer bytes.</summary>
    public required Type ViewType { get; init; }

    /// <summary>
    /// The replacement <see cref="EditNode"/> (of kind <see cref="EditNodeKind.BufferView"/>)
    /// with child nodes bound to the projected type's fields.
    /// </summary>
    public required EditNode Node { get; init; }
}
