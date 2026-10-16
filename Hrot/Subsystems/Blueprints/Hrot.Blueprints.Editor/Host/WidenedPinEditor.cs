using NodeEditor.Core.Interfaces;
using NodeEditor.UI.MiniEditors;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// ⭐ CE-1006 — an inline pin editor for a numeric width the framework has no editor for (double, long, short, byte, the unsigned
/// widths): the int or float editor draws it, and the value is converted to that editor's type going in and back to the pin's own
/// CLR type coming out, so <see cref="BlueprintPinDefaultValue.FormatValue"/> persists it in the pin's width. The same proxy idea
/// <see cref="LiteralValueJson.EditorTypeId"/> uses for a Literal node; a value outside the proxy's range is clamped on display.
/// </summary>
internal sealed class WidenedPinEditor : IPinDefaultValueEditor
{
    private readonly IPinDefaultValueEditor _inner;
    private readonly Func<object?, object> _toInner;
    private readonly Func<object, object> _fromInner;

    private WidenedPinEditor(IPinDefaultValueEditor inner, Func<object?, object> toInner, Func<object, object> fromInner)
        => (_inner, _toInner, _fromInner) = (inner, toInner, fromInner);

    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        object? proxy = _toInner(value);
        bool changed = _inner.Draw(ref proxy, ctx, out committed);
        if (changed && proxy != null) value = _fromInner(proxy);
        return changed;
    }

    /// <summary>The pin type ids this covers, each with its proxy editor.</summary>
    public static IEnumerable<(string TypeId, IPinDefaultValueEditor Editor)> All()
    {
        yield return ("System.Double", new WidenedPinEditor(new FloatPinEditor(), v => (float)AsDouble(v), f => (double)(float)f));
        yield return ("System.Int64",  Int(i => (long)i));
        yield return ("System.Int16",  Int(i => (short)Math.Clamp(i, short.MinValue, short.MaxValue)));
        yield return ("System.SByte",  Int(i => (sbyte)Math.Clamp(i, sbyte.MinValue, sbyte.MaxValue)));
        yield return ("System.Byte",   Int(i => (byte)Math.Clamp(i, byte.MinValue, byte.MaxValue)));
        yield return ("System.UInt16", Int(i => (ushort)Math.Clamp(i, ushort.MinValue, ushort.MaxValue)));
        yield return ("System.UInt32", Int(i => (uint)Math.Max(0, i)));
        yield return ("System.UInt64", Int(i => (ulong)Math.Max(0, i)));
    }

    private static WidenedPinEditor Int(Func<int, object> back)
        => new(new IntPinEditor(), v => (int)Math.Clamp(AsDouble(v), int.MinValue, int.MaxValue), i => back((int)i));

    private static double AsDouble(object? v) => v switch
    {
        null => 0.0,
        IConvertible c => c.ToDouble(System.Globalization.CultureInfo.InvariantCulture),
        _ => 0.0,
    };
}
