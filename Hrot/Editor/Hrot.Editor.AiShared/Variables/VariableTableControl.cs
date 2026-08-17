using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>The Details/Watch variable table — the DRAWING half of <c>C-table</c>.</b>
///
/// <para>
/// ⭐⭐⭐ <b>It renders a <see cref="VariableTableView"/> and nothing else.</b> Every decision —
/// which rows exist, what they are called, how they nest, which are highlighted — was already made in
/// <see cref="VariableTableModel"/> and is covered by §9's headless rails. ⇒ ⚠ <b>this file is
/// deliberately thin</b>, because it is the one part no test can see.
/// </para>
///
/// <para>
/// ⛔ <b>It does NOT replace <c>VariablesPanelControl</c>.</b> That control is the Blackboard authoring
/// panel with its seven columns, per-section budgets and aliasing UI; retiring it is <c>C-watch</c>/
/// <c>C-outline</c>'s job and would be a large change no one can currently look at. ⚠ <b>The visual
/// check is suspended</b> — see the Batch 68 report for exactly what that leaves unverified.
/// </para>
///
/// <para>
/// ⭐ <b>Folding is <c>CollapsingHeader</c>, not new machinery</b> (§1b) — the same primitive
/// <c>VariablesPanelControl</c> already uses in three places.
/// </para>
/// </summary>
public sealed class VariableTableControl
{
    // 🔴 the sim changed it · 🟡 your edit has not landed. ⛔ Never the same colour: §4a's whole point.
    private static readonly Vector4 ChangedTint = new(0.90f, 0.20f, 0.20f, 0.22f);
    private static readonly Vector4 PendingTint = new(1.00f, 0.85f, 0.30f, 0.22f);
    private static readonly Vector4 StaleText   = new(0.55f, 0.55f, 0.55f, 1.00f);

    private readonly VariableValueFormatter _formatter;

    /// <summary>Raised when a row's VALUE cell is double-clicked ⇒ <c>EditScope.ForField</c> (§4).</summary>
    public event Action<VariableRow>? EditValueRequested;

    /// <summary>Raised when a row's NAME cell is double-clicked ⇒ <c>EditScope.WholeComponent</c> (§4).</summary>
    public event Action<VariableRow>? PropertiesRequested;

    /// <summary>
    /// ⭐⭐ Raises "Edit value…" for a row. ⛔ The ⋮ menu's own path goes through ImGui, which no
    /// headless test can drive — this is the same call it makes, and it is what lets a rail prove the
    /// gesture is ATTACHED rather than merely constructed.
    /// </summary>
    public void RaiseEditValueRequested(VariableRow row) => EditValueRequested?.Invoke(row);

    /// <summary>⭐ Raises "Properties…" for a row. Same reason as above.</summary>
    public void RaisePropertiesRequested(VariableRow row) => PropertiesRequested?.Invoke(row);

    public VariableTableControl(VariableValueFormatter formatter)
        => _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

    public void Draw(string id, VariableTableView view)
    {
        if (view.Groups.Count == 0)
        {
            DrawRows(id, view, view.UngroupedRows);
            return;
        }
        foreach (var group in view.Groups) DrawGroup(id, view, group);
    }

    private void DrawGroup(string id, VariableTableView view, VariableRowGroup group)
    {
        // ⭐⭐⭐ A collapsed header inherits its children's state, so folding everything down still shows
        //     WHERE the activity is. Without it, folding only hides.
        var agg = view.HighlightOf(group);
        if (agg.Changed) ImGui.PushStyleColor(ImGuiCol.Header, ChangedTint);
        else if (agg.Pending) ImGui.PushStyleColor(ImGuiCol.Header, PendingTint);

        bool open = ImGui.CollapsingHeader($"{group.Header}##{id}_{group.Facet}_{group.Header}",
                                           ImGuiTreeNodeFlags.DefaultOpen);
        if (agg.Changed || agg.Pending) ImGui.PopStyleColor();

        if (!open) return;

        ImGui.Indent();
        foreach (var child in group.Children) DrawGroup(id, view, child);
        if (group.Children.Count == 0) DrawRows($"{id}_{group.Header}", view, group.Rows);
        ImGui.Unindent();
    }

