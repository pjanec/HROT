using System.Runtime.InteropServices;

namespace StructEdit.Core.Memory;

/// <summary>
/// Edit buffer for unmanaged blittable structs. Uses <see cref="NativeMemory"/> for allocation.
/// </summary>
internal sealed unsafe class NativeStructEditBuffer : IEditBuffer
{
    private readonly IRuntimeTypeOps _ops;
    private readonly int _size;
    private void* _memory;
    private bool _disposed;
    private bool _isDirty;

    public NativeStructEditBuffer(Type componentType, object boxedStruct, IRuntimeTypeOps ops)
    {
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _size = ops.SizeOf;
        _memory = NativeMemory.Alloc((nuint)_size);
        _ops.CopyObjectToNative(boxedStruct, _memory);
    }

    public Type ComponentType { get; }
    public bool IsNative => true;
    public bool IsDirty => _isDirty;

    public void MarkDirty() => _isDirty = true;

    public bool TryGetRootSpan(out Span<byte> bytes)
    {
        if (_disposed)
        {
            bytes = Span<byte>.Empty;
            return false;
        }
        bytes = new Span<byte>(_memory, _size);
        return true;
    }

    public IValueBinding CreateRootBinding() => new NativeRootBinding(this, _ops);

    public object Box()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _ops.BoxFromNative(_memory);
    }

    public void Dispose()
    {
        if (_disposed) return;
        NativeMemory.Free(_memory);
        _memory = null;
        _disposed = true;
    }

    // ---------- inner binding ----------

    private sealed class NativeRootBinding : IValueBinding
    {
        private readonly NativeStructEditBuffer _buffer;
        private readonly IRuntimeTypeOps _ops;

        internal NativeRootBinding(NativeStructEditBuffer buffer, IRuntimeTypeOps ops)
        {
            _buffer = buffer;
            _ops = ops;
        }

        public Type ValueType => _buffer.ComponentType;

        public object? GetBoxed() => _buffer.Box();

        public void SetBoxed(object? value)
        {
            ObjectDisposedException.ThrowIf(_buffer._disposed, _buffer);
            _ops.CopyObjectToNative(value!, _buffer._memory);
            _buffer._isDirty = true;
        }

        public bool TryGetSpan(out Span<byte> bytes)
            => _buffer.TryGetRootSpan(out bytes);
    }
}
