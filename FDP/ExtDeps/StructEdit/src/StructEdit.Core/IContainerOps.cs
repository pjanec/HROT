using System.Text.Json;

namespace StructEdit.Core;

/// <summary>
/// ⭐ S4 (📄 docs/DESIGN_Struct_Editor.md §6 S4, §10.1 second sequence, CE-3379) — the list operations a container node offers beyond <see cref="IContainerBinding.Resize"/>:
/// insert · remove-at · move · duplicate · clear, and a new element that is never null (a type's default).
/// <para>⭐ A <see cref="Move"/> is IN PLACE: the count is unchanged and element bindings are index-based, so the document needs NO rebuild (measured, §10.5).
/// ⚠ Every other operation changes the count ⇒ the caller marks a structural change (<see cref="IEditSession.MarkStructuralChange"/>), as a remove does today.</para>
/// <para>A fixed-size container (inline / fixed buffers, B4) answers <see cref="CanInsertRemove"/> false and only moves; use <see cref="ContainerOps.Move"/> for any container.</para>
/// </summary>
public interface IContainerOps : IContainerBinding
{
    /// <summary>False for a fixed-size container: only <see cref="Move"/> works.</summary>
    bool CanInsertRemove { get; }

    /// <summary>Inserts <paramref name="value"/> at <paramref name="index"/> (0..Count). Count changes ⇒ structural.</summary>
    void Insert(int index, object? value);

    /// <summary>Removes the element at <paramref name="index"/>. Count changes ⇒ structural.</summary>
    void RemoveAt(int index);

    /// <summary>Moves the element at <paramref name="from"/> to <paramref name="to"/>, shifting the ones between — in place, no rebuild.</summary>
    void Move(int from, int to);

    /// <summary>Inserts a deep copy of the element at <paramref name="index"/> right after it. Count changes ⇒ structural.</summary>
    void Duplicate(int index);

    /// <summary>Removes every element. Count changes ⇒ structural.</summary>
    void Clear();

    /// <summary>A new element's value: the element type's default — never null for a value type, a string or a type with a parameterless constructor.</summary>
    object? NewElement();
}

/// <summary>The list operations that work on ANY container, and the helpers the implementations share (one implementation — S4).</summary>
public static class ContainerOps
{
    private static readonly JsonSerializerOptions CloneOptions = new() { IncludeFields = true };

    /// <summary>Moves an element in place through the element bindings — works for every container (managed lists and arrays, inline and fixed buffers). No rebuild.</summary>
    public static void Move(IContainerBinding container, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(container);
        int n = container.Count;
        if ((uint)from >= (uint)n) throw new ArgumentOutOfRangeException(nameof(from));
        if ((uint)to >= (uint)n) throw new ArgumentOutOfRangeException(nameof(to));
        if (from == to) return;
        var moving = container.GetElementBinding(from).GetBoxed();
        int step = from < to ? 1 : -1;
        for (int i = from; i != to; i += step)
            container.GetElementBinding(i).SetBoxed(container.GetElementBinding(i + step).GetBoxed());
        container.GetElementBinding(to).SetBoxed(moving);
    }

    /// <summary>The default value of an element type: a value type's zero, <c>""</c> for a string, a new instance when a public parameterless constructor exists, else null.</summary>
    public static object? DefaultOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type == typeof(string)) return "";
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsAbstract || type.IsInterface) return null;
        return type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
    }

    /// <summary>A deep copy of <paramref name="value"/> (a JSON round trip, fields included — the same copy the edit buffers make); a value the serialiser
    /// refuses is copied shallowly (a boxed value type is copied by boxing; a reference type by <c>MemberwiseClone</c>).</summary>
    public static object? Clone(object? value, Type type, JsonSerializerOptions? options = null)
    {
        if (value is null) return null;
        if (value is string || type.IsPrimitive || type.IsEnum) return value;
        var runtime = value.GetType();
        try
        {
            string json = JsonSerializer.Serialize(value, runtime, options ?? CloneOptions);
            return JsonSerializer.Deserialize(json, runtime, options ?? CloneOptions);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            if (runtime.IsValueType) return value is ICloneable c ? c.Clone() : CopyBoxed(value);
            return typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(value, null);
        }
    }

    private static object CopyBoxed(object boxed)
    {
        // a boxed value type: unbox-and-rebox through a fresh array slot copies it
        var arr = Array.CreateInstance(boxed.GetType(), 1);
        arr.SetValue(boxed, 0);
        return arr.GetValue(0)!;
    }
}
