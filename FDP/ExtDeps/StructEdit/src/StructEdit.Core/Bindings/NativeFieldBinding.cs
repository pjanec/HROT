using StructEdit.Core.Memory;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Binding for a scalar/struct field at a fixed byte offset inside a <see cref="NativeStructEditBuffer"/>.
/// Uses <see cref="FieldReadWriterCache"/> — no reflection during read/write.
/// </summary>
internal sealed class NativeFieldBinding : IValueBinding
{
    private readonly NativeStructEditBuffer _buffer;
    private readonly int _offset;
    private readonly int _fieldSize;
    private readonly IFieldReadWriter _rw;

    public Type ValueType { get; }

    public NativeFieldBinding(NativeStructEditBuffer buffer, int offset, int fieldSize, Type valueType)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _offset = offset;
        _fieldSize = fieldSize;
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        _rw = FieldReadWriterCache.Get(valueType);
    }

    public object? GetBoxed()
    {
        if (!_buffer.TryGetRootSpan(out var root))
            throw new ObjectDisposedException(nameof(NativeStructEditBuffer));
        return _rw.Read(root.Slice(_offset, _fieldSize));
    }

    public void SetBoxed(object? value)
    {
        if (!_buffer.TryGetRootSpan(out var root))
            throw new ObjectDisposedException(nameof(NativeStructEditBuffer));
        _rw.Write(root.Slice(_offset, _fieldSize), value);
        _buffer.MarkDirty();
    }

    public bool TryGetSpan(out Span<byte> bytes)
    {
        if (!_buffer.TryGetRootSpan(out var root)) { bytes = default; return false; }
        bytes = root.Slice(_offset, _fieldSize);
        return true;
    }
}
