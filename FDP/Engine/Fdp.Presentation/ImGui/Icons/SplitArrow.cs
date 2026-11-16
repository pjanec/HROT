using System.Collections.Generic;
using ImGuiNET;

namespace Fdp.Presentation.Icons;

/// <summary>One line of a <see cref="SplitArrow"/> menu: a label, a ✓ when <see cref="Checked"/>, greyed when not <see cref="Enabled"/>, an optional hover tooltip.</summary>
public readonly record struct SplitItem(string Label, bool Checked = false, bool Enabled = true, string? Tooltip = null);

/// <summary>
/// ⭐ CE-5006 W1 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-1 / TE-19) — the SHARED split-button arrow: the narrow ▾ right after a caller's face button (<c>SameLine(0, 1)</c>) that opens a popup of choices with a ✓ on the current one, then
/// optional trailing actions after a separator. Extracted from the main toolbar's preset arrow (CE-3393), whose behaviour it keeps. Two ways in: <see cref="Begin"/> / <see cref="Item"/> / <see cref="End"/> (no allocation — the items are
/// drawn as the caller walks them) and <see cref="Draw"/> (a list in, the clicked index out).
/// </summary>
public static class SplitArrow
{
    /// <summary>
    /// Draws the ▾ after the previous item and, when it was clicked, opens the popup <paramref name="popupId"/>. Returns true while the popup is open — then call <see cref="Item"/> for each line and <see cref="End"/>.
    /// </summary>
    public static bool Begin(string arrowId, string popupId, string? arrowTooltip = null)
    {
        Gui.SameLine(0f, 1f);
        if (Gui.ArrowButton(arrowId, ImGuiDir.Down)) Gui.OpenPopup(popupId);
        if (arrowTooltip is not null && Gui.IsItemHovered()) Gui.SetTooltip(arrowTooltip);
        return Gui.BeginPopup(popupId);
    }

    /// <summary>One menu line (✓ when <paramref name="isChecked"/>); true on the frame it is clicked. A disabled line never reports a click.</summary>
    public static bool Item(string label, bool isChecked = false, bool enabled = true, string? tooltip = null)
    {
        bool clicked = Gui.MenuItem(label, null, isChecked, enabled);
        if (tooltip is not null && Gui.IsItemHovered()) Gui.SetTooltip(tooltip);
        return clicked && enabled;
    }

    /// <summary>A separator line between the choices and the trailing actions.</summary>
    public static void Separator() => Gui.Separator();

    /// <summary>Closes the popup begun by a true <see cref="Begin"/>.</summary>
    public static void End() => Gui.EndPopup();

    /// <summary>
    /// The list form: draws the arrow and the popup; returns the clicked slot — <paramref name="items"/> first (0..n−1), then <paramref name="trailing"/> (n..), or −1. Allocates nothing itself.
    /// </summary>
    public static int Draw(string arrowId, string popupId, string? arrowTooltip, IReadOnlyList<SplitItem> items, IReadOnlyList<SplitItem>? trailing = null)
    {
        if (!Begin(arrowId, popupId, arrowTooltip)) return -1;
        int clicked = -1, slot = 0;
        for (int i = 0; i < items.Count; i++, slot++)
        {
            var it = items[i];
            if (Item(it.Label, it.Checked, it.Enabled, it.Tooltip)) clicked = slot;
        }
        if (trailing is { Count: > 0 })
        {
            Separator();
            for (int i = 0; i < trailing.Count; i++, slot++)
            {
                var it = trailing[i];
                if (Item(it.Label, it.Checked, it.Enabled, it.Tooltip)) clicked = slot;
            }
        }
        End();
        return clicked;
    }

    /// <summary>
    /// The pure decision: the slot a click on <paramref name="slot"/> of (<paramref name="items"/> then <paramref name="trailing"/>) answers — the slot itself when it exists and is enabled, else −1.
    /// </summary>
    public static int Resolve(IReadOnlyList<SplitItem> items, IReadOnlyList<SplitItem>? trailing, int slot)
    {
        if (slot < 0) return -1;
        if (slot < items.Count) return items[slot].Enabled ? slot : -1;
        int t = slot - items.Count;
        return trailing is not null && t < trailing.Count && trailing[t].Enabled ? slot : -1;
    }
}
