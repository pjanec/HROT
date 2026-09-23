using System.Globalization;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-247</c> — the one place a persisted default value becomes a C# literal.</b>
///
/// <para>
/// 🔴 <b>What was there before: nothing.</b> <c>Stage5:107</c> and <c>:4681</c> both did
/// <c>DefaultValueCSharp = d.DefaultValueJson ?? ""</c> — the JSON text, <b>verbatim</b> — and the
/// emitters then wrote <c>s.{Name} = {DefaultValueCSharp};</c>. ⇒ a <c>float</c> variable whose default
/// is <c>0.5</c> emitted <c>s.Ratio = 0.5;</c>, a C# <b>double</b> literal, and Roslyn refused it with
/// <c>CS0664</c> <b>naming a generated file the designer has never seen</b>. ⛔ The <c>__var_-1</c> /
/// <c>BP-228</c> shape a fourth time: a diagnostic in the wrong language.
/// </para>
///
/// <para>
/// ⚠ <b>Latent rather than live, and that is exactly why it is worth fixing now:</b> every shipped
/// default is integral, <c>false</c>, or absent — measured — so it has never fired. ⭐ The Details
/// panel's write path turns it into an everyday occurrence the moment a designer can type a value.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Refusal, not pass-through.</b> A literal this cannot convert produces <c>BP1674</c> and no
/// initializer, so the failure is reported against the <b>declaration</b> in the compiler's own
/// language. ⛔ Passing an unrecognised literal through is what produced the defect.
/// </para>
/// </summary>
internal static class DefaultLiteral
{
    /// <summary>
    /// Converts <paramref name="json"/> — the declaration's persisted default — into a C# literal of
    /// <paramref name="type"/>. Returns <see langword="false"/> when no faithful literal exists;
    /// <paramref name="csharp"/> is then empty and the caller must report <c>BP1674</c>.
    /// </summary>
    /// <remarks>
    /// ⭐ An absent default is a SUCCESS with an empty literal, not a failure: the struct is
    /// zero-initialised and the emitters skip the assignment.
    /// </remarks>
    public static bool TryToCSharp(IrTypeRef type, string? json, out string csharp, out string reason)
    {
        csharp = "";
        reason = "";

        if (json is null) return true;                 // no default declared — nothing to emit
        string text = json.Trim();
        if (text.Length == 0) return true;

        // ⚠⚠ ZERO MEANS "leave it zero-initialised", for EVERY type — and this is the pre-existing
        //    contract rather than a convenience. The emitters skipped `DefaultValueCSharp == "0"`
        //    unconditionally, so a `0` on a bool, a list wrapper or a struct has always been a harmless
        //    leftover from a generic default-writer. ⛔ Refusing it turned that leftover into a compile
        //    error on assets that ship today — measured on the `ListVariable*` fixtures, the
        //    `ListVariableDemo` recipe, and a `bool` output variable carrying `0`. ⭐ Found by the suite,
        //    not by reasoning about it: the first draft special-cased only the no-literal-form types and
        //    the bool case still fell through.
        if (text == "0" || text == "0.0") return true;

        var inv = CultureInfo.InvariantCulture;
        switch (type.FullName)
        {
            case "System.Boolean":
                // ⚠ JSON spells these exactly as C# does, so the check is that it IS one of them —
                //   `True` round-trips through no parser we control and must not reach the emitter.
                if (text is "true" or "false") { csharp = text; return true; }
                reason = "expected `true` or `false`";
                return false;

            case "System.SByte":  return Integral(sbyte.TryParse(text, NumberStyles.Integer, inv, out _),  text, "",   out csharp, out reason);
            case "System.Byte":   return Integral(byte.TryParse(text, NumberStyles.Integer, inv, out _),   text, "",   out csharp, out reason);
            case "System.Int16":  return Integral(short.TryParse(text, NumberStyles.Integer, inv, out _), text, "",   out csharp, out reason);
            case "System.UInt16": return Integral(ushort.TryParse(text, NumberStyles.Integer, inv, out _),text, "",   out csharp, out reason);
            case "System.Int32":  return Integral(int.TryParse(text, NumberStyles.Integer, inv, out _),   text, "",   out csharp, out reason);
            // ⭐ The suffixes exist because the C# compiler types a bare literal by its own rules, not
            //   by the target: `0` is an `int` and `1.5` is a `double`, and both are wrong here.
            case "System.UInt32": return Integral(uint.TryParse(text, NumberStyles.Integer, inv, out _),  text, "U",  out csharp, out reason);
            case "System.Int64":  return Integral(long.TryParse(text, NumberStyles.Integer, inv, out _),  text, "L",  out csharp, out reason);
            case "System.UInt64": return Integral(ulong.TryParse(text, NumberStyles.Integer, inv, out _), text, "UL", out csharp, out reason);

            case "System.Single":
                if (float.TryParse(text, NumberStyles.Float, inv, out var f) && !float.IsNaN(f) && !float.IsInfinity(f))
                { csharp = f.ToString("R", inv) + "F"; return true; }
                reason = "expected a finite decimal number";
                return false;

            case "System.Double":
                if (double.TryParse(text, NumberStyles.Float, inv, out var dbl) && !double.IsNaN(dbl) && !double.IsInfinity(dbl))
                { csharp = dbl.ToString("R", inv) + "D"; return true; }
                reason = "expected a finite decimal number";
                return false;

            case "System.Decimal":
                if (decimal.TryParse(text, NumberStyles.Float, inv, out var dec))
                { csharp = dec.ToString(inv) + "M"; return true; }
                reason = "expected a decimal number";
                return false;

            default:
                // ⛔ Entity, the vectors, the fixed strings, the synthesized `__List_…` wrappers, the
                //    curated project structs, anything the AN2 fallback accepted: none of them has a
                //    literal form the compiler can write, and passing the text through is precisely how
                //    CS0664 got emitted. ⭐ An ABSENT default is fine — handled above.
                //    ⭐ So is a ZERO — handled uniformly at the top of this method.
                reason = $"type '{type.FullName}' has no literal form — leave the default empty";
                return false;
        }
    }

