using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Hrot.Presentation.Panels.Breakpoints;

/// <summary>⭐ One breakpoint row, projected for the dump. Mirrors <see cref="DataBreakpointManagerPanel.DrawGrid"/>'s
/// columns; <c>TypeName</c>/<c>Summary</c> reuse the SAME <see cref="DataBreakpointManagerPanel.GetTypeName"/>
/// and <c>BreakpointConditionSummarizer.Summarize</c> the draw calls.</summary>
public sealed record DataBreakpointRowViewModel(string Id, bool Enabled, string Scope, string TypeName, string Summary, int HitCount,
    string Kind = "Data", string Name = "");

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="DataBreakpointManagerPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ <see cref="Banner"/> embeds
/// <c>TemporalStatusBannerPanel</c>'s own dump — that sub-panel has no standalone window anywhere
/// (group-6 queue item; caller-registers rule), so its ONLY caller embeds it here.</summary>
public sealed record DataBreakpointManagerPanelViewModel(
    string PanelId, string PanelKind, string? SelectedId,
    IReadOnlyList<DataBreakpointRowViewModel> Breakpoints, TemporalStatusBannerViewModel Banner) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Data-grid panel for the Data Breakpoint Manager window.
/// Draws the toolbar row (Add / Remove / Enable All / Disable All / JSON),
/// the breakpoint data grid (Enabled, Scope, Type, Summary, Hits),
/// and the temporal status banner at the bottom.
/// </summary>
public sealed class DataBreakpointManagerPanel
{
    private readonly IDataBreakpointManager _manager;
    private readonly TemporalStatusBannerPanel _bannerPanel;
    private readonly Func<SearchPredicateDto>? _createDefaultPredicate;
    private readonly Func<Fdp.Core.Entity, string>? _entityLabel;
    private readonly Action<Fdp.Core.Entity>? _selectEntity;
    private BreakpointId _selectedId;
    private bool _showCondition;
    /// <summary>⭐ U-16 / C5 (DESIGN_Condition_Editor §6) — the friendly view of the selected breakpoint's condition: the ONE condition editor in the Breakpoint context (the facts plus the debugging kinds; a kind with no fact reads back as one Custom row).</summary>
    private readonly Fdp.Presentation.Editing.ConditionEditor _conditions = new(Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionContexts.Breakpoint);
    private bool _expert;
    // ⭐ U-6 (CE-4336) — "+ Add" opens the CONDITION first: the draft lives here, no row exists until the person presses "Add breakpoint"
    private bool _adding;
    private SearchPredicateDto? _addDraft;
    private BreakpointId _draftOf;
    private SearchPredicateDto? _draftBase, _draft;

    /// <summary>The ONE map pick context the friendly view's area / unit chips pick with (the host injects it); null ⇒ the chips take no map pick.</summary>
    public Fdp.Presentation.Editing.IMapPickContext? PickContext { get; set; }
    private int _kindFilter;   // 0 = all, else (int)BreakpointKindOf + 1

    /// <param name="entityLabel">⭐ CE-1285 — how the Scope column names a unit (its network id); default the raw handle.</param>
    /// <param name="selectEntity">⭐ CE-1285 — a click on a row selects its unit (the host's selection); null ⇒ no selection.</param>
    public DataBreakpointManagerPanel(
        IDataBreakpointManager manager,
        TemporalStatusBannerState bannerState,
        Func<SearchPredicateDto>? createDefaultPredicate = null,
        Func<Fdp.Core.Entity, string>? entityLabel = null,
        Action<Fdp.Core.Entity>? selectEntity = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _bannerPanel = new TemporalStatusBannerPanel(bannerState);
        _createDefaultPredicate = createDefaultPredicate;
        _entityLabel = entityLabel;
        _selectEntity = selectEntity;
    }

    private string ScopeOf(Breakpoint bp)
        => bp.FilterEntity is { } e ? (_entityLabel?.Invoke(e) ?? $"Entity {e}") : "Global";

    /// <summary>⭐ CE-1285 — the row's text: its name when it has one (a node, a journal break), else the readable condition.</summary>
    private static string NameOf(Breakpoint bp)
        => bp.DisplayName.Length > 0 ? bp.DisplayName : BreakpointConditionSummarizer.Summarize(bp.Condition);

