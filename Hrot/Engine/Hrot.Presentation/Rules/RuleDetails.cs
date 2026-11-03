using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Presentation.Behavior;
using Hrot.Presentation.Edits;
using ImGuiNET;
using StructEdit.Core;
using StructEdit.Reflection;

namespace Hrot.UI.Common.Rules;

/// <summary>One entry of a rule entity's brain journal (the Brain tab's own text — <c>BrainJournalText</c>).</summary>
public sealed record RuleJournalEntry(uint Seq, float At, string Kind, string Text);

/// <summary>What the host gives the rule editor: the ONE params form's service, the behaviour registry (the ONE params-type lookup), the edit-mode apply (the AI section's <c>EntityAiEditModel.ApplyTask</c>), the shared undo history, the journal reader.</summary>
public sealed record RuleEditorServices(
    Func<IComponentEditService?> EditService, Func<Fdp.Toolkit.Behavior.BehaviorRegistry?> Registry,
    Func<Entity, string, string, bool> ApplyEditMode, EditHistory History,
    Func<Entity, IReadOnlyList<RuleJournalEntry>> Journal, Func<IMapPickContext?>? Picks = null);

/// <summary>What the editor drew, as text — the rails read it (and an agent through the panel snapshot).</summary>
public sealed record RuleDetailsProbe(string Tab, IReadOnlyList<string> Tabs, IReadOnlyList<string> Fields, bool Editable, string? ReadOnlyReason, bool Dirty,
                                      string? Message, IReadOnlyList<string> History, IReadOnlyList<string> Results);

/// <summary>
/// ⭐⭐ CE-4218 (rules view R5a; docs/DESIGN_Rules_View_UI.md §7 RE-1 … RE-7, A1) — the RULE EDITOR: the right pane of the rules window as a TABBED panel over the selected rule entity's ONE params document (<c>BehaviorParamsEditor</c> → <c>BehaviorParamsForm</c>,
/// the form the AI section and the mission panel draw — ⛔ no second form). <see cref="RuleTabs"/> chooses each tab's fields; the header (<c>common</c>) sits above every tab. <b>Apply / Revert</b> (no write per keystroke): in EDIT mode Apply is the AI section's
/// <c>AssignNow</c> on the scenario entity and pushes ONE entry to the shared <see cref="EditHistory"/> whose undo re-applies the previous JSON. <b>Read-only</b> (the fields drawn disabled, WITH the reason): a rule from a macro (A1: <i>from macro X — edit the macro or its args</i>,
/// with a jump to the instance), a rule that is RUNNING (<i>live edits come in R5b</i> — the RV-5 buttons keep working), a recording. <b>History</b> = the entity's brain journal; <b>Results</b> = its children with pass / fail (the summary's <c>Why</c>).
/// </summary>
public sealed class RuleDetails
{
    public const string LiveReadOnly = "live edits come in R5b";
    public const string PlaceholderR5b = "R5b";

    private readonly RuleEditorServices _svc;
    private readonly Func<EntityRepository?> _world;
    private readonly Func<string?> _blocked;
    private readonly Action<int> _selectEntity;
    private readonly Action<RuleRow, double> _drawOverview;
    private readonly BehaviorParamsEditor _editor;
    private int _boundRule;
    private string? _pending;
    private RuleTab? _requestTab;
    private readonly Dictionary<int, RuleTab> _tabOf = new();
    private RuleDetailsProbe _probe = new("", Array.Empty<string>(), Array.Empty<string>(), false, null, false, null, Array.Empty<string>(), Array.Empty<string>());

    public RuleDetails(RuleEditorServices services, Func<EntityRepository?> world, Func<string?> blocked, Action<int> selectEntity, Action<RuleRow, double> drawOverview)
    {
        _svc = services ?? throw new ArgumentNullException(nameof(services));
        _world = world; _blocked = blocked; _selectEntity = selectEntity; _drawOverview = drawOverview;
        _editor = new BehaviorParamsEditor(_svc.EditService() ?? throw new InvalidOperationException("The rule editor needs the params form's edit service."));
    }

    public RuleDetailsProbe Probe => _probe;

    /// <summary>Where the Undo button was drawn (screen space) — a frame rail clicks it.</summary>
    internal (Vector2 Min, Vector2 Max) UndoRect { get; private set; }
    public string? Message { get; private set; }
    public RuleTab CurrentTab(RuleRow row) => _tabOf.TryGetValue(row.NetworkId, out var t) && RuleTabs.TabsFor(row.Status.Role).Contains(t) ? t : RuleTabs.TabsFor(row.Status.Role)[0];
    public void RequestTab(RuleTab tab) => _requestTab = tab;

