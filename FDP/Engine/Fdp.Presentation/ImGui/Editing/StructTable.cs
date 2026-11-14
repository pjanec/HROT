using System;
using ImGuiNET;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// ⭐ N-8 (CE-3384, docs/DESIGN_Struct_Editor.md §11 step 7) — the ONE chrome of a struct table: the two-column <i>Field · Value</i> table every <see cref="ComponentEditDrawer"/> row draws into. A host no longer builds its own table
/// (its own borders, its own column widths) around the drawer: it says <c>if (StructTable.Begin(id)) { drawer.DrawRoot(…); StructTable.End(); }</c>. The label column is the table's (stretch 40 %), the value column the rest; a host that wants
/// a header row asks for it.
/// </summary>
public static class StructTable
{
    /// <summary>The label column's share of the width.</summary>
    public const float LabelShare = 0.4f;

    /// <summary>⭐ CE-4358 — the flags of EVERY struct-editor table (ONE place): stretch columns the user can drag (the divider), the widths kept by ImGui per table id — i.e. per window — while it lives.</summary>
    public const ImGuiTableFlags Flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable;

    /// <summary>Opens the table; false when ImGui clipped it (nothing to draw, no <see cref="End"/>).</summary>
    public static bool Begin(string id, bool headers = false, string label = "Field", string value = "Value")
    {
        if (!ImGuiApi.BeginTable(id, 2, Flags)) return false;
        ImGuiApi.TableSetupColumn(label, ImGuiTableColumnFlags.WidthStretch, LabelShare);
        ImGuiApi.TableSetupColumn(value, ImGuiTableColumnFlags.WidthStretch, 1f - LabelShare);
        if (headers) ImGuiApi.TableHeadersRow();
        return true;
    }

    /// <summary>⭐ CE-4358 — the label column's width as of the last <see cref="End"/> (what a drag of the divider changed; the rails read it).</summary>
    public static float LastLabelWidth { get; private set; }

    public static void End() { if (ImGuiApi.TableGetRowIndex() >= 0) { ImGuiApi.TableSetColumnIndex(0); LastLabelWidth = ImGuiApi.GetContentRegionAvail().X; } ImGuiApi.EndTable(); }
}