    /// <summary>Currently selected breakpoint, or <see cref="BreakpointId.Invalid"/>.</summary>
    public BreakpointId SelectedId => _selectedId;

    /// <summary>
    /// Main draw entry-point. Must be called from within an active ImGui frame
    /// inside the owning <see cref="DataBreakpointManagerWindow.DrawClientArea"/>.
    /// </summary>
    public void DrawContent()
    {
        ConditionTop = 0f;
        DrawToolbar();
        DrawGrid();
        DrawPredicateEditor();
        DrawBanner();
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of the breakpoint grid. No ImGui. ⭐ Reuses
    /// <see cref="GetTypeName"/> and <c>BreakpointConditionSummarizer.Summarize</c>, the SAME
    /// functions <see cref="DrawGrid"/> calls.</summary>
    public DataBreakpointManagerPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        var rows = _manager.AllBreakpoints.Select(bp => new DataBreakpointRowViewModel(
            bp.Id.ToString(),
            bp.Enabled,
            ScopeOf(bp),
            GetTypeName(bp.Condition),
            BreakpointConditionSummarizer.Summarize(bp.Condition),
            bp.HitCount,
            BreakpointKind.Of(bp).ToString(),
            NameOf(bp))).ToList();

        return new DataBreakpointManagerPanelViewModel(
            panelId, panelKind, _selectedId.IsValid ? _selectedId.ToString() : null, rows, _bannerPanel.BuildViewModel());
    }

    // ── Internal action seams (used by tests) ─────────────────────────────────

    /// <summary>⭐ U-6 (CE-4336) — the "+ Add" toolbar button: opens the condition editor FIRST (over the host's default condition when it has one, else an empty one); ⛔ no row is added until <see cref="CommitAdd"/> — a breakpoint
    /// with no condition chosen never lands in the list as "New Breakpoint".</summary>
    internal void StartAdd()
    {
        _adding = true;
        _addDraft = _createDefaultPredicate?.Invoke();
        _showCondition = false;
    }

    /// <summary>Whether the new-breakpoint condition editor is open.</summary>
    internal bool Adding => _adding;

    /// <summary>"Add breakpoint": adds the drafted condition, selects the new row (its condition then shows), closes the new-breakpoint editor. False when there is nothing valid to add (no condition, or a blank left unfilled).</summary>
    internal bool CommitAdd()
    {
        if (!_adding || _addDraft == null || _conditions.Problem != null) return false;
        var id = _manager.AddBreakpoint(_addDraft);   // ⭐ no display name: the row reads as the condition's own sentence
        _selectedId = id;
        _showCondition = true;
        _adding = false;
        _addDraft = null;
        return true;
    }

    /// <summary>"Cancel": closes the new-breakpoint editor; nothing was added.</summary>
    internal void CancelAdd() { _adding = false; _addDraft = null; }

    /// <summary>Removes the currently selected breakpoint. Mirrors the "-Remove" toolbar button.</summary>
    internal void RemoveSelected()
    {
        if (!_selectedId.IsValid) return;
        _manager.Remove(_selectedId);
        _selectedId = BreakpointId.Invalid;
    }

    /// <summary>Enables all registered breakpoints. Mirrors the "Enable All" toolbar button.</summary>
    internal void EnableAll()
    {
        foreach (var bp in _manager.AllBreakpoints)
            _manager.SetEnabled(bp.Id, true);
    }

    /// <summary>Disables all registered breakpoints. Mirrors the "Disable All" toolbar button.</summary>
    internal void DisableAll()
    {
        foreach (var bp in _manager.AllBreakpoints)
            _manager.SetEnabled(bp.Id, false);
    }

    /// <summary>Toggles the Enabled state of a specific breakpoint. Called from the row checkbox.</summary>
    internal void ToggleEnabled(BreakpointId id)
    {
        var bps = _manager.AllBreakpoints;
        foreach (var bp in bps)
        {
            if (bp.Id == id)
            {
                _manager.SetEnabled(id, !bp.Enabled);
                return;
            }
        }
    }

