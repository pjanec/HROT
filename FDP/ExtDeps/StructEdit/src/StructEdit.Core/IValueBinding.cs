namespace StructEdit.Core;

/// <summary>
/// Contract for reading and writing values on an edit node.
/// </summary>
public interface IValueBinding
{
    Type ValueType { get; }
    object? GetBoxed();
    void SetBoxed(object? value);
    bool TryGetSpan(out Span<byte> bytes);
}
