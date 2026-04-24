using System.Reflection;

namespace StructEdit.Core.Memory;

/// <summary>
/// Edit buffer for managed classes and records. Clones the object on construction via MemberwiseClone.
/// </summary>
internal sealed class ManagedObjectEditBuffer : IEditBuffer
{
    private object _clone;
    private bool _isDirty;

    private static readonly MethodInfo _memberwiseClone =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public ManagedObjectEditBuffer(Type componentType, object obj)
    {
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        _clone = _memberwiseClone.Invoke(obj, null)!;
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

    public IValueBinding CreateRootBinding() => new ManagedRootBinding(this);

    public object Box() => _clone;

    public void Dispose() { /* managed; nothing to free */ }

    // ---------- inner binding ----------

    private sealed class ManagedRootBinding : IValueBinding
    {
        private readonly ManagedObjectEditBuffer _buffer;

        internal ManagedRootBinding(ManagedObjectEditBuffer buffer) => _buffer = buffer;

        public Type ValueType => _buffer.ComponentType;

        public object? GetBoxed() => _buffer._clone;

        public void SetBoxed(object? value)
        {
            _buffer._clone = value ?? throw new ArgumentNullException(nameof(value));
            _buffer._isDirty = true;
        }

        public bool TryGetSpan(out Span<byte> bytes)
        {
            bytes = Span<byte>.Empty;
            return false;
        }
    }
}
