using System.Globalization;

namespace StructEdit.Core.Text;

/// <summary>
/// ⭐ S6 / S9 (📄 docs/DESIGN_Struct_Editor.md §3 Parse / Format, §6 S6 S9, CE-3380) — text ⇄ value for the BUILT-IN types, with a refusal that is a MESSAGE
/// (<i>"'3x' is not a whole number"</i>, <i>"300 is out of range (0 … 255)"</i>) — never a silent ignore. ⭐ <c>long</c> parses as <c>long</c>; a flags enum reads and writes its names
/// joined (<c>"Red, Blue"</c>); <c>Nullable&lt;T&gt;</c> reads an empty text as null. The catalogue's built-in TypeEditors call this; a domain type brings its own Parse.
/// Invariant culture throughout (the file's text, the cell's text, Paste).
/// </summary>
public static class ValueText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>True when <paramref name="type"/> (or its nullable underlying type) is one this class parses.</summary>
    public static bool Handles(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(string) || t == typeof(Guid);
    }

    /// <summary>Parses <paramref name="text"/> as <paramref name="type"/>; on refusal <paramref name="problem"/> says why and <paramref name="value"/> is null.</summary>
    public static bool TryParse(Type type, string? text, out object? value, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(type);
        value = null; problem = null;
        text ??= "";
        var under = Nullable.GetUnderlyingType(type);
        if (under is not null)
        {
            if (text.Trim().Length == 0) return true;   // empty ⇒ not set
            type = under;
        }
        string s = text.Trim();
        if (type == typeof(string)) { value = text; return true; }
        if (type == typeof(bool))
        {
            if (bool.TryParse(s, out var b)) { value = b; return true; }
            if (s is "1" or "yes" or "on") { value = true; return true; }
            if (s is "0" or "no" or "off") { value = false; return true; }
            problem = Quote(s) + " is not yes / no";
            return false;
        }
        if (type == typeof(Guid))
        {
            if (Guid.TryParse(s, out var g)) { value = g; return true; }
            problem = Quote(s) + " is not an id (32 hex digits)";
            return false;
        }
        if (type.IsEnum) return TryParseEnum(type, s, out value, out problem);
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            if (!decimal.TryParse(s, NumberStyles.Float, Inv, out var dec) && !double.TryParse(s, NumberStyles.Float, Inv, out _))
            { problem = Quote(s) + " is not a number"; return false; }
            if (type == typeof(decimal)) { value = dec; return true; }
            double d = double.Parse(s, NumberStyles.Float, Inv);
            if (type == typeof(float))
            {
                if (Math.Abs(d) > float.MaxValue) { problem = s + " is out of range for a float"; return false; }
                value = (float)d;
            }
            else value = d;
            return true;
        }
        if (IsInteger(type))
        {
            if (!decimal.TryParse(s, NumberStyles.Integer | NumberStyles.AllowThousands, Inv, out var n))
            { problem = Quote(s) + " is not a whole number"; return false; }
            var (min, max) = RangeOf(type);
            if (n < min || n > max) { problem = s + " is out of range (" + min.ToString(Inv) + " … " + max.ToString(Inv) + ")"; return false; }
            value = Convert.ChangeType(n, type, Inv);
            return true;
        }
        problem = "no text form for " + type.Name;
        return false;
    }

    /// <summary>The text of <paramref name="value"/> (the cell, Copy, the file's readable form agree): invariant numbers (<c>"R"</c> for floats), enum names (flags joined), empty for null.</summary>
    public static string Format(object? value)
    {
        switch (value)
        {
            case null: return "";
            case string s: return s;
            case bool b: return b ? "true" : "false";
            case float f: return f.ToString("R", Inv);
            case double d: return d.ToString("R", Inv);
            case Enum e: return e.ToString();
            case IFormattable fm: return fm.ToString(null, Inv);
            default: return value.ToString() ?? "";
        }
    }

    private static bool TryParseEnum(Type type, string s, out object? value, out string? problem)
    {
        value = null; problem = null;
        bool flags = type.IsDefined(typeof(FlagsAttribute), false);
        if (flags && s.Length == 0) { value = Enum.ToObject(type, 0); return true; }
        string[] parts = flags ? s.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new[] { s };
        ulong mask = 0;
        foreach (var p in parts)
        {
            if (char.IsDigit(p[0]) || p[0] == '-') { problem = Quote(p) + " is not one of " + Names(type); return false; }
            if (!Enum.TryParse(type, p, ignoreCase: true, out var one) || !Enum.IsDefined(type, one!)) { problem = Quote(p) + " is not one of " + Names(type); return false; }
            if (!flags) { value = one; return true; }
            mask |= Convert.ToUInt64(one, Inv);
        }
        value = Enum.ToObject(type, mask);
        return true;
    }

    private static string Names(Type t) => string.Join(", ", Enum.GetNames(t));

    private static string Quote(string s) => "'" + s + "'";

    private static bool IsInteger(Type t)
        => t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) || t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte);

    private static (decimal Min, decimal Max) RangeOf(Type t)
        => t == typeof(int) ? (int.MinValue, int.MaxValue)
         : t == typeof(uint) ? (uint.MinValue, uint.MaxValue)
         : t == typeof(long) ? (long.MinValue, long.MaxValue)
         : t == typeof(ulong) ? (ulong.MinValue, ulong.MaxValue)
         : t == typeof(short) ? (short.MinValue, short.MaxValue)
         : t == typeof(ushort) ? (ushort.MinValue, ushort.MaxValue)
         : t == typeof(byte) ? (byte.MinValue, byte.MaxValue)
         : (sbyte.MinValue, sbyte.MaxValue);
}
