using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Variables;
using ImGuiNET;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>One row of a typed args form: the header's param, its default, and the override (if any).</summary>
public sealed record ParamArgRow(string Name, string TypeId, Type? Type, string? Comment, string? DefaultJson, string? OverrideJson)
{
    public bool IsOverridden => OverrideJson is not null;

    /// <summary>What the form shows and a run will use: the override, else the default (<c>null</c> literal when neither).</summary>
    public string EffectiveJson => OverrideJson ?? DefaultJson ?? "null";

    /// <summary>One row per declared param of <paramref name="decls"/>, in declaration order, each carrying the matching entry of <paramref name="overrides"/> (a name → value object) if there is one.</summary>
    public static IReadOnlyList<ParamArgRow> From(IReadOnlyList<ScenarioParamDecl>? decls, JsonObject overrides)
    {
        if (decls is not { Count: > 0 }) return Array.Empty<ParamArgRow>();
        return decls.Select(d =>
        {
            ParamTypes.TryResolve(d.TypeId, out var clr);
            return new ParamArgRow(d.Name, d.TypeId, clr, d.Comment, d.Default?.ToJsonString(),
                                   overrides.TryGetPropertyValue(d.Name, out var v) ? v?.ToJsonString() ?? "null" : null);
        }).ToList();
    }
}

/// <summary>
/// ⭐⭐ CE-4141 / CE-4210 MA-8 — THE typed one-field args form, shared by the macro instance's Details section (MA-8) and the scenario picker's <i>Run setup</i> expander (AQ108 D24): one table row per declared param, each drawn by the ONE component
/// drawer over a one-field typed holder (<see cref="DefaultValueAuthoring"/>, the precedent of a variable's default); the default is greyed until it is overridden; a <i>reset</i> button on an override. The caller owns what an edit MEANS:
/// <c>onSet</c> gets the row and the committed JSON, <c>onReset</c> the row. ⛔ Extracted from the macro view so there is not a second copy.
/// </summary>
public sealed class ParamArgsTable : IDisposable
{
    private sealed class RowState : IDisposable
    {
        public IEditSession Session = null!;
        public ComponentEditDrawer Drawer = null!;
        public string ShownJson = "";
        public Type Type = null!;
        public void Dispose() => Session?.Dispose();
    }

    private readonly Func<IComponentEditService?> _editService;
    private readonly Func<IMapPickContext?> _picks;
    private readonly string _pickScope;
    private readonly Dictionary<string, RowState> _rows = new();

    /// <summary>What the last <see cref="Draw"/> showed (read by the frame rails): the rows drawn and the reset buttons.</summary>
    public int RowsDrawn { get; private set; }
    public int ResetButtons { get; private set; }

    /// <summary>The screen centre of the last <i>reset</i> button drawn (the frame rails click it); null when none was drawn.</summary>
    public System.Numerics.Vector2? LastResetButtonCenter { get; private set; }

