using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97a</c>) — the one-field wrapper that makes a SCALAR variable editable.</b>
///
/// <para>🔴🔴 <b>The defect it exists for</b> *(<c>BP-356</c>, measured by Batch 96)*.
/// <c>ReflectionEditDocumentBuilder.CreateLeafBinding</c> opens
/// <c>if (fi == null &amp;&amp; pi == null) return null;</c> — <b>a binding needs a MEMBER</b> — and a
/// document's ROOT has none. ⇒ a DTO variable's root is a <c>Struct</c> whose CHILDREN are bound and
/// editing works; ⛔ a <b>scalar</b> variable IS the leaf, its <c>Binding</c> is <c>null</c>, and
/// <c>ComponentEditDrawer.DrawLeafNode</c> ends <c>node.Binding?.SetBoxed(value)</c> — ⛔⛔ <b>a
/// null-conditional that silently discards the designer's typing</b>, so <c>Commit()</c> could only
/// ever return the seed.</para>
///
/// <para>⭐⭐ <b>The user chose this shape</b> — <i>"maybe a special generic struct with a single field
/// passed to StructEdit"</i> — and Batch 96 had independently costed it as the cheaper alternative.
/// ⛔ <b><c>StructEdit</c> is NOT changed</b>: it is <c>FDP/ExtDeps</c> with its own suite, and a root
/// binding there would touch every scalar-rooted edit session in the editor.</para>
///
/// <para>⭐ <b>Why it works, verified against the sources before building</b>: <c>Value</c> is a
/// <b>public FIELD</b>, so <c>DetermineKind</c> classifies <c>ScalarEditBox&lt;T&gt;</c> as
/// <c>Struct</c> — a container — and its one child reaches <c>CreateLeafBinding</c> with
/// <c>fi != null</c> ⇒ <b>BOUND</b>. ⚠ No registration and no codegen: <c>RuntimeTypeOpsFactory.Get</c>
/// is <c>MakeGenericType</c> + a static read, and for a non-blittable <c>T</c> *(e.g. <c>string</c>)*
/// the classifier routes to <c>BoxedStructEditBuffer</c> and never reaches it at all.</para>
///
/// <para>⛔⛔ <b>The wrapper NEVER escapes the edit session.</b> The JSON written to a declaration and
/// the bytes written to a live blackboard are the <b>SCALAR</b> — <see cref="Unwrap"/> is the one
/// place that guarantees it, and it is asserted on both arms.</para>
/// </summary>
/// <typeparam name="T">The variable's own type — a leaf kind per <see cref="ScalarEditBox"/>.</typeparam>
public struct ScalarEditBox<T>
{
    /// <summary>
    /// ⭐ The variable's value. ⛔ <b>A FIELD, not a property</b> — <c>CreateLeafBinding</c> binds
    /// <c>FieldInfo</c> or <c>PropertyInfo</c>, and a field is what the native buffer path can offset
    /// into. ⚠ The name is what the designer reads as the row label.
    /// </summary>
    public T Value;

    public ScalarEditBox(T value) => Value = value;
}

/// <summary>
/// ⭐⭐ <b>Whether a variable's type needs <see cref="ScalarEditBox{T}"/>, and the boxing itself.</b>
///
/// <para>⭐⭐⭐ <b>The rule is <c>DetermineKind</c>'s LEAF KINDS</b> — <c>Boolean</c> · <c>String</c> ·
/// <c>Guid</c> · <c>DateTime</c> · <c>Enum</c> · <c>Scalar</c> — ⛔ <b>not a hand-written list of "int
/// and float"</b> that the next primitive falls off. ⚠ <c>DetermineKind</c> is <b>private</b> to
/// <c>StructEdit.Reflection</c>, so the predicate below MIRRORS it — and
/// <c>TheScalarBoxAgreesWithTheBuilderTests</c> pins the mirror to the builder's ACTUAL behaviour over
/// a type corpus, ⭐ so a divergence fails rather than silently un-editing a type.</para>
/// </summary>
public static class ScalarEditBox
{
    /// <summary>⭐ The numeric primitives <c>DetermineKind</c> calls <c>Scalar</c>.</summary>
    private static readonly HashSet<Type> Numeric = new()
    {
        typeof(int),  typeof(uint),   typeof(long),  typeof(ulong),
        typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
        typeof(float), typeof(double), typeof(decimal),
    };

    /// <summary>
    /// ⭐⭐ <b>True when a session opened over <paramref name="type"/> would produce an UNBOUND root</b>,
    /// i.e. the type is a leaf and therefore has no member to bind.
    /// ⛔ Mirrors <c>ReflectionEditDocumentBuilder.DetermineKind</c>'s leaf arms, in its own order.
    /// </summary>
    public static bool NeedsBoxing(Type? type)
        => type is not null
        && (type == typeof(bool)
         || type == typeof(string)
         || type == typeof(Guid)
         || type == typeof(DateTime)
         || type.IsEnum
         || Numeric.Contains(type));

    /// <summary>
    /// ⭐ The type a session should actually be opened over — <c>ScalarEditBox&lt;T&gt;</c> for a leaf,
    /// and <paramref name="fieldType"/> itself for anything with members of its own.
    /// </summary>
    public static Type EditTypeFor(Type fieldType)
        => NeedsBoxing(fieldType)
            ? typeof(ScalarEditBox<>).MakeGenericType(fieldType)
            : fieldType;

    /// <summary>⭐ Wraps <paramref name="value"/> when its type is a leaf; otherwise returns it as-is.</summary>
    public static object Wrap(object value, Type fieldType)
        => NeedsBoxing(fieldType)
            ? Activator.CreateInstance(EditTypeFor(fieldType), value)!
            : value;

    /// <summary>
    /// ⭐⭐ <b>Is <paramref name="type"/> one of MY wrappers?</b> ⭐ Asked by the DRAW, which must show a
    /// scalar as ONE ROW carrying the variable's own name — ⛔ not as a collapsible
    /// <c>ScalarEditBox`1</c> whose single child reads <c>Value</c>.
    ///
    /// <para>⭐ It lives here, beside <see cref="EditTypeFor"/>/<see cref="Wrap"/>/<see cref="Unwrap"/>,
    /// because the wrapper's shape is this type's business — ⛔ an <c>IsGenericType</c> test written at
    /// a call site is a second place that would have to learn about the box.</para>
    /// </summary>
    public static bool IsWrapper(Type? type)
        => type is { IsGenericType: true }
           && type.GetGenericTypeDefinition() == typeof(ScalarEditBox<>);

    /// <summary>
    /// ⭐⭐⭐ <b>Unwraps a committed value back to the variable's OWN type.</b>
    /// ⛔ <b>Every commit path must go through this</b> — the JSON on a declaration and the bytes on a
    /// live blackboard are the SCALAR, ⛔ never <c>{"Value":7}</c> and never the wrapper's layout.
    /// ⚠ Fails OPEN: a value that is not a box for this type is returned untouched, so a caller that
    /// never wrapped is unharmed.
    /// </summary>
    public static object Unwrap(object committed, Type fieldType)
    {
        if (committed is null || !NeedsBoxing(fieldType)) return committed!;

        var boxType = EditTypeFor(fieldType);
        if (!boxType.IsInstanceOfType(committed)) return committed;

        return boxType.GetField(nameof(ScalarEditBox<int>.Value))!.GetValue(committed)!;
    }
}
