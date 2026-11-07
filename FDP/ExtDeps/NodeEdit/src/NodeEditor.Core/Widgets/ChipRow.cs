using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

public enum ChipRowOpKind { None, Click, Remove, Add, Move }

/// <summary>What the user did to a <see cref="ChipRow"/> this frame: click chip <c>Index</c> · remove it · add (⊕) · move chip <c>Index</c> to <c>Target</c>. Never a callback — the host applies it.</summary>
public readonly record struct ChipRowOp(ChipRowOpKind Kind, int Index = -1, int Target = -1);

/// <summary>
/// ⭐ B1 (docs/DESIGN_Struct_Editor.md §8, struct-editor-visual.svg) — a short list of words / flags as chips that <b>WRAP</b> within the available width, a ✕ on each and a ⊕ at the end. Chips are
/// <see cref="Chip"/>s; the row measures them with <c>CalcTextSize</c> and breaks the line itself. Returns ONE <see cref="ChipRowOp"/> per frame. Drag a chip to reorder (a bonus — K4: a host also moves
/// chips by its own menu). Zero allocation per steady frame.
/// </summary>
public static class ChipRow
{
    /// <summary>Draws the row at the cursor. <paramref name="labels"/> is read by index only (no enumerator).</summary>
    public static ChipRowOp Draw(string id, IReadOnlyList<string> labels, bool canRemove = true, bool canAdd = true, IconGlyph? icon = null)
    {
        ImGui.PushID(id);
        uint owner = ImGui.GetID("row");
        var op = default(ChipRowOp);
        float spacing = ImGui.GetStyle().ItemSpacing.X, avail = ImGui.GetContentRegionAvail().X, x = 0f;
        var end = canRemove ? ChipEnd.Removable : ChipEnd.None;
        int n = labels.Count;
        for (int i = 0; i < n; i++)
        {
            string label = labels[i];
            float w = Chip.WidthOf(label, icon, avail, end);
            x = Place(i, x, w, spacing, avail);
            ImGui.PushID(i);
            var hit = Chip.Draw("##c", label, icon, avail, end);
            if (hit == ChipHit.Body) op = new ChipRowOp(ChipRowOpKind.Click, i);
            else if (hit == ChipHit.Remove) op = new ChipRowOp(ChipRowOpKind.Remove, i);
            if (KitDrag.Reorder(owner, i, label, asSource: true, asTarget: true, out int from)) op = new ChipRowOp(ChipRowOpKind.Move, from, i);
            ImGui.PopID();
        }
        if (canAdd)
        {
            float h = ImGui.GetFrameHeight();
            x = Place(n, x, h, spacing, avail);
            if (AddButton(h)) op = new ChipRowOp(ChipRowOpKind.Add, n);
        }
        ImGui.PopID();
        return op;
    }

    /// <summary>Puts the next item of width <paramref name="w"/> on the current line when it fits, else on a new one; returns the line's used width after it.</summary>
    private static float Place(int index, float x, float w, float spacing, float avail)
    {
        if (index == 0) return w;
        if (x + spacing + w <= avail) { ImGui.SameLine(); return x + spacing + w; }
        return w;   // not SameLine ⇒ ImGui starts a new line
    }

    /// <summary>The ⊕ — a round button, a green plus in an outlined circle.</summary>
    private static bool AddButton(float h)
    {
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("##add", new Vector2(h, h));
        bool hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var c = pos + new Vector2(h * 0.5f, h * 0.5f);
        dl.AddCircleFilled(c, h * 0.5f, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg));
        dl.AddCircle(c, h * 0.5f, ImGui.GetColorU32(ImGuiCol.Border));
        float gw = ImGui.CalcTextSize(KitGlyph.Plus).X;
        dl.AddText(new Vector2(c.X - gw * 0.5f, pos.Y + (h - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(IconPalette.Green), KitGlyph.Plus);
        return clicked;
    }
}
