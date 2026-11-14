namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100f</c>) — WHICH ROW GESTURES A SURFACE OFFERS, declared by the surface.</b>
///
/// <para>📌 <b>User, verbatim:</b> <i>"no one is interested in the other properties than the value in
/// the Watch window."</i> ⇒ the Watch offers <b>"Edit value…"</b> and ⛔ <b>not "Properties…"</b>.</para>
///
/// <para>🔴 <b>What it replaces.</b> <c>RegisterExtraWindow</c> ran
/// <c>if (window is IVariableTableHost h) AttachEditGestures(h);</c> ⇒ ⛔ <b>every table host got every
/// gesture</b>, because the wiring had no way to ask. ⚠ The registrar was not wrong — it had nothing
/// to consult.</para>
///
/// <para>⭐⭐⭐ <b>Deliberately shaped after <see cref="VariableTableColumns"/>, which already solved
/// this exact problem for COLUMNS</b> — same file neighbourhood, same <c>record struct</c>, same
/// <c>Default</c>/<c>Watch</c> pair. ⛔ <b>NOT <c>if (host is AiWatchWindow)</c></b>: a type test in the
/// registrar puts the Watch's editorial decision in a file that knows nothing about watching, and the
/// second surface that wants it would add a second type test.</para>
///
/// <para>⚠ <b>A struct of bools, not a <c>[Flags]</c> enum</b> — the same call
/// <see cref="VariableTableColumns"/> made and for the same reason: ⛔ a flags set invites callers to
/// compose arbitrary combinations, ⭐ whereas these are a small closed set of SURFACES whose
/// affordances are an editorial choice, not a configuration.</para>
/// </summary>
/// <param name="OffersProperties">
/// ⭐ Whether the row menu offers <b>"Properties…"</b> — the DECLARATION editor.
/// ⛔ <c>false</c> on the Watch: it is a monitoring surface, and a variable's tooltip and category are
/// not what a designer is watching for.
/// </param>
public readonly record struct VariableTableGestures(bool OffersProperties)
{
    /// <summary>⭐ An authoring surface — Details, the Variables table, the outline. Everything.</summary>
    public static VariableTableGestures Default => new(OffersProperties: true);

    /// <summary>
    /// ⭐⭐ The Watch — <b>values only</b>.
    /// ⚠ <b>"Edit value…" STAYS</b>, and that is not an oversight: 📌 Batch 84 built writing a live
    /// value while frozen, and the Watch is exactly where a designer does it.
    /// </summary>
    public static VariableTableGestures Watch => new(OffersProperties: false);
}
