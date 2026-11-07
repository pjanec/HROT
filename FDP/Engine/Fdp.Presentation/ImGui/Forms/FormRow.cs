using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Forms;

/// <summary>The mark a form row shows in its ONE status slot (DESIGN_Details_Forms §6.2). One at a time; <see cref="FormRow.Strongest"/> picks it (Q-V1: ✗ &gt; hourglass &gt; lock &gt; reset).</summary>
public enum FormMark { None, Refused, Pending, Locked, DiffersFromType }

/// <summary>
/// What a form row is in. Strings are the caller's own (cached — nothing is built per frame): a tooltip is read only while the mouse is over the mark / the control.
/// </summary>
public readonly record struct FormRowState(
    bool Invalid = false, string? InvalidReason = null,
    bool Refused = false, string? RefusedReason = null,
    bool Pending = false, string? PendingTip = null,
    bool Locked = false, string? LockedReason = null,
    bool DiffersFromType = false, string? TypeTip = null);

/// <summary>
/// ⭐ U-11 / D0 (docs/UX/DESIGN_Details_Forms.md §6.2, mockup details-forms-visual.svg) — THE form row, on <see cref="FormGrid"/>: <b>label | control | status slot</b>, every state ONE ImGui call and ONE mark in a fixed
/// 18 px slot at the end of the row, so a mark never shifts the layout. Usage inside <c>FormGrid.Begin(id, labels, statusSlot: true)</c>:
/// <code>FormRow.Begin("skill", "Skill", in state); ImGui.SliderFloat("##v", ref v, 0, 1); var clicked = FormRow.End(in state);</code>
/// <list type="bullet">
/// <item><b>invalid input</b> — a 1 px <c>Error</c> frame around the control while typing, the reason as <c>Error</c> text UNDER it;</item>
/// <item><b>refused / lost</b> — <c>CircleXmark</c> in <c>Error</c> in the slot, the row tinted <see cref="StatusPalette.ErrorRowTint"/>, the reason as the tooltip, a click on the mark clears it (<see cref="End"/> returns <see cref="FormMark.Refused"/>);</item>
/// <item><b>pending</b> — <c>HourglassHalf</c> in <c>Pending</c>; <b>locked</b> — the control in <c>BeginDisabled</c>, <c>Lock</c> in <c>Muted</c>, the reason as a tooltip even over the disabled control;</item>
/// <item><b>authored ≠ type</b> — <c>RotateLeft</c> in <c>Info</c>, a click returns <see cref="FormMark.DiffersFromType"/> (the caller resets).</item>
/// </list>
/// Colours are the ONE <see cref="StatusPalette"/>; glyphs and colours come from <c>Icons</c> ("form/*"). ⭐ R-2084: nothing is allocated per frame.
/// </summary>
public static class FormRow
{
    /// <summary>Q-V1 — the one mark a row shows: ✗ refused &gt; hourglass pending &gt; lock &gt; reset.</summary>
    public static FormMark Strongest(in FormRowState s)
        => s.Refused ? FormMark.Refused : s.Pending ? FormMark.Pending : s.Locked ? FormMark.Locked : s.DiffersFromType ? FormMark.DiffersFromType : FormMark.None;

    /// <summary>The <c>Icons</c> key of a mark (the glyph and its colour are the map's).</summary>
    public static string IconKeyOf(FormMark mark) => mark switch
    {
        FormMark.Refused => "form/error", FormMark.Pending => "form/pending", FormMark.Locked => "form/locked", FormMark.DiffersFromType => "form/reset", _ => "",
    };

    /// <summary>Starts a row: the label in column 1, the cursor in column 2 with the next item stretched; applies the state to what follows (the refused tint, the disabled lock, the invalid frame).</summary>
    public static void Begin(string id, string label, in FormRowState s)
    {
        ImGui.PushID(id);
        FormGrid.Row(label);
        if (s.Refused) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(StatusPalette.ErrorRowTint));
        if (s.Locked) ImGui.BeginDisabled();
        if (s.Invalid)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.Border, StatusPalette.Error);
        }
    }

    /// <summary>Ends the row after the caller drew its control: the reason under an invalid field, the locked tooltip over the control, then the status slot's mark. Returns the mark that was CLICKED
    /// (<see cref="FormMark.Refused"/>: clear it · <see cref="FormMark.DiffersFromType"/>: reset), else <see cref="FormMark.None"/>.</summary>
    public static FormMark End(in FormRowState s)
    {
        if (s.Invalid)
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }
        bool overControl = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        if (s.Locked && overControl && s.LockedReason is not null) ImGui.SetTooltip(s.LockedReason);
        if (s.Locked) ImGui.EndDisabled();
        if (s.Invalid && s.InvalidReason is not null) ImGui.TextColored(StatusPalette.Error, s.InvalidReason);

        var clicked = DrawSlot(Strongest(in s), in s);
        ImGui.PopID();
        return clicked;
    }

    /// <summary>The status slot (column 3) of the current row: the mark centred in it, the tooltip while hovered. The slot is always there — with no mark it is just empty.</summary>
    private static FormMark DrawSlot(FormMark mark, in FormRowState s)
    {
        FormGrid.Status();
        if (mark == FormMark.None) return FormMark.None;

        var icon = NodeEditor.Core.Widgets.Icons.Get(IconKeyOf(mark));
        float w = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(w, ImGui.GetFrameHeight());
        bool pressed = ImGui.InvisibleButton("##mark", size);
        bool hovered = ImGui.IsItemHovered();
        float gw = ImGui.CalcTextSize(icon.Glyph).X;
        ImGui.GetWindowDrawList().AddText(new Vector2(pos.X + (w - gw) * 0.5f, pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(icon.Color), icon.Glyph);

        if (hovered)
        {
            string? tip = mark switch { FormMark.Refused => s.RefusedReason, FormMark.Pending => s.PendingTip, FormMark.Locked => s.LockedReason, FormMark.DiffersFromType => s.TypeTip, _ => null };
            if (tip is not null) ImGui.SetTooltip(tip);
        }
        return pressed && mark is FormMark.Refused or FormMark.DiffersFromType ? mark : FormMark.None;
    }
}