    private static bool Integral(bool parsed, string text, string suffix, out string csharp, out string reason)
    {
        if (parsed) { csharp = text + suffix; reason = ""; return true; }
        csharp = "";
        reason = "expected a whole number in range";
        return false;
    }

    /// <summary>
    /// ⭐ Whether the emitters may skip the initializer entirely — the old inline
    /// <c>IsNullOrEmpty || != "0" || != "default"</c> test, named.
    ///
    /// <para>
    /// ⚠ It still recognises the SUFFIXED zeros even though <see cref="TryToCSharp"/> now returns an
    /// empty literal for a zero: the lowering passes write <c>DefaultValueCSharp</c> directly
    /// (<c>AiPrimitiveLowering</c> emits <c>"0f"</c>, the <c>When</c> lowerings emit <c>"default"</c>)
    /// and never go through the converter. ⛔ A test that only knew about <c>"0"</c> would start
    /// emitting assignments that write a zero over a zero.
    /// </para>
    /// </summary>
    public static bool IsSkippable(string? csharp)
    {
        if (string.IsNullOrEmpty(csharp)) return true;
        if (csharp == "default") return true;
        var core = csharp!.TrimEnd('F', 'f', 'D', 'd', 'M', 'm', 'L', 'l', 'U', 'u');
        return core is "0" or "0.0";
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-300</c> — the C# text for a <c>LiteralNode</c>'s value, TYPED BY ITS PIN.</b>
    ///
    /// <para>🔴 <b>The defect this closes.</b> <c>Stage5_Schedule</c> passed <c>LiteralNode.ValueJson</c>
    /// to <c>IrOp_Const</c> verbatim, so a hand-authored <c>System.Single</c> literal spelled
    /// <c>0.2777778</c> emitted <c>var __t5 = 0.2777778;</c> and Roslyn refused the GENERATED file with
    /// <c>CS0266: cannot implicitly convert 'double' to 'float'</c> — an error naming a file the author
    /// never wrote. ⚠ The editor's drawer appends the <c>f</c>, so the guard was <b>a drawer, not a
    /// compiler rule</b>: any hand-authored asset, recipe or future tool reproduces it.</para>
    ///
    /// <para>⛔⛔ <b>CONVERT-OR-PASS-THROUGH, and the asymmetry with <see cref="TryToCSharp"/> is
    /// MEASURED rather than chosen.</b> 📐 <c>CE-300</c>'s filed fix said to route this through
    /// <see cref="TryToCSharp"/> and REFUSE what it cannot type. ⛔ That is wrong, and the corpus says
    /// so: <b><c>ValueJson</c> does not hold JSON — it holds C# SOURCE TEXT.</b> Across the 104 shipped
    /// assets its values are <c>0f</c>, <c>-1L</c>, <c>(ushort)0</c>, <c>"HullDownAttack"</c> and
    /// <c>global::…NavigationResult.Arrived</c> — suffixes, casts, a quoted string and a qualified enum
    /// member. 🔴 <b><c>TryToCSharp</c> parses JSON, so it REFUSES 42 of the 86 literal nodes that ship
    /// today</b>; refusing them would turn a latent authoring trap into a corpus-wide build break.</para>
    ///
    /// <para>⭐ So: <b>convert when the converter can, keep the author's text when it cannot.</b> Every
    /// corpus value either converts to an equivalent literal or falls through unchanged, and the one
    /// value that was broken — a bare decimal on a float pin — is exactly the case the converter
    /// handles.</para>
    ///
    /// <para>⚠⚠ <b>IT DOES MOVE ONE GOLDEN, and an earlier draft of this comment claimed it could
    /// not.</b> 📐 Measured: <c>ResolverWorldReachDemo</c> emits <c>var __t1 = 4242;</c> for a literal
    /// on a <c>System.Int64</c> pin, and now emits <c>4242L</c> — <b>one file, one line</b>. ⭐ That is
    /// a CORRECTION of the same defect this method exists for: a bare <c>4242</c> is an <c>int</c> that
    /// happened to widen implicitly. ⛔ Verified to be spelling ONLY, not binding —
    /// <c>NetworkEntityMapOps.ResolveTarget</c> has a single overload taking <c>long</c>, so both forms
    /// bind to it identically. 🔒 The row warned to check this rather than assume it; the assumption
    /// was made anyway, and the suite is what caught it.</para>
    ///
    /// <para>⛔ <b>The empty-string guard is load-bearing, not defensive.</b>
    /// <see cref="TryToCSharp"/> answers <c>true</c> with an EMPTY literal for <c>0</c> — its
    /// "leave it zero-initialised" contract for DECLARATION defaults. ⚠ A literal NODE has no such
    /// contract: emitting nothing produces <c>var __t5 = ;</c>. ⇒ an empty conversion is a
    /// pass-through, and an <c>Int32</c> literal of <c>0</c> stays <c>0</c>.</para>
    /// </summary>
    public static string ForLiteralNode(IrTypeRef type, string? valueText)
    {
        string text = valueText ?? "";
        return TryToCSharp(type, text, out string converted, out _)
            && !string.IsNullOrEmpty(converted)
                ? converted
                : text;
    }
}
