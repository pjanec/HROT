using System.Collections.Generic;
using System.Numerics;
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
        // ⭐ CE-1965 — the face just drawn: right-clicking it opens the menu too; the bar stands beside it at its full height
        var faceMin = Gui.GetItemRectMin(); var faceMax = Gui.GetItemRectMax();
        bool rightOnFace = Gui.IsItemHovered() && Gui.IsMouseReleased(ImGuiMouseButton.Right);
        Gui.SameLine(0f, BarGap);
        if (Bar(arrowId, faceMax.Y - faceMin.Y, arrowTooltip ?? "Choose…") || rightOnFace) Gui.OpenPopup(popupId);
        LastFace = (faceMin, faceMax);
        LastBar = (Gui.GetItemRectMin(), Gui.GetItemRectMax());
        return Gui.BeginPopup(popupId);
    }

    /// <summary>⭐ <c>CE-1965</c> — the ▾ bar's width at the default font size (px): narrow, but a whole column to aim at — the face and the bar are two buttons,
    /// each lit on its own hover. 🔒 User, 2026-11-18: "Will the icon and the vertical bar on the right be separated hit zones? … in current implementation …
    /// the little button in the corner, it was very difficult to click it. And I never knew if I clicked the icon or the arrow."</summary>
    public const float BarWidth = 14f;

    /// <summary>The gap between the face and its bar (px).</summary>
    public const float BarGap = 1f;

    /// <summary>The bar's width at the current font size.</summary>
    public static float BarWidthNow => BarWidth * Gui.GetFontSize() / 14f;

    /// <summary>⭐ CE-1965 — the face and the bar of the split drawn last (screen px): the rails measure that they are two hit zones, side by side, one height.</summary>
    public static (Vector2 Min, Vector2 Max) LastFace { get; private set; }
    /// <inheritdoc cref="LastFace"/>
    public static (Vector2 Min, Vector2 Max) LastBar { get; private set; }

    private static readonly Vector4 BarFill = new(0x1C / 255f, 0x1C / 255f, 0x22 / 255f, 1f), BarBorder = new(0x2E / 255f, 0x2E / 255f, 0x36 / 255f, 1f),
                                    BarHover = new(1f, 1f, 1f, 0.16f), ArrowColour = new(0.80f, 0.80f, 0.85f, 1f);

    /// <summary>
    /// ⭐ CE-1965 — THE ▾ bar of every split button: a narrow button as tall as <paramref name="height"/> (the face beside it), the ▾ centred in it, its own
    /// hover highlight and tooltip. True on a click. Allocates nothing.
    /// </summary>
    public static bool Bar(string id, float height, string? tooltip)
    {
        var size = new Vector2(BarWidthNow, height > 1f ? height : Gui.GetFrameHeight());
        var pos = Gui.GetCursorScreenPos();
        bool clicked = Gui.InvisibleButton(id, size);
        bool hovered = Gui.IsItemHovered();
        var dl = Gui.GetWindowDrawList();
        float r = 3f * Gui.GetFontSize() / 14f;
        dl.AddRectFilled(pos, pos + size, Gui.GetColorU32(BarFill), r);
        if (hovered) dl.AddRectFilled(pos, pos + size, Gui.GetColorU32(BarHover), r);
        dl.AddRect(pos, pos + size, Gui.GetColorU32(BarBorder), r);
        float m = size.X * 0.28f; var c = pos + size * 0.5f;
        dl.AddTriangleFilled(c + new Vector2(-m, -m * 0.5f), c + new Vector2(m, -m * 0.5f), c + new Vector2(0f, m * 0.6f), Gui.GetColorU32(ArrowColour));
        if (hovered && tooltip is { Length: > 0 }) Gui.SetTooltip(tooltip);
        return clicked;
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
