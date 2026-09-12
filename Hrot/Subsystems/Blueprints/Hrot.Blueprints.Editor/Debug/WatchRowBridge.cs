using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Row 59b — a <see cref="Watch"/> becomes a <see cref="VariableRow"/>, so the Watch panel
/// renders through the SAME control and the SAME formatter as every other variable surface.</b>
///
/// <para>📌 <b>ruling 11, verbatim:</b> <i>"the runtime value change is the same mechanism the Watch
/// panel should provide — <b>SHARE it</b>."</i> ⇒ ⛔ a Watch-local table, formatter or editor fails
/// ruling 9; ⭐ the bridge is the whole of what Watch needs that is Watch-specific.</para>
///
/// <para>🔴🔴 <b>This is what closes <c>BP-01</c>.</b> The panel rendered
/// <c>Convert.ToHexString(w.LastValueBytes)</c> — 📌 the tracker's own words: <i>"Watch panel shows raw
/// hex bytes; <c>MarshalFromBytes</c> is complete, tested, and used at 4 other sites in the same
/// file."</i> ⭐ It still is; the panel simply never called it. ⇒ ⛔ <b>NEVER raw hex.</b></para>
///
/// <para>⚠ <b>Bytes come from <c>LastValueBytes</c>, and the row COPIES them.</b> The watch reuses one
/// 64-byte buffer, so a row holding the span would show whatever the buffer contains at DRAW time
/// rather than at OBSERVE time — ⛔ a race that reads as flicker and is very hard to see.</para>
/// </summary>
public static class WatchRowBridge
{
    /// <summary>
    /// ⭐ The Watch section's row-origin section name. ⛔ Shared with nothing else, so a Watch row and
    /// a Details row of the same variable stay distinguishable in the change monitor.
    /// </summary>
    public const string Section = "watch";

    /// <summary>
    /// ⭐⭐ Projects one watch. <paramref name="isStale"/> keeps §1a's rule: ⛔ <b>a stale row is KEPT,
    /// showing its last value greyed</b> — dropping it would make the list silently shrink.
    /// </summary>
    public static VariableRow ToRow(Watch watch, bool isStale = false)
    {
        if (watch is null) throw new ArgumentNullException(nameof(watch));

        // ⚠ COPY. See the class remarks — the watch's buffer is reused between observations.
        var bytes = watch.LastValueBytes.ToArray();

        return new VariableRow(
            Origin:    new VariableRowOrigin(
                           watch.AssetId, default, Section, watch.DisplayName, AssetName: ""),
            ShortName: watch.DisplayName,
            TypeText:  watch.ExpectedType.Name,
            ClrType:   watch.ExpectedType,
            ReadValue: () => bytes,
            AssetTick: () => watch.HasEverBeenWritten ? watch.LastUpdateTick : (uint?)null,
            RowKind:   VariableRowKind.Normal,
            IsStale:   isStale,
            // ⭐⭐ "Show NOTHING before the run" (row 59b) is a STATE, not an empty value — and it is
            //    the SAME state the Details table already renders as "(pending)". ⛔ The panel used to
            //    spell it "--", a second vocabulary for one meaning.
            HasEverBeenWritten: watch.HasEverBeenWritten);
    }

    /// <summary>Projects a whole watch list, in order.</summary>
    public static IReadOnlyList<VariableRow> ToRows(IEnumerable<Watch> watches)
        => (watches ?? throw new ArgumentNullException(nameof(watches))).Select(w => ToRow(w)).ToList();
}