    /// <summary>The entity's authored behaviour name and params JSON (its <c>BehaviorStartRecord</c>), or null.</summary>
    public (string Name, string Json)? ParamsOf(RuleRow row)
    {
        var w = _world();
        if (w == null || !w.IsAlive(row.Entity) || !w.TryGetTable(typeof(BehaviorStartRecord), out _) || !w.HasManagedComponent<BehaviorStartRecord>(row.Entity)) return null;
        var rec = ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<BehaviorStartRecord>(row.Entity);
        return rec == null ? null : (rec.BehaviorName, rec.JsonParams);
    }

    /// <summary>Why the fields cannot be edited, or null when they can (edit mode, an own rule).</summary>
    public string? ReadOnlyReason(RuleRow row)
    {
        if (_blocked() is { } b) return b;
        if (row.Macro != null) return $"from macro {row.Macro} — edit the macro or its args";                 // A1
        if (!row.Authored) return LiveReadOnly;                                                              // running: R5b
        if (ParamsOf(row) is not { } p) return "this rule has no authored params on this node";
        if (BehaviorParamsForm.ParamsTypeOf(p.Name, _svc.Registry()) is null) return $"{p.Name} has no params form";
        return null;
    }

    /// <summary>Binds the form to <paramref name="row"/>'s params and settles the probe (no ImGui) — <see cref="Draw"/> calls it first; a rail may call it alone.</summary>
    public void Prepare(RuleRow row, double now)
    {
        if (row.NetworkId != _boundRule) { _boundRule = row.NetworkId; _pending = null; _editor.Sync(null, null); }
        var p = ParamsOf(row);
        var type = p is { } pp ? BehaviorParamsForm.ParamsTypeOf(pp.Name, _svc.Registry()) : null;
        if (p is { } pr && type != null)
        {
            _editor.Sync(type, pr.Json);
            if (_editor.Edited() is { } edited) _pending = edited;
        }
        else _editor.Sync(null, null);
        var tab = CurrentTab(row);
        var fields = type == null ? Array.Empty<string>() : (tab == RuleTab.Overview || tab == RuleTab.Status ? Array.Empty<string>() : RuleTabs.FieldsOf(type, tab).ToArray());
        string? ro = ReadOnlyReason(row);
        bool dirty = _pending != null && p is { } cur && _pending != cur.Json;
        _probe = new RuleDetailsProbe(tab.ToString(), RuleTabs.TabsFor(row.Status.Role).Select(RuleTabs.TitleOf).ToList(),
            (type != null ? new[] { RuleTabs.HeaderField } : Array.Empty<string>()).Concat(fields).ToList(), ro == null, ro, dirty, Message,
            tab == RuleTab.History ? _svc.Journal(row.Entity).Select(j => $"{j.At:0.0}s {j.Text}").ToList() : (IReadOnlyList<string>)Array.Empty<string>(),
            tab == RuleTab.Results && _world() is { } w ? MissionSummaryModel.Build(w, new SummaryOptions(SummarySide.Both), null, now).Why(row.NetworkId).Select(x => $"{x.Title} | {(x.Go is { } g ? (g ? "pass" : "fail") : "—")}").ToList() : (IReadOnlyList<string>)Array.Empty<string>());
    }

    /// <summary>⭐ RE-7 edit mode — writes the edited params to the scenario entity (the AI section's <c>ApplyTask</c> → <c>AssignNow</c>) and records ONE undo entry. False (and a message) when refused or there is nothing to apply.</summary>
    public bool Apply(RuleRow row)
    {
        if (ReadOnlyReason(row) is { } ro) { Message = ro; return false; }
        if (_pending is not { } json || ParamsOf(row) is not { } cur || json == cur.Json) { Message = "nothing to apply"; return false; }
        string before = cur.Json, name = cur.Name;
        var entity = row.Entity;
        if (!_svc.ApplyEditMode(entity, name, json)) { Message = "refused: the assignment was not accepted"; return false; }
        _svc.History.Push($"edit {row.Title}", () => _svc.ApplyEditMode(entity, name, json), () => _svc.ApplyEditMode(entity, name, before));
        _pending = null;
        Message = $"applied — undo: \"edit {row.Title}\"";
        return true;
    }

    /// <summary>Test hook — stages <paramref name="json"/> as the form's pending edit (what a drawn field edit produces).</summary>
    internal void StagePending(string json) => _pending = json;

    /// <summary>Drops the edits not applied: the form reopens from the entity's params.</summary>
    public void Revert(RuleRow row)
    {
        _pending = null;
        if (ParamsOf(row) is { } p && BehaviorParamsForm.ParamsTypeOf(p.Name, _svc.Registry()) is { } t) { _editor.Sync(null, null); _editor.Sync(t, p.Json); }
        Message = "reverted";
    }

    /// <summary>Undo / redo through the ONE history (the Parameters window's too).</summary>
    public bool Undo() { bool ok = _svc.History.Undo(); Message = ok ? "undone" : "nothing to undo"; _pending = null; _editor.Sync(null, null); return ok; }
    public bool Redo() { bool ok = _svc.History.Redo(); Message = ok ? "redone" : "nothing to redo"; _pending = null; _editor.Sync(null, null); return ok; }

