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

    /// <summary>
    /// ⭐ The one-line cell text, for the CURRENT arm. ⚠ Kept so ~every existing caller is unchanged;
    /// ⭐ the mode-aware overload below is what row 58 added.
    /// </summary>
    public string Cell(VariableRow row, int width = DefaultCellWidth)
        => Cell(row, VariableValueMode.Current, width);

    /// <summary>
    /// ⭐⭐⭐ <b>The ONE Value column, read through the arm <paramref name="mode"/> names.</b>
    ///
    /// <para>📌 <b><c>Q32</c> ruling 3:</b> <i>"ONE Value column, meaning switched by run state"</i>
    /// ⇒ ⛔ <b>not two columns, and not two formatters</b> — the same elision, the same decode, the
    /// same tooltip contract; only the SOURCE of the bytes differs.</para>
    ///
    /// <para>⭐ <b>Three outcomes stay distinct, and they mean different things:</b>
    /// <c>(pending)</c> = <i>the run has not written this yet</i> · <c>&lt;unreadable&gt;</c> =
    /// <i>the bytes did not decode</i> · and in the INITIAL arm, a declaration with no default is
    /// <b>zero-initialised</b> and renders as that zero — 📌 <c>BP-247</c>'s uniform rule
    /// (<i>"`0` means leave it zero-initialised, for EVERY type"</i>), ⛔ not a fourth string.</para>
    /// </summary>
    public string Cell(VariableRow row, VariableValueMode mode, int width = DefaultCellWidth)
    {
        if (mode == VariableValueMode.Initial)
        {
            var initial = InitialText(row);
            return initial is null ? Unreadable : Elide(initial, width);
        }

        // ✅ Already designed and shipped on the Watch side via !HasEverBeenWritten -- "nothing before
        //    the run" is a state, not an empty value.
        if (!row.HasEverBeenWritten) return PendingFirstWrite;

        var decoded = Decode(row);
        if (decoded is null) return Unreadable;

        return Elide(OneLine(decoded), width);
    }

    /// <summary>⭐⭐ The tooltip — pretty-printed, multi-line, one field per line for a struct.
    /// ⛔ Same decode, same value; only the LAYOUT differs.</summary>
    public string Tooltip(VariableRow row) => Tooltip(row, VariableValueMode.Current);

    /// <summary>⭐ The tooltip for the arm <paramref name="mode"/> names. ⛔ Same value, richer layout.</summary>
    public string Tooltip(VariableRow row, VariableValueMode mode)
    {
        if (mode == VariableValueMode.Initial)
        {
            var initial = InitialText(row, multiLine: true);
            return initial is null
                ? $"{Unreadable}\nThe declared type could not be resolved."
                : "Initial value\n" + initial;
        }

        if (!row.HasEverBeenWritten) return PendingFirstWrite;

        var decoded = Decode(row);
        if (decoded is null)
            return $"{Unreadable}\nThe type could not be resolved or the bytes did not match it.";

        string body = MultiLine(decoded);
        return row.IsStale ? body + "\nasset/entity no longer present" : body;
    }

    // ── the INITIAL arm (row 58) ────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The declared starting value as text, or <c>null</c> when the type cannot be resolved.
    ///
    /// <para>⭐ <b>Two sources, in order.</b> ① the persisted <c>DefaultValueJson</c>, rendered as it
    /// is stored — ⛔ <b>no second converter</b>: the JSON <i>is</i> the authored value, and
    /// type-directed conversion here would be a parallel implementation of
    /// <c>DefaultLiteral</c>'s job *(which produces C# source for the compiler, not display text, and
    /// is `internal` to `Hrot.Blueprints.Compiler` in any case)*. ② no default declared ⇒
    /// <b>zero-initialised</b>, rendered from the CLR type through the SAME layout the current arm
    /// uses — 📌 <c>BP-247</c>: <i>"`0` means leave it zero-initialised, for EVERY type."</i></para>
    /// </summary>
    private static string? InitialText(VariableRow row, bool multiLine = false)
    {
        var json = row.ReadInitialJson?.Invoke();
        if (!string.IsNullOrWhiteSpace(json)) return CompactJson(json!, multiLine);

        if (row.ClrType is null) return null;

        object? zero;
        try { zero = row.ClrType.IsValueType ? Activator.CreateInstance(row.ClrType) : null; }
        catch { return null; }        // ⭐ a monitor must never take the window down

        if (zero is null) return "null";
        return multiLine ? MultiLine(zero) : OneLine(zero);
    }

    /// <summary>
    /// ⭐ Normalises stored JSON for display. ⛔ Does NOT interpret it against the type — that is the
    /// compiler's job and it already has an owner.
    /// </summary>
    private static string CompactJson(string json, bool multiLine)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(
                doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = multiLine });
        }
        catch
        {
            // ⚠ Hand-edited or malformed JSON: show it verbatim rather than claiming it is unreadable.
            //   ⛔ The value IS what is stored; the compiler's BP1674 is the authority on whether it
            //   converts, and this cell must not pre-empt that verdict.
            return json.Trim();
        }
    }

    // ── decode ──────────────────────────────────────────────────────────────────

    private object? Decode(VariableRow row)
    {
        // ⭐⭐⭐ Batch 90 (90a) — the OBJECT arm, preferred when present.
        //
        //    ⭐ It enters the pipeline exactly ONE STEP IN: everything after this point — notation,
        //      elision, the struct tooltip, <unreadable> — is unchanged and shared, because Cell and
        //      Tooltip both funnel through here. ⛔ That is why this is the only place the arm is
        //      read: a second entry point would be ruling 9 in miniature, and a Value cell that is
        //      live while its tooltip says (pending) is worse than neither being live.
        //
        //    ⚠ No ClrType check: an object carries its own type. The byte path below needs one to
        //      decode; this one does not, which also makes a blueprint row whose declared type could
        //      not be resolved render its live value rather than <unreadable>.
        if (row.ReadValueObject is { } readObject)
        {
            object? live;
            try { live = readObject(); } catch { return null; }   // ⭐ a monitor never takes the window down

            // ⛔ Same mapping as the byte path: a raw byte[] IS the decoder's "I could not" signal, and
            //    rendering it is the BP-01 hex bug. It is <unreadable>, never formatted.
            return live is byte[] ? null : live;
        }

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