    // ── ImGui drawing ─────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        if (ImGuiApi.Button("+ Add"))
            StartAdd();
        ImGuiApi.SameLine();
        // ⭐ CE-1285 — ⛔ the "- Remove" button is gone: a row's own menu deletes it (🔒 user: "the button on top … can be removed once we
        //   have context menu"). ⭐ Q44-A: one list, a Kind FILTER (not tabs, which would re-split it)
        ImGuiApi.SetNextItemWidth(110f);
        ImGuiApi.Combo("##kind", ref _kindFilter, "All kinds\0Data\0Node\0Journal\0");
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Enable All"))
            EnableAll();

        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Disable All"))
            DisableAll();

        ImGuiApi.SameLine();
        if (ImGuiApi.Button("{ } JSON"))
            DrawJsonPopup();
    }

    /// <summary>⭐⭐ <c>CE-1285</c> (DBG-6, Q44 R-97) — THE one list: every kind with a Kind column, resizable columns, a readable name, a
    /// row menu (Enable/Disable · Show condition · Select the unit · Delete), click selects the row and its unit, double-click shows the
    /// condition. 🔒 User: <i>"columns unsiazable … items have no context menus … clicking does nothing, doubleclicking does nothing"</i>.</summary>
    private void DrawGrid()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        // ⭐ U-6 (CE-4336) — a scrolling table with no height fills the WHOLE window and pushes the condition editor below it out of sight: while an editor shows (the new-breakpoint one, or a row's condition)
        //   the list keeps a bounded share so the editor is on screen. 📐 measured: Show condition "did nothing" because the editor drew below an unbounded table.
        bool editorBelow = _adding || (_showCondition && _selectedId.IsValid);
        float listHeight = editorBelow ? Math.Max(90f, ImGuiApi.GetContentRegionAvail().Y * 0.35f) : 0f;
        GridRect = default;
        var gridTop = ImGuiApi.GetCursorScreenPos();
        if (!ImGuiApi.BeginTable("##bpgrid", 5, flags, new Vector2(0f, listHeight))) return;
        ImGuiApi.TableSetupColumn("##en",  ImGuiTableColumnFlags.WidthFixed, 20f);
        ImGuiApi.TableSetupColumn("Kind",  ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGuiApi.TableSetupColumn("Breakpoint", ImGuiTableColumnFlags.WidthStretch);
        ImGuiApi.TableSetupColumn("Scope", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGuiApi.TableSetupColumn("Hits",  ImGuiTableColumnFlags.WidthFixed, 40f);
        ImGuiApi.TableHeadersRow();

        BreakpointId? toDelete = null;
        foreach (var bp in _manager.AllBreakpoints)
        {
            var kind = BreakpointKind.Of(bp);
            if (_kindFilter != 0 && (int)kind + 1 != _kindFilter) continue;
            ImGuiApi.TableNextRow();
            ImGuiApi.PushID(bp.Id.ToString());

            ImGuiApi.TableSetColumnIndex(0);
            bool enabled = bp.Enabled;
            if (ImGuiApi.Checkbox("##en", ref enabled))
                ToggleEnabled(bp.Id);

            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.TextUnformatted(kind.ToString());

            ImGuiApi.TableSetColumnIndex(2);
            bool isSelected = _selectedId == bp.Id;
            if (ImGuiApi.Selectable(NameOf(bp) + "##sel", isSelected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedId = bp.Id;
                if (bp.FilterEntity is { } e) _selectEntity?.Invoke(e);
                if (ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left)) _showCondition = true;
            }
            if (ImGuiApi.IsItemHovered()) ImGuiApi.SetTooltip(BreakpointConditionSummarizer.Summarize(bp.Condition));
            if (ImGuiApi.BeginPopupContextItem("##row"))
            {
                _selectedId = bp.Id;
                if (ImGuiApi.MenuItem(bp.Enabled ? "Disable" : "Enable")) ToggleEnabled(bp.Id);
                // ⭐ U-6 (CE-4336) — on ANY row (it selects the row first); a second pick on the row whose condition shows hides it
                if (ImGuiApi.MenuItem("Show condition", null, _showCondition && _selectedId == bp.Id)) ToggleCondition(bp.Id);
                if (bp.FilterEntity is { } unit && _selectEntity != null && ImGuiApi.MenuItem("Select the unit")) _selectEntity(unit);
                ImGuiApi.Separator();
                if (ImGuiApi.MenuItem("Delete")) toDelete = bp.Id;
                ImGuiApi.EndPopup();
            }

            ImGuiApi.TableSetColumnIndex(3);
            ImGuiApi.TextUnformatted(ScopeOf(bp));
            ImGuiApi.TableSetColumnIndex(4);
            ImGuiApi.TextUnformatted(bp.HitCount.ToString());
            ImGuiApi.PopID();
        }
        ImGuiApi.EndTable();
        GridRect = (gridTop, ImGuiApi.GetCursorScreenPos());   // the list's top-left and the line under it
        if (toDelete is { } d)
        {
            _manager.Remove(d);
            if (_selectedId == d) _selectedId = BreakpointId.Invalid;
        }
    }

    /// <summary>
    /// Renders the predicate condition tree for the selected breakpoint.
    /// Compound children listed in <see cref="CompoundPredicateDto.ReadOnlyChildIndices"/>
    /// are rendered inside <c>ImGui.BeginDisabled()</c> so the user cannot edit them.
    /// </summary>
    private void DrawPredicateEditor()
    {
        if (_adding) { DrawNewBreakpointEditor(); return; }
        if (!_selectedId.IsValid || !_showCondition) return;   // ⭐ CE-1285 — shown on double-click / "Show condition"
        ConditionTop = ImGuiApi.GetCursorScreenPos().Y;

        var bp = _manager.AllBreakpoints.FirstOrDefault(b => b.Id == _selectedId);
        if (bp == null) return;

        // a compound with LOCKED children (a node's own link to its visual node) can only be read — the friendly view would let them drift, so it stays on the Expert (read-only) view
        bool locked = bp.Condition is CompoundPredicateDto lockedCompound && lockedCompound.ReadOnlyChildIndices?.Count > 0;
        bool expert = _expert || locked;
        ImGuiApi.BeginDisabled(locked);
        if (ImGuiApi.Checkbox("Expert view", ref expert) && !locked) _expert = expert;
        ImGuiApi.EndDisabled();

        if (!expert)
        {
            // a draft of the condition: edited in the sentence rows, written back by Apply — never while a blank is unfilled (§4)
            if (_draftOf != bp.Id || !ReferenceEquals(_draftBase, bp.Condition)) { _draftOf = bp.Id; _draftBase = bp.Condition; _draft = bp.Condition; }
            ImGuiApi.SeparatorText("Condition");
            _conditions.Draw("bp-when", ref _draft, PickContext);
            FriendlyDraws++;
            if (_conditions.ExpertRequested) { _expert = true; _conditions.ExpertRequested = false; }
            bool dirty = !ReferenceEquals(_draft, _draftBase);
            bool blank = _conditions.Problem != null;
            ImGuiApi.BeginDisabled(!dirty || blank || _draft == null);
            if (ImGuiApi.Button("Apply")) { _manager.UpdateCondition(bp.Id, _draft); _draftBase = _draft; }
            ImGuiApi.EndDisabled();
            if (blank) { ImGuiApi.SameLine(); ImGuiApi.TextDisabled(_conditions.Problem!); }
            return;
        }

        if (bp.Condition is CompoundPredicateDto compound)
        {
            ImGuiApi.SeparatorText("Condition (Compound)");
            ImGuiApi.TextUnformatted($"Operator: {compound.Operator}");

            for (int i = 0; i < compound.Conditions.Count; i++)
            {
                bool readOnly = CompoundPredicateHelper.IsChildReadOnly(compound, i);
                if (readOnly) ImGuiApi.BeginDisabled();

                ImGuiApi.TextUnformatted(
                    $"  [{i}]{(readOnly ? " (locked)" : "")} {BreakpointConditionSummarizer.Summarize(compound.Conditions[i])}");

                if (readOnly) ImGuiApi.EndDisabled();
            }
        }
        else if (bp.Condition != null)
        {
            ImGuiApi.SeparatorText("Condition");
            ImGuiApi.TextUnformatted(BreakpointConditionSummarizer.Summarize(bp.Condition));
        }
    }

    /// <summary>⭐ U-6 (CE-4336) — the new-breakpoint editor: the SAME friendly condition editor as a row's, over the draft; "Add breakpoint" holds while a blank is unfilled or nothing is chosen (the reason beside it).</summary>
    private void DrawNewBreakpointEditor()
    {
        ConditionTop = ImGuiApi.GetCursorScreenPos().Y;
        ImGuiApi.SeparatorText("New breakpoint - when should it stop?");
        _conditions.Draw("bp-new", ref _addDraft, PickContext);
        FriendlyDraws++;
        bool blank = _conditions.Problem != null;
        bool empty = _addDraft == null;
        ImGuiApi.BeginDisabled(blank || empty);
        if (ImGuiApi.Button("Add breakpoint")) CommitAdd();
        ImGuiApi.EndDisabled();
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Cancel")) CancelAdd();
        if (blank) { ImGuiApi.SameLine(); ImGuiApi.TextDisabled(_conditions.Problem!); }
        else if (empty) { ImGuiApi.SameLine(); ImGuiApi.TextDisabled("choose a condition first"); }
    }

    /// <summary>The screen Y where the condition editor (a row's, or the new-breakpoint one) started this frame; 0 when none was drawn (a rail checks it is inside the window).</summary>
    internal float ConditionTop { get; private set; }

    /// <summary>The breakpoint list's rectangle as drawn this frame (a rail reads it).</summary>
    internal (Vector2 Min, Vector2 Max) GridRect { get; private set; }

    /// <summary>How many frames drew the friendly view (rails: it stops counting while the Expert view shows).</summary>
    internal int FriendlyDraws { get; private set; }
    /// <summary>The friendly view's sentence rows as drawn (rails).</summary>
    internal IReadOnlyList<string> ConditionRows => _conditions.Sentences;
    /// <summary>The first unfilled blank of the friendly view, or null (Apply holds while it is set).</summary>
    internal string? ConditionProblem => _expert ? null : _conditions.Problem;
    /// <summary>Whether the Expert (read-only listing) view is showing.</summary>
    internal bool ExpertView { get => _expert; set => _expert = value; }
    /// <summary>⭐ U-6 (CE-4336) — the row menu's <i>Show condition</i>: on ANY row (it selects the row), a second pick on the row whose condition shows hides it. The new-breakpoint editor closes (one editor below the list).</summary>
    internal void ToggleCondition(BreakpointId id)
    {
        bool shownHere = _showCondition && _selectedId == id;
        _selectedId = id;
        _showCondition = !shownHere;
        if (_showCondition) _adding = false;
    }

    /// <summary>The new-breakpoint draft (a rail fills it as the condition editor's clicks would).</summary>
    internal SearchPredicateDto? AddDraft { get => _addDraft; set => _addDraft = value; }

    /// <summary>Selects a breakpoint and opens its condition (a seam for rails; the UI does it with a double-click).</summary>
    internal void ShowCondition(BreakpointId id) { _selectedId = id; _showCondition = true; }

    private void DrawBanner()
    {
        _bannerPanel.Draw(text =>
        {
            ImGuiApi.Separator();
            ImGuiApi.TextColored(new Vector4(1f, 0.85f, 0f, 1f), text);
        });
    }

    private void DrawJsonPopup()
    {
        // Copy selected breakpoint's condition to clipboard
        if (!_selectedId.IsValid) return;
        foreach (var bp in _manager.AllBreakpoints)
        {
            if (bp.Id != _selectedId) continue;
            if (bp.Condition != null)
                ImGuiApi.SetClipboardText(BreakpointJsonClipboard.Serialize(bp.Condition));
            return;
        }
    }

    private static string GetTypeName(SearchPredicateDto? dto) => dto switch
    {
        PropertyMatchDto           => "Component",
        TransientEventPredicateDto => "Event",
        BehaviorParamPredicateDto  => "BParam",
        StructuralPredicateDto     => "Structural",
        SpatialBoundingPredicateDto => "Spatial",
        LifecyclePredicateDto      => "Lifecycle",
        TraceBufferScanPredicateDto => "Trace",
        CompoundPredicateDto       => "Compound",
        BlueprintVariablePredicateDto => "Blueprint",
        ExternalHitTagPredicateDto => "ExtTag",
        _                          => "Unknown",
    };
}
