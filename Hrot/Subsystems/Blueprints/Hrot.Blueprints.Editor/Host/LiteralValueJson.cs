using System.Globalization;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Converts between a <c>LiteralNode.ValueJson</c> (a raw C# literal — e.g. <c>5f</c>, <c>-1L</c>,
/// <c>(ushort)3</c>, <c>"hi"</c>) and the plain value shown in the inline body editor, so the designer
/// types a bare value and never sees the C# syntax (suffixes, casts, quotes).
/// <para>
/// Inline editing reuses the existing pin-default editors (only Int32 / Single / Boolean / String are
/// registered), so the whole integer family is edited through the <b>Int32</b> editor as a proxy and
/// re-formatted to the correct C# literal on commit. Types with no proxy editor (<see cref="EditorTypeId"/>
/// = null) keep their value in the node title + Details.
/// </para>
/// </summary>
internal static class LiteralValueJson
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// The pin TypeId whose registered editor is reused to edit a literal of <paramref name="typeId"/>
    /// inline (a proxy — e.g. every integer width edits through the Int32 editor). Null → no inline editor.
    /// </summary>
    public static string? EditorTypeId(string? typeId) => typeId switch
    {
        "System.Int32" or "System.Int64" or "System.Int16" or "System.Byte"
            or "System.SByte" or "System.UInt16" or "System.UInt32" => "System.Int32",
        "System.Single"  => "System.Single",
        "System.Boolean" => "System.Boolean",
        "System.String"  => "System.String",
        _                => null,
    };

    /// <summary>True when the literal type can be edited inline in the node body.</summary>
    public static bool HasInlineEditor(string? typeId) => EditorTypeId(typeId) != null;

    /// <summary>
    /// ValueJson (C# literal) → the plain value the proxy editor expects: strips a leading cast
    /// (<c>(ushort)</c>), a trailing numeric suffix (<c>L</c>/<c>u</c>/<c>f</c>), or string quotes.
    /// </summary>
    public static string ToEditString(string? typeId, string? valueJson)
    {
        var raw = (valueJson ?? string.Empty).Trim();
        if (typeId == "System.String")
            return raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw;
        if (typeId == "System.Boolean")
            return raw;

        // Numeric: drop a leading "(cast)" and any trailing type-suffix letters.
        if (raw.StartsWith("(", StringComparison.Ordinal))
        {
            var close = raw.IndexOf(')');
            if (close >= 0) raw = raw[(close + 1)..];
        }
        return raw.TrimEnd('L', 'l', 'u', 'U', 'f', 'F').Trim();
    }

    /// <summary>
    /// Boxed editor value → the ValueJson C# literal for <paramref name="typeId"/> (adds the suffix /
    /// cast / quotes the Roslyn emitter needs). Integer-family values arrive as boxed <see cref="int"/>
    /// from the proxy editor.
    /// </summary>
    public static string ToValueJson(string? typeId, object? value)
    {
        switch (typeId)
        {
            case "System.Int32":  return I(value).ToString(Inv);
            case "System.Int64":  return I(value).ToString(Inv) + "L";
            case "System.UInt32": return ((uint)Math.Max(0, I(value))).ToString(Inv) + "u";
            case "System.Int16":  return "(short)" + ((short)Math.Clamp(I(value), short.MinValue, short.MaxValue)).ToString(Inv);
            case "System.UInt16": return "(ushort)" + ((ushort)Math.Clamp(I(value), ushort.MinValue, ushort.MaxValue)).ToString(Inv);
            case "System.Byte":   return "(byte)" + ((byte)Math.Clamp(I(value), byte.MinValue, byte.MaxValue)).ToString(Inv);
            case "System.SByte":  return "(sbyte)" + ((sbyte)Math.Clamp(I(value), sbyte.MinValue, sbyte.MaxValue)).ToString(Inv);
            case "System.Single":  return Convert.ToSingle(value ?? 0f, Inv).ToString(Inv) + "f";
            case "System.Boolean": return (value is bool b && b) ? "true" : "false";
            case "System.String":  return "\"" + (value?.ToString() ?? string.Empty) + "\"";
            default:               return value?.ToString() ?? string.Empty;
        }
    }

    private static int I(object? value)
    {
        try { return Convert.ToInt32(value ?? 0, Inv); } catch { return 0; }
    }
}
