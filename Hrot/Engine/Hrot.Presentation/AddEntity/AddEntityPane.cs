using Fdp.Interfaces;
using Hrot.Common.Constants;
using Hrot.Core.Mission;
using ImGuiNET;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ <c>CE-4142</c> GL4 (G5 + G6) — the gallery's SIDE PANE below the picker's default content (big picture, name, group, the tile line):
/// who it will be placed as, and the facts the TKB holds (speed, seats, weapons, DIS). 📄 docs/DESIGN_Add_Entity_Picker.md §2f G6.
/// Draws through the picker's <see cref="PickerRequest.DrawPane"/> hook — no entity-only picker.
/// </summary>
public static class AddEntityPane
{
    /// <summary>The side the pane names, e.g. <c>Friendly</c>; null for a side-less map graphic.</summary>
    public static string? SideLabel(eForceIdentifier? side) => side switch
    {
        eForceIdentifier.FORCE_FRIENDLY => "Friendly",
        eForceIdentifier.FORCE_OPPOSING => "Hostile",
        eForceIdentifier.FORCE_NEUTRAL  => "Neutral",
        _                               => null,
    };

    /// <summary>The pane rows of an entry: (label, value); empty for a non-type entry. Pure — the rail asserts it without ImGui.</summary>
    public static IReadOnlyList<(string Label, string Value)> Rows(ITkbDatabase tkb, PickerEntry entry, eForceIdentifier? side)
    {
        var rows = new List<(string, string)>();
        if (entry.Tag is not EntityTypeEntry e) return rows;
        // ⭐ Terrain authoring U-1 (B-5) — a recipe / template has NO side (the mockup greys the Side row): its pane is what it makes, never "Placed as"
        if (e.Terrain is { } terrain) { rows.AddRange(terrain.Facts); return rows; }
        if (SideLabel(side) is { } s) rows.Add(("Placed as", s));
        if (e.DisabledReason is { } why) rows.Add(("Unavailable", why));
        if (tkb.TryGetByType((long)e.TkbType, out var t)) rows.AddRange(EntityRoleLine.Facts(t, e.DisText));
        else if (e.DisText is not null) rows.Add(("DIS", e.DisText));
        return rows;
    }

    /// <summary>Draws <see cref="Rows"/> as a muted-label table under the default pane content.</summary>
    public static void Draw(ITkbDatabase tkb, PickerEntry entry, PickerPaneContext ctx, eForceIdentifier? side)
    {
        var rows = Rows(tkb, entry, side);
        if (rows.Count == 0) return;
        ImGui.Separator();
        foreach (var (label, value) in rows)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();
            ImGui.SameLine(88f);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(PickerText.Drawable(value));
            ImGui.PopTextWrapPos();
        }
    }
}
