using StructEdit.Core.Memory;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Binding for a C# <c>fixed T[N]</c> buffer field inside a <see cref="NativeStructEditBuffer"/>.
/// Exposes individual elements via <see cref="NativeFieldBinding"/> at computed offsets.
/// </summary>
internal sealed class FixedBufferBinding : IContainerBinding
{
    private readonly NativeStructEditBuffer _buffer;
    private readonly int _baseOffset;
    private readonly int _elementSize;

    internal Type ElementType { get; }

    public int Count { get; }
    public bool CanResize => false;
    public Type ValueType => typeof(byte[]);

    public FixedBufferBinding(
        NativeStructEditBuffer buffer,
        int baseOffset,
        Type elementType,
        int elementSize,
        int count)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _baseOffset = baseOffset;
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        _elementSize = elementSize;
        Count = count;
    }

    public object? GetBoxed() => null; // fixed buffer is treated as a container, not a single value
    public void SetBoxed(object? value) => throw new NotSupportedException("Cannot set a fixed buffer directly.");

    public bool TryGetSpan(out Span<byte> bytes)
    {
        if (!_buffer.TryGetRootSpan(out var root)) { bytes = default; return false; }
        bytes = root.Slice(_baseOffset, Count * _elementSize);
        return true;
    }

    public IValueBinding GetElementBinding(int index)
        => new NativeFieldBinding(_buffer, _baseOffset + index * _elementSize, _elementSize, ElementType);

    public void Resize(int newCount) => throw new NotSupportedException("Fixed buffers cannot be resized.");
}
