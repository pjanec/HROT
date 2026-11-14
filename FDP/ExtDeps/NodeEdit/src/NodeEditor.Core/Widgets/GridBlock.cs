using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ B3 (docs/DESIGN_Struct_Editor.md §8) — small flat structures as a GRID: <c>BeginTable</c> with one column per field (plus a ⋮⋮ handle column and a ⋮ menu column), one row per item. The row menu, the
/// <i>Add</i> button and the fixed-size footer are <see cref="ListBlock"/>'s (one implementation); the host draws the cells between <see cref="Row"/> and <see cref="Cell"/> calls — no closures. Returns one
/// <see cref="ListOp"/> per frame from <see cref="End"/>. Zero allocation per steady frame.
/// <code>
/// if (GridBlock.Begin("wp", Headers, count, opts)) { for (i…) { GridBlock.Row(i); GridBlock.Cell(0); ImGui.InputText(…); GridBlock.Cell(1); … } }
/// var op = GridBlock.End();
/// </code>
/// </summary>
public static class GridBlock
{
    private const float HandleW = 18f, MenuW = 24f;
    private static ListOp _op;
    private static ListOptions _opt = null!;
    private static int _count, _columns;
    private static uint _owner;
    private static bool _rowOpen, _tableOpen;
    private static ListBlock.ListState? _state;

    /// <summary>Begins the grid. <paramref name="headers"/> is a caller-owned, static array (one entry per field column). False when the table is not visible — then call <see cref="End"/> anyway.</summary>
    public static bool Begin(string id, string[] headers, int count, ListOptions opt)
    {
        _owner = ImGui.GetID(id);
        ImGui.PushID(id);
        _op = default; _opt = opt; _count = count; _columns = headers.Length; _rowOpen = false;
        _state = ListBlock.StateFor(_owner);
        _tableOpen = ImGui.BeginTable("##grid", headers.Length + 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX);
        if (!_tableOpen) return false;
        ImGui.TableSetupColumn("##hcol", ImGuiTableColumnFlags.WidthFixed, HandleW);   // ⚠ not "##h": a header cell and row 0 would share one ID
        for (int c = 0; c < headers.Length; c++) ImGui.TableSetupColumn(headers[c], ImGuiTableColumnFlags.WidthStretch, 1f);   // equal shares: a short field is not squeezed
        ImGui.TableSetupColumn("##mcol", ImGuiTableColumnFlags.WidthFixed, MenuW);
        ImGui.TableHeadersRow();
        return true;
    }

    /// <summary>Starts row <paramref name="i"/>: draws its ⋮⋮ handle (drag to reorder) and its ⋮ menu; the host then draws its cells with <see cref="Cell"/>.</summary>
    public static void Row(int i)
    {
        EndRow();
        ImGui.TableNextRow();
        ImGui.PushID(i);
        _rowOpen = true;
        float h = ImGui.GetFrameHeight();
        ImGui.TableSetColumnIndex(0);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##h", new Vector2(HandleW - 4f, h));
        KitProbe.Set(KitProbe.ListHandle, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (KitDrag.Reorder(_owner, i, ListBlock.NumberText(i + 1), asSource: true, asTarget: true, out int from)) _op = new ListOp(ListOpKind.Move, from, i);
        ListBlock.DrawGlyph(ImGui.GetWindowDrawList(), KitGlyph.Grip, pos, HandleW - 4f, h, ImGui.GetColorU32(IconPalette.Muted));
        ImGui.TableSetColumnIndex(_columns + 1);
        ListBlock.DrawMenuButton(i, _count, _opt, ref _op);
    }

    /// <summary>Moves to field column <paramref name="c"/> of the current row; the next item fills it.</summary>
    public static void Cell(int c)
    {
        ImGui.TableSetColumnIndex(c + 1);
        ImGui.SetNextItemWidth(-1f);
    }

    private static void EndRow() { if (_rowOpen) { ImGui.PopID(); _rowOpen = false; } }

    /// <summary>Ends the grid: <i>Add</i> / the fixed-size footer under it; returns the frame's op.</summary>
    public static ListOp End()
    {
        EndRow();
        if (_tableOpen) ImGui.EndTable();
        _tableOpen = false;
        ListBlock.DrawAddAndFooter(_opt, _count, _state, ref _op);
        ImGui.PopID();
        return _op;
    }
}
