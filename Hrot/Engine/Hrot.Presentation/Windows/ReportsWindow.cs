using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Squad.Command;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐ CE-1191 (UI-6, DESIGN_Command_Display §6 D3) — the whole of what the <see cref="ReportsWindow"/> shows, this frame: the log's size,
/// the filters in force and the rows they keep. ⚠ A plain window model: the HOST supplies id and kind.
/// </summary>
public sealed record ReportsWindowViewModel(
    string PanelId, string PanelKind, long TotalLogged, string UnitFilter, string KindFilter,
    IReadOnlyList<ReportRow> Rows, string? EmptyText) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ CE-1191 (UI-6, <c>docs/DESIGN_Command_Display.md</c> §6 D3) — the report log of THIS node as a table: time · kind / subtype ·
/// from → to · the SPOT fields · age · SYN · verdict, newest first, with a unit filter (from or to) and a kind filter. A thin
/// <see cref="ManagedWindow"/> over <see cref="ReportsWindowModel"/> (the model is pure; the rails drive it).
/// ⚠ The log is filled where the commander's staff runs (brain nodes): elsewhere the window shows
/// <see cref="ReportsWindowModel.EmptyText"/> (D4 — behaviors' call — puts the log on every node). One implementation, every host that
/// registers it; the id is per host (<see cref="ScenarioPanelWindowIds"/>) so saved layouts never cross.
/// </summary>
public sealed class ReportsWindow : ManagedWindow
{
    /// <summary>The panel kind in the snapshot, shared by every host of this window.</summary>
    public const string Kind = "command-reports";

    private readonly Func<EntityRepository?> _world;
    private Entity? _unit;
    private ReportKind? _kind;

    /// <param name="world">The host's world, or null where it has none (ExCon). Read each frame — a scenario load swaps it.</param>
    /// <param name="id">Window id — ⚠ per host, see <see cref="ScenarioPanelWindowIds"/>.</param>
    /// <param name="perspective">Owning perspective.</param>
    /// <param name="titleBarColor">The host's title-bar colour.</param>
    public ReportsWindow(Func<EntityRepository?> world, string id, string perspective, Vector4 titleBarColor)
        : base(id, "Reports", perspective, WindowScope.PerspectiveBound)
    {
        _world = world;
        IsOpen = true;
        TitleBarColor = titleBarColor;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>The unit the table is filtered to; null = all. Tests and the combo set it.</summary>
    public Entity? UnitFilter { get => _unit; set => _unit = value; }

    /// <summary>The kind the table is filtered to; null = all.</summary>
    public ReportKind? KindFilter { get => _kind; set => _kind = value; }

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the log under the current filters. No ImGui.</summary>
    public ReportsWindowViewModel BuildViewModel()
    {
        var world = _world();
        var log = world == null ? null : SquadReportLog.Peek(world);
        float now = world == null ? 0f : CommandPictureOps.Now(world);
        var rows = ReportsWindowModel.Rows(world, log, now, _unit, _kind);
        string? empty = (log == null || log.Count == 0) ? ReportsWindowModel.EmptyText
                      : rows.Count == 0 ? ReportsWindowModel.NoMatchText
                      : null;
        return new ReportsWindowViewModel(
            Id, Kind, log?.Count ?? 0,
            _unit is { } u ? ReportsWindowModel.NameOf(world, u) : "all",
            _kind?.ToString() ?? "all", rows, empty);
    }

    private ReportsWindowViewModel BuildAndPublish()
    {
        var vm = BuildViewModel();
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Headless seam for the panel-model rails.</summary>
    public ReportsWindowViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        var vm = BuildAndPublish();
        var world = _world();
        var log = world == null ? null : SquadReportLog.Peek(world);
        if (log == null || log.Count == 0)
        {
            ImGui.TextDisabled(ReportsWindowModel.EmptyText);
            return;
        }

        DrawFilters(world, log);
        if (vm.EmptyText != null) ImGui.TextDisabled(vm.EmptyText);

        if (!ImGui.BeginTable("##reports", 7,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, 0)))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time",      ImGuiTableColumnFlags.WidthFixed, 56f);
        ImGui.TableSetupColumn("Kind",      ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("From → To", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Spot",      ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Age",       ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn("",          ImGuiTableColumnFlags.WidthFixed, 34f);
        ImGui.TableSetupColumn("Verdict",   ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableHeadersRow();
        foreach (var row in vm.Rows)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); ImGui.Text($"{row.SeenAt:F1}");
            ImGui.TableSetColumnIndex(1); ImGui.Text(row.KindText);
            ImGui.TableSetColumnIndex(2); ImGui.Text($"{row.From} → {row.To}");
            ImGui.TableSetColumnIndex(3); ImGui.Text(row.SpotText);
            ImGui.TableSetColumnIndex(4); ImGui.Text($"{row.AgeSeconds:F0} s");
            ImGui.TableSetColumnIndex(5); if (row.Synthetic) ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), "SYN");
            ImGui.TableSetColumnIndex(6); ImGui.Text(row.Verdict);
        }
        ImGui.EndTable();
    }

    /// <summary>The unit and kind combos. A unit that left the log (the ring wrapped) is dropped from the filter, not kept invisibly.</summary>
    private void DrawFilters(EntityRepository? world, SquadReportLog log)
    {
        var units = ReportsWindowModel.Units(world, log);
        if (_unit is { } cur && !units.Any(u => u.Unit.Equals(cur))) _unit = null;

        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Unit", _unit is { } u0 ? ReportsWindowModel.NameOf(world, u0) : "All units"))
        {
            if (ImGui.Selectable("All units", _unit == null)) _unit = null;
            foreach (var u in units)
                if (ImGui.Selectable($"{u.Name}##u{u.Unit.Index}", _unit is { } sel && sel.Equals(u.Unit))) _unit = u.Unit;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130f);
        if (ImGui.BeginCombo("Kind", _kind?.ToString() ?? "All kinds"))
        {
            if (ImGui.Selectable("All kinds", _kind == null)) _kind = null;
            foreach (var k in ReportsWindowModel.Kinds)
                if (ImGui.Selectable(k.ToString(), _kind == k)) _kind = k;
            ImGui.EndCombo();
        }
    }
}
