using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Fdp.Core;

/// <summary>
/// Structural recognition of the canonical fixed-capacity list WRAPPER pattern shared by the
/// Fixed Collections homes (Q#21-A1): a plain struct whose instance fields are EXACTLY one
/// <c>int Count</c> plus one buffer field whose type carries <c>[InlineArray(N)]</c> (field
/// order irrelevant). The element type is the buffer's single backing field's type; it must be
/// a value type and must not itself be a wrapper (no nested lists — v1 rule).
/// <para>
/// This is THE single definition of the shape — the editor classifier
/// (<c>BlackboardFieldClassifier</c>), the JSON converter
/// (<c>FixedListJsonConverterFactory</c>), and the inspector view provider all delegate here,
/// so the pattern cannot drift between consumers.
/// </para>
/// </summary>
public static class FixedListShape
{
    /// <summary>Simple form — element type + capacity only.</summary>
    public static bool TryGet(Type t, out Type elementType, out int capacity)
        => TryGet(t, out elementType, out _, out capacity, out _, out _);

    /// <summary>
    /// Full form — also returns the buffer type and the two <see cref="FieldInfo"/>s, for
    /// consumers that read/write the wrapper by reflection (the JSON converter).
    /// </summary>
    public static bool TryGet(
        Type t,
        out Type elementType,
        out Type bufferType,
        out int capacity,
        out FieldInfo countField,
        out FieldInfo bufferField)
    {
        elementType = typeof(void);
        bufferType  = typeof(void);
        capacity    = 0;
        countField  = null!;
        bufferField = null!;

        if (!t.IsValueType || t.IsEnum || t.IsPrimitive) return false;

        var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fields.Length != 2) return false;

        FieldInfo? count = null, buffer = null;
        foreach (var f in fields)
        {
            if (f.FieldType == typeof(int) && f.Name == "Count") count = f;
            else if (f.FieldType.IsValueType
                     && f.FieldType.IsDefined(typeof(InlineArrayAttribute), inherit: false))
                buffer = f;
        }
        if (count is null || buffer is null) return false;

        var bufType = buffer.FieldType;
        var attr = (InlineArrayAttribute)bufType
            .GetCustomAttributes(typeof(InlineArrayAttribute), inherit: false)[0];
        var backing = bufType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (backing.Length != 1) return false;

        var elem = backing[0].FieldType;
        if (!elem.IsValueType) return false;
        if (TryGet(elem, out _, out _)) return false;   // no nested lists (v1)
        if (attr.Length <= 0) return false;

        elementType = elem;
        bufferType  = bufType;
        capacity    = attr.Length;
        countField  = count;
        bufferField = buffer;
        return true;
    }
}
