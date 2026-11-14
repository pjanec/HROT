using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-500</c> — the group-by selector, as a SHARED control.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §1b.
///
/// <para>⚠⚠ <b>Measured before building: there was nothing to mirror.</b> The dispatching handoff points
/// at <c>AiVariablesWindow.GroupBy</c> *(<c>:145</c>)* as the control to copy — 📐 that member is a
/// <b>property forwarding to <c>_model.GroupBy</c></b>, and a repo-wide search for a writer found only
/// the model's own constructor. ⇒ ⛔ <b>no group-by UI existed anywhere</b>; this is the first one, not
/// a second copy.</para>
///
/// <para>⭐⭐ <b>So it is built SHARED rather than inside the Watch window</b> — the Variables window
/// needs the identical control and would otherwise grow a divergent one *(ruling 9)*. ⚠ It is wired to
/// the Watch only; adopting it in Variables is a one-line change that batch can make when it wants the
/// surface, ⛔ not something to do to that window unasked.</para>
///
/// <para>⭐ <b>The modes are FACET LISTS, not an enum of behaviours</b> — §1b's whole point: <i>ungrouped</i>
/// is <c>[]</c>, <i>by entity</i> is <c>[Entity]</c>, <i>by asset then entity</i> is
/// <c>[Asset, Entity]</c>. ⛔ Adding a mode later is a row here, with no new grouping code.</para>
/// </summary>
public static class VariableGroupBySelector
{
    /// <summary>⭐ One offered mode: a label and the facet list it selects.</summary>
    public readonly record struct Mode(string Label, IReadOnlyList<VariableFacet> Facets);

    /// <summary>
    /// ⭐⭐ The four modes §1b names, in the order a designer reads them: the default first.
    /// ⛔ Not a hardcoded switch — each is just a facet list.
    /// </summary>
    public static IReadOnlyList<Mode> Modes { get; } = new[]
    {
        new Mode("Asset, then entity", VariableRowGrouping.WatchDefault),
        new Mode("Entity",             new[] { VariableFacet.Entity }),
        new Mode("Section",            new[] { VariableFacet.Section }),
        new Mode("Ungrouped",          Array.Empty<VariableFacet>()),
    };

    /// <summary>
    /// ⭐ The index of the mode matching <paramref name="facets"/>, or <c>-1</c> when the model carries a
    /// combination no offered mode names.
    /// <para>⚠ <c>-1</c> is a real answer and the caller must render it as "no selection" — ⛔ falling
    /// back to <c>0</c> would show "Asset, then entity" while the model was grouped some other way.
    /// 📌 Exactly the defect <c>BP-114</c> fixed in the type picker, and it is worth not repeating.</para>
    /// </summary>
    public static int IndexOf(IReadOnlyList<VariableFacet> facets)
    {
        if (facets is null) return -1;
        for (int i = 0; i < Modes.Count; i++)
            if (Modes[i].Facets.SequenceEqual(facets)) return i;
        return -1;
    }

    /// <summary>⭐ The labels, for the combo. ⛔ Built once — this runs inside a draw loop.</summary>
    public static string[] Labels { get; } = Modes.Select(m => m.Label).ToArray();

    /// <summary>
    /// ⭐⭐ Draws the combo and applies a change to <paramref name="model"/>. Returns <c>true</c> when the
    /// grouping actually changed.
    /// <para>⚠ ImGui-only — the caller must already be inside a live frame. ⭐ The pure parts
    /// (<see cref="Modes"/>, <see cref="IndexOf"/>) are what rails assert on, so the selector's BEHAVIOUR
    /// is testable headless while only its painting needs a context.</para>
    /// </summary>
    public static bool Draw(string id, VariableTableModel model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        int current = IndexOf(model.GroupBy);
        ImGuiNET.ImGui.SetNextItemWidth(180f);

        // ⚠ ImGui.Combo with a -1 index renders an empty preview and calls no item getter out of range,
        //   which is the honest rendering of "the model is grouped some way this list does not name".
        if (!ImGuiNET.ImGui.Combo(id, ref current, Labels, Labels.Length)) return false;
        if (current < 0 || current >= Modes.Count) return false;

        var chosen = Modes[current].Facets;
        if (chosen.SequenceEqual(model.GroupBy)) return false;

        model.GroupBy = chosen;
        return true;
    }
}