    private void DrawRows(string id, VariableTableView view, IReadOnlyList<VariableRow> rows)
    {
        if (rows.Count == 0) return;

        var columns = view.Columns.Visible;
        if (!ImGui.BeginTable($"##vt_{id}", columns.Count,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        foreach (var c in columns)
        {
            if (c == VariableColumn.Type)
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 90f);
            else
                ImGui.TableSetupColumn(c.ToString(), ImGuiTableColumnFlags.WidthStretch);
        }
        ImGui.TableHeadersRow();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var highlight = view.HighlightOf(row);

            ImGui.TableNextRow();
            // ⭐ Pending wins the row tint when both apply: "my edit has not landed" is the actionable
            //   one, and §4a's requirement is that the two remain DISTINCT states -- which they do,
            //   because both booleans survive on the view for anything that needs them.
            if (highlight.Pending)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(PendingTint));
            else if (highlight.Changed)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ChangedTint));

            ImGui.PushID(i);
            foreach (var c in columns) DrawCell(c, view, row);
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawCell(VariableColumn column, VariableTableView view, VariableRow row)
    {
        ImGui.TableNextColumn();
        if (row.IsStale) ImGui.PushStyleColor(ImGuiCol.Text, StaleText);

        switch (column)
        {
            case VariableColumn.Name:
                ImGui.Selectable(view.DisplayNameOf(row));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(VariableRowGrouping.FullPathTooltip(row));   // ⭐ full path, always
                    // ⭐ Double-click disambiguates BY CELL -- extending the existing convention, not
                    //   overriding it: the NAME cell opens the whole properties object.
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && row.CanEverBeWritten)
                        PropertiesRequested?.Invoke(row);
                }
                DrawRowMenu(row);
                break;

            case VariableColumn.Type:
                ImGui.TextUnformatted(row.TypeText);
                break;

            case VariableColumn.Value:
                ImGui.TextUnformatted(_formatter.Cell(row, view.ValueMode));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(_formatter.Tooltip(row, view.ValueMode));
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && row.CanEverBeWritten)
                        EditValueRequested?.Invoke(row);
                }
                break;
        }

        if (row.IsStale) ImGui.PopStyleColor();
    }

    /// <summary>
    /// ⭐⭐ The row menu (checklist 2.26). Right-click on the NAME cell — the same cell whose
    /// double-click opens Properties, so the two gestures share one target.
    ///
    /// <para>⛔ <b>Rename is deliberately ABSENT, and that is a finding, not an omission.</b> A
    /// <c>VariableRow</c> is an OBSERVATION — <c>(AssetId, Entity, Section, VariablePath)</c> plus a
    /// byte reader. It carries no asset handle, no schema source and no undo recorder, so there is
    /// nothing here that could rename a declaration. The blueprint side renames through
    /// <c>BlueprintDocumentFactory.RegisterMyBlueprintItemCommands</c>, off the My Blueprint OUTLINE,
    /// which does hold the asset. ⇒ ⭐ rename belongs to the outline, and offering a greyed entry here
    /// would restate the same "built but inert" shape this batch exists to remove.</para>
    ///
    /// <para>⚠ Both live entries respect <c>CanEverBeWritten</c>, so a stale or node-owned row shows
    /// them disabled rather than firing a dialog that would refuse.</para>
    /// </summary>
    private void DrawRowMenu(VariableRow row)
    {
        if (!ImGui.BeginPopupContextItem()) return;

        bool writable = row.CanEverBeWritten;

        if (ImGui.MenuItem("Edit value…", null, false, writable))
            EditValueRequested?.Invoke(row);

        if (ImGui.MenuItem("Properties…", null, false, writable))
            PropertiesRequested?.Invoke(row);

        ImGui.EndPopup();
    }
}
