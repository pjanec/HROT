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
public sealed record DataBreakpointRowViewModel(string Id, bool Enabled, string Scope, string TypeName, string Summary, int HitCount);

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
    private BreakpointId _selectedId;

    public DataBreakpointManagerPanel(
        IDataBreakpointManager manager,
        TemporalStatusBannerState bannerState,
        Func<SearchPredicateDto>? createDefaultPredicate = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _bannerPanel = new TemporalStatusBannerPanel(bannerState);
        _createDefaultPredicate = createDefaultPredicate;
    }

    /// <summary>Currently selected breakpoint, or <see cref="BreakpointId.Invalid"/>.</summary>
    public BreakpointId SelectedId => _selectedId;

    /// <summary>
    /// Main draw entry-point. Must be called from within an active ImGui frame
    /// inside the owning <see cref="DataBreakpointManagerWindow.DrawClientArea"/>.
    /// </summary>
    public void DrawContent()
    {
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
            bp.FilterEntity.HasValue ? $"Entity {bp.FilterEntity.Value}" : "Global",
            GetTypeName(bp.Condition),
            BreakpointConditionSummarizer.Summarize(bp.Condition),
            bp.HitCount)).ToList();

        return new DataBreakpointManagerPanelViewModel(
            panelId, panelKind, _selectedId.IsValid ? _selectedId.ToString() : null, rows, _bannerPanel.BuildViewModel());
    }

    // ── Internal action seams (used by tests) ─────────────────────────────────

    /// <summary>Adds a new breakpoint with the default predicate. Mirrors the "+Add" toolbar button.</summary>
    internal void AddBreakpoint()
    {
        var dto = _createDefaultPredicate?.Invoke() ?? new PropertyMatchDto();
        var id = _manager.AddBreakpoint(dto, displayName: "New Breakpoint");
        _selectedId = id;
    }

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
            AddBreakpoint();

        ImGuiApi.SameLine();
        bool canRemove = _selectedId.IsValid;
        if (!canRemove) ImGuiApi.BeginDisabled();
        if (ImGuiApi.Button("- Remove"))
            RemoveSelected();
        if (!canRemove) ImGuiApi.EndDisabled();

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

    private void DrawGrid()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;

        if (!ImGuiApi.BeginTable("##bpgrid", 5, flags)) return;

        ImGuiApi.TableSetupColumn("##en",  ImGuiTableColumnFlags.WidthFixed, 20f);
        ImGuiApi.TableSetupColumn("Scope", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGuiApi.TableSetupColumn("Type",  ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGuiApi.TableSetupColumn("Condition Summary", ImGuiTableColumnFlags.WidthStretch);
        ImGuiApi.TableSetupColumn("Hits", ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGuiApi.TableHeadersRow();

        foreach (var bp in _manager.AllBreakpoints)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0);

            bool enabled = bp.Enabled;
            if (ImGuiApi.Checkbox($"##en_{bp.Id}", ref enabled))
                ToggleEnabled(bp.Id);

            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.TextUnformatted(bp.FilterEntity.HasValue
                ? $"Entity {bp.FilterEntity.Value}"
                : "Global");

            ImGuiApi.TableSetColumnIndex(2);
            ImGuiApi.TextUnformatted(GetTypeName(bp.Condition));

            ImGuiApi.TableSetColumnIndex(3);
            bool isSelected = _selectedId == bp.Id;
            if (ImGuiApi.Selectable(
                    BreakpointConditionSummarizer.Summarize(bp.Condition) + $"##sel_{bp.Id}",
                    isSelected,
                    ImGuiSelectableFlags.SpanAllColumns))
            {
                _selectedId = bp.Id;
            }

            ImGuiApi.TableSetColumnIndex(4);
            ImGuiApi.TextUnformatted(bp.HitCount.ToString());
        }

        ImGuiApi.EndTable();
    }

    /// <summary>
    /// Renders the predicate condition tree for the selected breakpoint.
    /// Compound children listed in <see cref="CompoundPredicateDto.ReadOnlyChildIndices"/>
    /// are rendered inside <c>ImGui.BeginDisabled()</c> so the user cannot edit them.
    /// </summary>
    private void DrawPredicateEditor()
    {
        if (!_selectedId.IsValid) return;

        var bp = _manager.AllBreakpoints.FirstOrDefault(b => b.Id == _selectedId);
        if (bp == null) return;

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