    // ── drawing ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    public void Draw(RuleRow row, double now)
    {
        Prepare(row, now);
        string? ro = ReadOnlyReason(row);
        var p = ParamsOf(row);
        var type = p is { } pp ? BehaviorParamsForm.ParamsTypeOf(pp.Name, _svc.Registry()) : null;

        if (ro != null) ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f), ro);
        if (row.Macro != null && row.MacroInstance != 0 && ImGui.SmallButton("Go to the macro instance")) _selectEntity(row.MacroInstance);

        // the tab strip: one button per tab, the current one lit (a plain strip — ImGui's tab bar cannot be set from a rail)
        var cur = CurrentTab(row);
        if (_requestTab is { } req && RuleTabs.TabsFor(row.Status.Role).Contains(req)) { _tabOf[row.NetworkId] = req; cur = req; _requestTab = null; }
        foreach (var tab in RuleTabs.TabsFor(row.Status.Role))
        {
            bool on = tab == cur;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            if (ImGui.Button(RuleTabs.TitleOf(tab) + "##rt" + tab)) { _tabOf[row.NetworkId] = tab; cur = tab; }
            if (on) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.BeginChild("##rule-tab-body", new Vector2(0, Math.Max(120f, ImGui.GetContentRegionAvail().Y - 64f)), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        DrawTab(row, cur, type, ro, now);
        ImGui.EndChild();
        DrawApplyBar(row, ro);
    }

    private void DrawTab(RuleRow row, RuleTab tab, Type? type, string? ro, double now)
    {
        switch (tab)
        {
            case RuleTab.Overview: case RuleTab.Status: _drawOverview(row, now); return;
            case RuleTab.Bindings: ImGui.TextDisabled(PlaceholderR5b); return;
            case RuleTab.Do: ImGui.TextDisabled($"The step list editor — {PlaceholderR5b}"); return;
            case RuleTab.History:
                var entries = _svc.Journal(row.Entity);
                if (entries.Count == 0) ImGui.TextDisabled("No journal (the rule is not running on this node).");
                foreach (var j in entries) ImGui.TextUnformatted($"{j.At:0.0}s  {j.Text}");
                return;
            case RuleTab.Results:
                var why = _world() is { } w ? MissionSummaryModel.Build(w, new SummaryOptions(SummarySide.Both), null, now).Why(row.NetworkId) : Array.Empty<WhyRow>();
                if (why.Count == 0) ImGui.TextDisabled("Nothing under it.");
                foreach (var x in why)
                    ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(x.Go is { } g ? (g ? RulesModel.Achieved : RulesModel.Failed) : RulesModel.Grey),
                                      $"{x.Title}  {(x.IsMeasure ? $"{x.Value:0.##} vs {x.Target:0.##}" : x.Score.ToString("0.00"))}  weight {x.Weight:0.##}{(x.Required ? "  required ✓" : "")}");
                return;
        }
        if (type == null || _editor.Session is not { } session) { ImGui.TextDisabled(ro ?? "no params form"); return; }
        var picks = _svc.Picks?.Invoke();
        if (ro != null) ImGui.BeginDisabled();
        ImGui.TextDisabled("Header");
        BehaviorParamsForm.DrawFields(session, picks, "##rule-common", null, new HashSet<string> { RuleTabs.HeaderField });
        ImGui.Spacing();
        BehaviorParamsForm.DrawFields(session, picks, "##rule-" + tab, null, RuleTabs.FieldsOf(type, tab).ToHashSet());
        if (ro != null) ImGui.EndDisabled();
    }

    private void DrawApplyBar(RuleRow row, string? ro)
    {
        ImGui.Separator();
        bool dirty = _probe.Dirty;
        if (ro != null || !dirty) ImGui.BeginDisabled();
        if (ImGui.Button("Apply")) Apply(row);
        ImGui.SameLine();
        if (ImGui.Button("Revert")) Revert(row);
        if (ro != null || !dirty) ImGui.EndDisabled();
        if (ro != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(ro);
        ImGui.SameLine();
        // ⛔ read CanUndo / CanRedo ONCE: a click changes them between Begin- and EndDisabled, and ImGui asserts (found live: the editor died on the first Undo click)
        bool canUndo = _svc.History.CanUndo, canRedo = _svc.History.CanRedo;
        string undoLabel = _svc.History.UndoLabel is { } ul ? $"Undo {ul}" : "Undo";
        if (!canUndo) ImGui.BeginDisabled();
        bool undo = ImGui.Button(undoLabel);
        UndoRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (!canUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!canRedo) ImGui.BeginDisabled();
        bool redo = ImGui.Button("Redo");
        if (!canRedo) ImGui.EndDisabled();
        if (undo) Undo();
        if (redo) Redo();
        if (Message != null) ImGui.TextDisabled(Message);
    }
}
