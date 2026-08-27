using System;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ <b>The ONE place the main toolbar's time-control group is registered.</b>
///
/// <para>📐 <b>Measured `2026-08-27`:</b> the editor and CGF each carried the same four lines —
/// construct <see cref="MainToolbarTimeControlSection"/>, then <c>RegisterEntry("TimeControlGroup",
/// sortOrder: 0, declaredHeight: DefaultEntryHeight, …Render)</c>. ⭐ They differed in exactly ONE
/// thing, and that difference is now a <b>named parameter</b> rather than a line one host has and the
/// other deleted.</para>
///
/// <para>⭐⭐⭐ <b>Why <paramref name="withSeparator"/> exists instead of always emitting it.</b>
/// 🔒 <c>CE-016</c> §7 deliberately DELETED the trailing separator on CGF: it separated the time group
/// from a <b>perspective group that host did not then register</b> — <i>"a rule drawn against
/// nothing"</i>. ⇒ ⛔ making it unconditional would put that dangling rule back, and making it silently
/// absent would hide the editor's. ⭐ Declared, so the next reader sees a decision instead of a diff
/// between two 3 000-line composition roots.</para>
///
/// <para>⛔ <b>Not an <c>IUiBundle</c>, on purpose.</b> A bundle is for a FEATURE's whole registration
/// set; this is one entry plus one optional separator, and the hosts build the facade themselves. 📌 The
/// bundle seam is for duplicated registration at scale *(the diagnostics group, 20 sites)* — wrapping
/// four lines in one would be ceremony, and it would put toolbar sort orders the golden pins at risk for
/// no gain. 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.8 (<c>H1</c>).</para>
/// </summary>
public static class ShellTimeControlToolbar
{
    /// <summary>⭐ The entry id, in one place — it is a toolbar identity and a rail asserts it.</summary>
    public const string EntryId = "TimeControlGroup";

    /// <summary>⭐ The separator id the editor emits after the group.</summary>
    public const string SeparatorId = "ToolbarSep_TimeToPersp";

    /// <summary>⚠ §7's range: the time-control group owns sort orders 0–9, so the group is 0 and the
    /// separator that closes it is 10 — the first slot of the next group's gap.</summary>
    public const int EntrySortOrder     = 0;
    public const int SeparatorSortOrder = 10;

    /// <summary>
    /// Registers the transport group into <paramref name="toolbar"/>.
    /// </summary>
    /// <param name="toolbar">
    /// the host's main toolbar. ⚠ Never null in practice — <c>WindowManager._mainToolbar</c> is a
    /// <c>readonly … = new()</c> field exposed by an expression-bodied property — which is why neither
    /// caller guards on it any more (design §5c.8 <c>H2</c>).
    /// </param>
    /// <param name="facade">the host's transport. ⭐ The existing seam: the editor supplies
    /// <c>EditorTimeTransportFacade</c>, CGF and SimHost supply <c>ClusterTimeTransportAdapter</c>.</param>
    /// <param name="withSeparator">
    /// ⭐⭐ <see langword="true"/> to close the group with a separator. ⛔ Pass <see langword="false"/> when
    /// nothing follows it on this host — see the class remarks and <c>CE-016</c> §7.
    /// </param>
    public static void Register(
        MainToolbarManager toolbar,
        ITimeTransportFacade facade,
        bool withSeparator)
    {
        if (toolbar is null) throw new ArgumentNullException(nameof(toolbar));
        if (facade  is null) throw new ArgumentNullException(nameof(facade));

        var section = new MainToolbarTimeControlSection(facade);

        toolbar.RegisterEntry(
            EntryId, sortOrder: EntrySortOrder,
            declaredHeight: MainToolbarManager.DefaultEntryHeight,
            section.Render);

        if (withSeparator)
            toolbar.RegisterSeparator(SeparatorId, sortOrder: SeparatorSortOrder);
    }
}
