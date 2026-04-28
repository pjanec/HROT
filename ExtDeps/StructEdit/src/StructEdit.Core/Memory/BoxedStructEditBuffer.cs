using System.Text.Json;

namespace StructEdit.Core.Memory;

/// <summary>
/// Edit buffer for non-blittable structs. Stores a deep-cloned boxed copy of the struct
/// to isolate managed fields from the live component instance.
/// </summary>
internal sealed class BoxedStructEditBuffer : IEditBuffer
{
    private object _boxed;
    private bool _isDirty;
    private static readonly JsonSerializerOptions _cloneOptions = new()
    {
        IncludeFields = true
    };

    public BoxedStructEditBuffer(Type componentType, object boxedStruct)
    {
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        if (boxedStruct is null) throw new ArgumentNullException(nameof(boxedStruct));

        string json = JsonSerializer.Serialize(boxedStruct, componentType, _cloneOptions);
        _boxed = JsonSerializer.Deserialize(json, componentType, _cloneOptions)
            ?? throw new InvalidOperationException($"Failed to deep clone component of type {componentType}.");
    }

    public Type ComponentType { get; }
    public bool IsNative => false;
    public bool IsDirty => _isDirty;

    public void MarkDirty() => _isDirty = true;

    public bool TryGetRootSpan(out Span<byte> bytes)
    {
        bytes = Span<byte>.Empty;
        return false;
    }

    public IValueBinding CreateRootBinding() => new BoxedRootBinding(this);

    public object Box() => _boxed;

    public void Dispose() { /* nothing to free */ }

    // ---------- inner binding ----------

    private sealed class BoxedRootBinding : IValueBinding
    {
        private readonly BoxedStructEditBuffer _buffer;

        internal BoxedRootBinding(BoxedStructEditBuffer buffer) => _buffer = buffer;

        public Type ValueType => _buffer.ComponentType;

        public object? GetBoxed() => _buffer._boxed;

        public void SetBoxed(object? value)
        {
            _buffer._boxed = value ?? throw new ArgumentNullException(nameof(value));
            _buffer._isDirty = true;
        }

        public bool TryGetSpan(out Span<byte> bytes)
        {
            bytes = Span<byte>.Empty;
            return false;
        }
    }
}
