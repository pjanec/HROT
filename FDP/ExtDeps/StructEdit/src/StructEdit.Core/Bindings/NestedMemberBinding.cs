using System.Reflection;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Wraps an existing <see cref="IValueBinding"/> (e.g. an array element binding) and exposes
/// one public field or property of the parent value as a bindable leaf.
/// </summary>
/// <remarks>
/// When the parent holds a value type (<see cref="IValueBinding.ValueType"/> is a struct),
/// <see cref="SetBoxed"/> must write the mutated boxed struct back to the parent via
/// <see cref="IValueBinding.SetBoxed"/> — otherwise the mutation is lost due to C# copy-on-box
/// semantics.
/// </remarks>
internal sealed class NestedMemberBinding : IValueBinding
{
    private readonly MemberInfo _member;
    private readonly IValueBinding _parent;

    /// <inheritdoc/>
    public Type ValueType { get; }

    public NestedMemberBinding(MemberInfo member, IValueBinding parent)
    {
        _member = member ?? throw new ArgumentNullException(nameof(member));
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));

        ValueType = member switch
        {
            FieldInfo fi  => fi.FieldType,
            PropertyInfo pi => pi.PropertyType,
            _ => throw new ArgumentException(
                     $"Member must be FieldInfo or PropertyInfo, got {member.GetType().Name}.",
                     nameof(member)),
        };
    }

    /// <inheritdoc/>
    public object? GetBoxed()
    {
        var parentObj = _parent.GetBoxed();
        if (parentObj == null) return null;

        return _member switch
        {
            FieldInfo fi  => fi.GetValue(parentObj),
            PropertyInfo pi => pi.GetValue(parentObj),
            _ => null,
        };
    }

    /// <inheritdoc/>
    public void SetBoxed(object? value)
    {
        var parentObj = _parent.GetBoxed();
        if (parentObj == null) return;

        switch (_member)
        {
            case FieldInfo fi:
                fi.SetValue(parentObj, value);
                break;
            case PropertyInfo pi:
                pi.SetValue(parentObj, value);
                break;
        }

        // Value-type parents must be written back: mutating a boxed copy does not
        // propagate automatically due to C# copy-on-box semantics.
        if (_parent.ValueType.IsValueType)
            _parent.SetBoxed(parentObj);
    }

    /// <inheritdoc/>
    public bool TryGetSpan(out Span<byte> bytes)
    {
        bytes = default;
        return false;
    }
}
