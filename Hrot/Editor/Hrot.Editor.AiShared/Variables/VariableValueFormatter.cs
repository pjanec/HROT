using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>Decodes a raw byte slice into a CLR value. Returns <c>null</c>, or the byte array itself,
/// when it cannot — both are treated as undecodable.</summary>
public delegate object? DecodeRawValue(byte[] bytes, Type type);

/// <summary>
/// ⭐⭐ <b>How a value is rendered in the cell, and in its tooltip (§4b).</b>
///
/// <para>
/// ⭐ <b>One line, never wrapping, never growing the row.</b> The cell is a glance; the tooltip is the
/// detail; the dialog is the edit. ⭐⭐ <b>The tooltip and the cell share ONE formatter</b> — a second
/// pretty-printer for tooltips would be ruling 9 in miniature.
/// </para>
///
/// <para>
/// ⛔⛔ <b>NEVER render raw hex as if it were the value.</b> 🔴 That was <c>BP-01</c>'s user-visible
/// symptom — <i>"the watch panel shows raw hex"</i> — and it came from <c>MarshalFromBytes</c> falling
/// through to <c>return bytes</c>. ⇒ after <c>S3</c> the struct arm decodes; ⭐ <b>anything still
/// undecodable says so in WORDS</b>, and this type is where that promise is kept.
/// </para>
///
/// <para>
/// ⭐⭐⭐ <b>The decoder is INJECTED, and that is deliberate.</b> The one decoder is
/// <c>BlueprintDebugSession.MarshalFromBytes</c>, which lives in <c>Hrot.Blueprints.Editor</c> —
/// <b>above</b> this assembly. ⛔ Writing a second decoder here to avoid the layering would recreate
/// exactly the duplication <c>S3</c> just collapsed. ⇒ the host passes its decoder in.
/// </para>
///
/// <para>
/// 🔴 <b>It does NOT inherit the Watch buffer's 64-byte limit.</b> <c>Watch._valueBuffer</c> is
/// <c>new byte[64]</c> and <c>WriteValue</c> <b>throws</b> above it, so <c>MemberSlotList</c> (96),
/// <c>WaveState</c> (104) and <c>HillAttackSharedState</c> (136) cannot go through that path. ⭐ This
/// formatter takes a span of any length — the limit is a property of that one carrier, not of
/// rendering.
/// </para>
/// </summary>
public sealed class VariableValueFormatter
{
    /// <summary>Elision budget for the one-line cell. ⚠ Characters, not bytes — the cell is text.</summary>
    public const int DefaultCellWidth = 48;

    public const string Unreadable = "<unreadable>";
    public const string PendingFirstWrite = "(pending)";

    private readonly DecodeRawValue _decode;

    public VariableValueFormatter(DecodeRawValue decode)
        => _decode = decode ?? throw new ArgumentNullException(nameof(decode));

    /// <summary>⭐ The one-line cell text.</summary>
    public string Cell(VariableRow row, int width = DefaultCellWidth)
    {
        // ✅ Already designed and shipped on the Watch side via !HasEverBeenWritten -- "nothing before
        //    the run" is a state, not an empty value.
        if (!row.HasEverBeenWritten) return PendingFirstWrite;

        var decoded = Decode(row);
        if (decoded is null) return Unreadable;

        return Elide(OneLine(decoded), width);
    }

    /// <summary>⭐⭐ The tooltip — pretty-printed, multi-line, one field per line for a struct.
    /// ⛔ Same decode, same value; only the LAYOUT differs.</summary>
    public string Tooltip(VariableRow row)
    {
        if (!row.HasEverBeenWritten) return PendingFirstWrite;

        var decoded = Decode(row);
        if (decoded is null)
            return $"{Unreadable}\nThe type could not be resolved or the bytes did not match it.";

        string body = MultiLine(decoded);
        return row.IsStale ? body + "\nasset/entity no longer present" : body;
    }

    // ── decode ──────────────────────────────────────────────────────────────────

    private object? Decode(VariableRow row)
    {
        if (row.ClrType is null) return null;

        var bytes = row.ReadValue().ToArray();
        if (bytes.Length == 0) return null;

        object? value;
        try { value = _decode(bytes, row.ClrType); }
        catch { return null; }        // ⭐ a monitor must never take the window down

        // ⛔ The decoder's own "I could not" signal is returning the bytes unchanged. Rendering that
        //    IS the hex bug, so it is mapped to <unreadable> here rather than formatted.
        return value is byte[] ? null : value;
    }

    // ── layout ──────────────────────────────────────────────────────────────────

    private static string OneLine(object value)
    {
        if (value is string s) return s;                    // fixed lists arrive pre-formatted
        if (IsScalar(value)) return Scalar(value);

        var fields = Fields(value);
        if (fields.Count == 0) return Scalar(value);

        return "{" + string.Join(", ", fields.Select(f => $"{f.Name}={f.Text}")) + "}";
    }

    private static string MultiLine(object value)
    {
        if (value is string s) return s;
        if (IsScalar(value)) return Scalar(value);

        var fields = Fields(value);
        if (fields.Count == 0) return Scalar(value);

        var sb = new StringBuilder();
        foreach (var f in fields) sb.Append(f.Name).Append(" = ").AppendLine(f.Text);
        return sb.ToString().TrimEnd();
    }

    private static bool IsScalar(object v)
        => v is bool or byte or sbyte or short or ushort or int or uint or long or ulong
              or float or double or decimal or char or Enum;

    private static string Scalar(object v) => v switch
    {
        bool b   => b ? "true" : "false",
        float f  => f.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        _        => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static IReadOnlyList<(string Name, string Text)> Fields(object value)
    {
        var t = value.GetType();
        var result = new List<(string, string)>();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? fv;
            try { fv = f.GetValue(value); } catch { continue; }
            result.Add((f.Name, fv is null ? "null" : IsScalar(fv) ? Scalar(fv) : OneLine(fv)));
        }
        return result;
    }

    /// <summary>⭐ Elide to fit, with the ellipsis INSIDE the braces so the shape still reads as a
    /// struct: <c>{X=1, Y=2, …}</c>, not <c>{X=1, Y…</c>.</summary>
    internal static string Elide(string text, int width)
    {
        if (width <= 1 || text.Length <= width) return text;

        if (text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
        {
            int budget = width - 4;                          // "{" + "…}" plus one separator
            if (budget <= 0) return "{…}";
            var inner = text.Substring(1, text.Length - 2);
            int cut = inner.LastIndexOf(", ", Math.Min(budget, inner.Length), StringComparison.Ordinal);
            inner = cut > 0 ? inner.Substring(0, cut) : inner.Substring(0, Math.Min(budget, inner.Length));
            return "{" + inner + ", …}";
        }

        return text.Substring(0, Math.Max(1, width - 1)) + "…";
    }
}
