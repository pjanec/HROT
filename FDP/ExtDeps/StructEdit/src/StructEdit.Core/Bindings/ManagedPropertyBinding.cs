using System.Reflection;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Binding for a CLR property on a managed class or record instance.
/// Uses <see cref="PropertyInfo.GetValue"/> / <see cref="PropertyInfo.SetValue"/>.
/// </summary>
internal sealed class ManagedPropertyBinding : IValueBinding
{
    private readonly PropertyInfo _property;
    private readonly object _owner;
    private readonly Action? _markDirty;

    public Type ValueType => _property.PropertyType;

    public ManagedPropertyBinding(PropertyInfo property, object owner, Action? markDirty = null)
    {
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _markDirty = markDirty;
    }

    public object? GetBoxed() => _property.GetValue(_owner);

    public void SetBoxed(object? value)
    {
        _property.SetValue(_owner, value);
        _markDirty?.Invoke();
    }

    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}
