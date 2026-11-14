using System.Collections;
using System.Text.Json;
using StructEdit.Core.Declared;

namespace StructEdit.Core.Defaults;

/// <summary>
/// ⭐ S7 (📄 docs/DESIGN_Struct_Editor.md §6 S7, CE-3381) — where a node's DEFAULT comes from, so the table can draw grey = default, amber bar + ↺ = changed.
/// "The host's Fill source": a declared document's declarations, a C# type's fresh instance, a behaviour asset's defaults, a preset's base values.
/// <para>⭐ Read on a CHANGE (a set, a rebuild), never per frame — the table caches the answer per node (§10.2).</para>
/// </summary>
public interface IFieldDefaults
{
    /// <summary>The default of <paramref name="node"/>; false when none is known (the row then shows no changed mark).</summary>
    bool TryGetDefault(EditNode node, out object? value);
}

/// <summary>The defaults of a declared document: each top-level row's declaration default (<see cref="JsonMemberBinding.DefaultValue"/>).</summary>
public sealed class DeclaredDefaults : IFieldDefaults
{
    public static readonly DeclaredDefaults Instance = new();

    public bool TryGetDefault(EditNode node, out object? value)
    {
        if (node.Binding is JsonMemberBinding b) { value = b.DefaultValue(); return true; }
        value = null;
        return false;
    }
}

/// <summary>
/// The defaults of a C# document: the values at the SAME JSON paths in a second document built over a defaults object (a fresh <c>new T()</c>, an asset's defaults) — by path,
/// so a row of a list item past the defaults' count has none.
/// </summary>
public sealed class DocumentDefaults : IFieldDefaults
{
    private readonly Dictionary<string, IValueBinding> _byPath = new(StringComparer.Ordinal);

    /// <param name="defaults">A document over the defaults object (keep its session open while this is used — the bindings read it).</param>
    public DocumentDefaults(EditDocument defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        Index(defaults.Root);
    }

    private void Index(EditNode node)
    {
        if (node.Binding is not null) _byPath[node.JsonPath] = node.Binding;
        foreach (var c in node.Children) Index(c);
    }

    public bool TryGetDefault(EditNode node, out object? value)
    {
        if (_byPath.TryGetValue(node.JsonPath, out var b)) { value = b.GetBoxed(); return true; }
        value = null;
        return false;
    }
}

/// <summary>Value equality for "changed": <c>Equals</c>, else (lists, classes) the same JSON text — run on a change only.</summary>
public static class FieldValues
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> hold the same value.</summary>
    public static bool Same(object? a, object? b, JsonSerializerOptions? options = null)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return IsEmpty(a) && IsEmpty(b);
        if (a.Equals(b)) return true;
        if (a.GetType() != b.GetType()) return false;
        if (a.GetType().IsPrimitive || a is string || a is Enum) return false;
        try { return JsonSerializer.Serialize(a, a.GetType(), options ?? Options) == JsonSerializer.Serialize(b, b.GetType(), options ?? Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException) { return false; }
    }

    private static bool IsEmpty(object? v) => v is null || (v is string s && s.Length == 0) || (v is ICollection c && c.Count == 0);

    /// <summary>True when <paramref name="node"/>'s value differs from its default under <paramref name="defaults"/> (false when no default is known).</summary>
    public static bool IsChanged(EditNode node, IFieldDefaults defaults)
        => node.Binding is { } b && defaults.TryGetDefault(node, out var d) && !Same(b.GetBoxed(), d);
}
