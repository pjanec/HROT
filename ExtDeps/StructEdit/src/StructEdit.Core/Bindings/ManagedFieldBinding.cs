using System.Reflection;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Binding for a public CLR field on a managed class or boxed struct instance.
/// Re-reads the field on every <see cref="GetBoxed"/> call — never caches the value.
/// </summary>
internal sealed class ManagedFieldBinding : IValueBinding
{
    private readonly FieldInfo _field;
    private readonly object _owner;
    private readonly Action? _markDirty;

    public Type ValueType => _field.FieldType;

    public ManagedFieldBinding(FieldInfo field, object owner, Action? markDirty = null)
    {
        _field = field ?? throw new ArgumentNullException(nameof(field));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _markDirty = markDirty;
    }

    public object? GetBoxed() => _field.GetValue(_owner);

    public void SetBoxed(object? value)
    {
        _field.SetValue(_owner, value);
        _markDirty?.Invoke();
    }

    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}