    /// <param name="pickScope">The map-pick scope prefix of a row's field (<c>"$.macroarg."</c>, <c>"$.runsetup."</c>).</param>
    public ParamArgsTable(Func<IComponentEditService?> editService, Func<IMapPickContext?>? picks = null, string pickScope = "$.arg.")
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        _picks = picks ?? (() => null);
        _pickScope = pickScope;
    }

    /// <summary>Zeroes the counters (a view that returns before drawing the table starts each frame from nothing).</summary>
    public void ResetCounts() { RowsDrawn = ResetButtons = 0; LastResetButtonCenter = null; }

    /// <summary>Draws the table. Returns false (and draws a muted line) when there is no editor service. <paramref name="id"/> keeps two tables on one window apart.</summary>
    public bool Draw(string id, IReadOnlyList<ParamArgRow> rows, Action<ParamArgRow, string> onSet, Action<ParamArgRow> onReset)
    {
        ResetCounts();
        if (_editService() is not { } svc) { ImGui.TextDisabled("No editor service."); return false; }
        ParamArgRow? resetClicked = null;
        if (ImGui.BeginTable(id, 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            try
            {
                ImGui.TableSetupColumn("Param", ImGuiTableColumnFlags.WidthFixed, 160f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70f);
                foreach (var row in rows)
                {
                    // ⭐ CE-4258 ① — one id scope per row: two rows of one type (two entity links) drew widgets with the same id
                    ImGui.PushID(row.Name);
                    try
                    {
                        var st = PrepareRow(svc, row);
                        if (st is null) continue;
                        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, row.IsOverridden ? 1f : 0.55f);   // the default is greyed until it is overridden
                        st.Drawer.DrawRoot(VariableEditModal.ScalarRowOrRoot(st.Session.Document.Root, row.Name));
                        ImGui.PopStyleVar();
                        if (FinishRow(row, st, onSet)) resetClicked = row;
                    }
                    finally { ImGui.PopID(); }
                }
            }
            finally { ImGui.EndTable(); }
        }
        // ⭐ CE-4258 ② — the reset runs AFTER the table: a caller's reset may Clear() (dispose) every row's session, which inside the row loop
        // left the next FinishRow reading a disposed session — the exception skipped EndTable ("missing EndTable") and crashed the editor
        if (resetClicked is not null) onReset(resetClicked);
        return true;
    }

    /// <summary>The row's session (opened or reopened from the effective JSON); null for an unknown type (its row is already written).</summary>
    private RowState? PrepareRow(IComponentEditService svc, ParamArgRow row)
    {
        if (row.Type is null)
        {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(row.Name);
            ImGui.TableSetColumnIndex(1); ImGui.TextDisabled($"unknown type '{row.TypeId}'");
            return null;
        }
        if (!_rows.TryGetValue(row.Name, out var st) || !string.Equals(st.ShownJson, row.EffectiveJson, StringComparison.Ordinal) || st.Type != row.Type)
        {
            st?.Dispose();
            var entry = new BlackboardVariableEntry(row.Name, row.Type, row.Comment, DefaultValueJson: row.EffectiveJson);
            var s = DefaultValueAuthoring.OpenSession(svc, entry);
            st = new RowState { Session = s, Drawer = new ComponentEditDrawer(s, _picks() is { } p ? new ScopedMapPickContext(p, _pickScope + row.Name) : null), ShownJson = row.EffectiveJson, Type = row.Type };
            _rows[row.Name] = st;
        }
        if (st.Session.RebuildState == EditRebuildState.RebuildRequired) st.Session.RebuildDocument();
        return st;
    }

    /// <summary>After the drawer drew the row: the reset column and the commit of an edit. Returns true when <i>reset</i> was clicked (the caller resets after the table).</summary>
    private bool FinishRow(ParamArgRow row, RowState st, Action<ParamArgRow, string> onSet)
    {
        RowsDrawn++;
        ImGui.TableSetColumnIndex(2);
        if (row.IsOverridden)
        {
            ResetButtons++;
            bool clicked = ImGui.SmallButton("reset");
            LastResetButtonCenter = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
            if (clicked) return true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset to the default" + (row.DefaultJson is null ? "" : " (" + row.DefaultJson + ")"));
        }
        else ImGui.TextDisabled("default");

        CommitIfDirty(row, st, onSet);
        return false;
    }

    /// <summary>The commit of an edit: when the form is dirty and valid, serialises it and tells the caller (<paramref name="onSet"/>) — shared by the table row and the one-cell form.</summary>
    private static void CommitIfDirty(ParamArgRow row, RowState st, Action<ParamArgRow, string> onSet)
    {
        if (st.Session.IsDirty)
        {
            try
            {
                string json = DefaultValueAuthoring.CommitAndSerialize(st.Session, row.Type!);
                if (!string.Equals(json, st.ShownJson, StringComparison.Ordinal))
                {
                    st.ShownJson = json;   // what the form now shows; the next frame's record carries it
                    onSet(row, json);
                }
            }
            catch (EditValidationException) { /* an invalid edit stays in the form; nothing is written */ }
        }
    }

    /// <summary>
    /// ⭐ CE-4254 — the ONE-CELL form of a row: the field type's StructEdit leaf, with no table and no reset column, for a host that already owns the table
    /// (the Variables table's <i>default</i> cell). Same session / drawer / commit as <see cref="Draw"/>; the caller owns what an edit means (<paramref name="onSet"/>).
    /// Returns false (a muted line) when there is no editor service or the type is unknown.
    /// </summary>
    public bool DrawCell(ParamArgRow row, Action<ParamArgRow, string> onSet)
    {
        if (_editService() is not { } svc) { ImGui.TextDisabled("No editor service."); return false; }
        if (row.Type is null) { ImGui.TextDisabled($"unknown type '{row.TypeId}'"); return false; }
        ImGui.PushID(row.Name);
        try
        {
            var st = PrepareRow(svc, row)!;
            // the drawer's leaf starts with TableNextRow — it needs its OWN two-column table, nested in the host's cell (else it would add a row to the host's table)
            if (ImGui.BeginTable("##defcell", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX))
            {
                try
                {
                    ImGui.TableSetupColumn("##n", ImGuiTableColumnFlags.WidthFixed, 1f);
                    ImGui.TableSetupColumn("##v", ImGuiTableColumnFlags.WidthStretch);
                    st.Drawer.DrawRoot(VariableEditModal.ScalarRowOrRoot(st.Session.Document.Root, row.Name));
                }
                finally { ImGui.EndTable(); }
            }
            CommitIfDirty(row, st, onSet);
        }
        finally { ImGui.PopID(); }
        return true;
    }

    /// <summary>The open edit session of row <paramref name="name"/> (null before it was drawn) — the frame rails edit through it as the row's widget does.</summary>
    internal IEditSession? SessionOf(string name) => _rows.TryGetValue(name, out var st) ? st.Session : null;

    /// <summary>Drops every row's session (the record changed, or the write was refused: reopen from the owner's own value next frame).</summary>
    public void Clear()
    {
        foreach (var r in _rows.Values) r.Dispose();
        _rows.Clear();
    }

    public void Dispose() => Clear();
}
