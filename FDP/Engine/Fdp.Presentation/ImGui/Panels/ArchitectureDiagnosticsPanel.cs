using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Resilience;
using Fdp.ModuleHost.Scheduling;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>⭐ A system/translator profile row, projected by hand rather than embedding
/// <see cref="SystemProfileData"/> directly — that type carries a <c>LastError</c> (<c>Exception?</c>)
/// which is not a clean dump shape. 📄 mirrors the gotcha table's "project the displayed shape by hand".</summary>
public sealed record ArchDiagProfileViewModel(double LastMs, double AverageMs, double MaxMs, double TotalMs, int ErrorCount);

public sealed record ArchDiagSystemRowViewModel(string Phase, string ModuleName, string SystemName, ArchDiagProfileViewModel Profile);

public sealed record ArchDiagTranslatorRowViewModel(
    string SystemName, string Direction, string TopicName, long DescriptorOrdinal,
    ArchDiagProfileViewModel Profile, long ReceivedSamples, long SentSamples);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ArchitectureDiagnosticsPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ <see cref="Modules"/> is dumped as-is
/// (<c>ModuleDiagnosticsDto</c> is already a flat, delegate-free DTO); systems/translators are
/// hand-projected — see <see cref="ArchDiagProfileViewModel"/>.
/// </summary>
public sealed record ArchitectureDiagnosticsPanelViewModel(
    string PanelId,
    string PanelKind,
    IReadOnlyList<ModuleDiagnosticsDto> Modules,
    IReadOnlyList<ArchDiagSystemRowViewModel> Systems,
    IReadOnlyList<ArchDiagTranslatorRowViewModel> Translators) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

public sealed class ArchitectureDiagnosticsPanel
{
    private readonly IArchitectureDiagnosticsService _service;

    public ArchitectureDiagnosticsPanel(IArchitectureDiagnosticsService service)
    {
        _service = service ?? throw new System.ArgumentNullException(nameof(service));
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of the service's snapshot. No ImGui, no sorting (the
    /// table's column sort is transient ImGui state, not part of the model).</summary>
    public ArchitectureDiagnosticsPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        var snapshot = _service.GetSnapshot();

        var systems = snapshot.Systems.Select(s => new ArchDiagSystemRowViewModel(
            s.Phase, s.ModuleName, s.Profile.SystemName,
            new ArchDiagProfileViewModel(s.Profile.LastMs, s.Profile.AverageMs, s.Profile.MaxMs, s.Profile.TotalMs, s.Profile.ErrorCount)))
            .ToList();

        var translators = snapshot.Translators.Select(t => new ArchDiagTranslatorRowViewModel(
            t.SystemName, t.Direction, t.TopicName, t.DescriptorOrdinal,
            new ArchDiagProfileViewModel(t.Profile.LastMs, t.Profile.AverageMs, t.Profile.MaxMs, t.Profile.TotalMs, t.Profile.ErrorCount),
            t.ReceivedSamples, t.SentSamples))
            .ToList();

        return new ArchitectureDiagnosticsPanelViewModel(panelId, panelKind, snapshot.Modules, systems, translators);
    }

    public void DrawContent()
    {
        var snapshot = _service.GetSnapshot();

        if (ImGuiApi.CollapsingHeader("Modules", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawModulesTable(snapshot.Modules);
        }

        if (ImGuiApi.CollapsingHeader("Systems", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSystemsTable(snapshot.Systems);
        }

        if (ImGuiApi.CollapsingHeader("Translators", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTranslatorsTable(snapshot.Translators);
        }
    }

    private static unsafe void DrawModulesTable(IReadOnlyList<ModuleDiagnosticsDto> modules)
    {
        if (!ImGuiApi.BeginTable("ArchDiagModulesTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
            return;

        ImGuiApi.TableSetupColumn("Module", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortAscending);
        ImGuiApi.TableSetupColumn("Type");
        ImGuiApi.TableSetupColumn("Mode");
        ImGuiApi.TableSetupColumn("Strategy");
        ImGuiApi.TableSetupColumn("Target Hz");
        ImGuiApi.TableSetupColumn("Lifecycle");
        ImGuiApi.TableSetupColumn("Circuit");
        ImGuiApi.TableSetupColumn("RX / TX / Runs");
        ImGuiApi.TableSetupColumn("Failures");
        ImGuiApi.TableHeadersRow();

        var sortedModules = modules.ToList();
        var sortSpecs = ImGuiApi.TableGetSortSpecs();
        if (sortSpecs.NativePtr != null && sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            bool asc = spec.SortDirection != ImGuiSortDirection.Descending;

            sortedModules = spec.ColumnIndex switch
            {
                0 => asc
                    ? sortedModules.OrderBy(m => m.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : sortedModules.OrderByDescending(m => m.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                1 => asc
                    ? sortedModules.OrderBy(m => m.ModuleTypeName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : sortedModules.OrderByDescending(m => m.ModuleTypeName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                2 => asc
                    ? sortedModules.OrderBy(m => m.RunMode).ToList()
                    : sortedModules.OrderByDescending(m => m.RunMode).ToList(),
                3 => asc
                    ? sortedModules.OrderBy(m => m.DataStrategy).ToList()
                    : sortedModules.OrderByDescending(m => m.DataStrategy).ToList(),
                4 => asc
                    ? sortedModules.OrderBy(m => m.TargetFrequencyHz).ToList()
                    : sortedModules.OrderByDescending(m => m.TargetFrequencyHz).ToList(),
                5 => asc
                    ? sortedModules.OrderBy(m => m.LifecycleState).ToList()
                    : sortedModules.OrderByDescending(m => m.LifecycleState).ToList(),
                6 => asc
                    ? sortedModules.OrderBy(m => m.CircuitState).ToList()
                    : sortedModules.OrderByDescending(m => m.CircuitState).ToList(),
                7 => asc
                    ? sortedModules.OrderBy(m => m.ExecutionCount).ToList()
                    : sortedModules.OrderByDescending(m => m.ExecutionCount).ToList(),
                8 => asc
                    ? sortedModules.OrderBy(m => m.FailureCount).ToList()
                    : sortedModules.OrderByDescending(m => m.FailureCount).ToList(),
                _ => sortedModules.OrderBy(m => m.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        foreach (var module in sortedModules)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(module.ModuleName);
            ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextUnformatted(module.ModuleTypeName);
            ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextUnformatted(module.RunMode);
            ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextUnformatted(module.DataStrategy);
            ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted(module.TargetFrequencyHz.ToString());
            ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted(module.LifecycleState);

            var circuitColor = module.CircuitState == "Closed"
                ? new Vector4(0.45f, 0.90f, 0.45f, 1.0f)
                : new Vector4(1.0f, 0.40f, 0.40f, 1.0f);
            ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextColored(circuitColor, module.CircuitState);

            ImGuiApi.TableSetColumnIndex(7); ImGuiApi.TextUnformatted(module.ExecutionCount.ToString());
            ImGuiApi.TableSetColumnIndex(8); ImGuiApi.TextUnformatted(module.FailureCount.ToString());
        }

        ImGuiApi.EndTable();
    }

    private static unsafe void DrawSystemsTable(IReadOnlyList<SystemDiagnosticsRow> systems)
    {
        if (!ImGuiApi.BeginTable("ArchDiagSystemsTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
            return;

        ImGuiApi.TableSetupColumn("Phase");
        ImGuiApi.TableSetupColumn("Module", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortAscending);
        ImGuiApi.TableSetupColumn("System");
        ImGuiApi.TableSetupColumn("Last (ms)");
        ImGuiApi.TableSetupColumn("Avg (ms)");
        ImGuiApi.TableSetupColumn("Max (ms)");
        ImGuiApi.TableSetupColumn("Total (ms)");
        ImGuiApi.TableSetupColumn("Errors");
        ImGuiApi.TableHeadersRow();

        var allProfileData = systems.ToList();

        var sortSpecs = ImGuiApi.TableGetSortSpecs();
        if (sortSpecs.NativePtr != null && sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            bool asc = spec.SortDirection != ImGuiSortDirection.Descending;

            allProfileData = spec.ColumnIndex switch
            {
                0 => asc ? allProfileData.OrderBy(p => p.Phase).ToList() : allProfileData.OrderByDescending(p => p.Phase).ToList(),
                1 => asc
                    ? allProfileData.OrderBy(p => p.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : allProfileData.OrderByDescending(p => p.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                2 => asc
                    ? allProfileData.OrderBy(p => p.Profile.SystemName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : allProfileData.OrderByDescending(p => p.Profile.SystemName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                3 => asc ? allProfileData.OrderBy(p => p.Profile.LastMs).ToList() : allProfileData.OrderByDescending(p => p.Profile.LastMs).ToList(),
                4 => asc ? allProfileData.OrderBy(p => p.Profile.AverageMs).ToList() : allProfileData.OrderByDescending(p => p.Profile.AverageMs).ToList(),
                5 => asc ? allProfileData.OrderBy(p => p.Profile.MaxMs).ToList() : allProfileData.OrderByDescending(p => p.Profile.MaxMs).ToList(),
                6 => asc ? allProfileData.OrderBy(p => p.Profile.TotalMs).ToList() : allProfileData.OrderByDescending(p => p.Profile.TotalMs).ToList(),
                7 => asc ? allProfileData.OrderBy(p => p.Profile.ErrorCount).ToList() : allProfileData.OrderByDescending(p => p.Profile.ErrorCount).ToList(),
                _ => allProfileData.OrderBy(p => p.ModuleName, System.StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Profile.SystemName, System.StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        foreach (var entry in allProfileData)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(entry.Phase);
            ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), entry.ModuleName);
            ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextUnformatted(entry.Profile.SystemName);

            var timeColor = entry.Profile.LastMs > 5.0
                ? new Vector4(1.0f, 0.40f, 0.40f, 1.0f)
                : entry.Profile.LastMs > 1.0
                    ? new Vector4(1.0f, 0.85f, 0.30f, 1.0f)
                    : new Vector4(0.90f, 0.90f, 0.90f, 1.0f);

            ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextColored(timeColor, $"{entry.Profile.LastMs:F3}");
            ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted($"{entry.Profile.AverageMs:F3}");
            ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted($"{entry.Profile.MaxMs:F3}");
            ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextUnformatted($"{entry.Profile.TotalMs:F3}");
            ImGuiApi.TableSetColumnIndex(7); ImGuiApi.TextUnformatted(entry.Profile.ErrorCount.ToString());
        }

        ImGuiApi.EndTable();
    }

    private static unsafe void DrawTranslatorsTable(IReadOnlyList<TranslatorDiagnosticsDto> translators)
    {
        if (!ImGuiApi.BeginTable("ArchDiagTranslatorsTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
            return;

        ImGuiApi.TableSetupColumn("System");
        ImGuiApi.TableSetupColumn("Direction");
        ImGuiApi.TableSetupColumn("Topic", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortAscending);
        ImGuiApi.TableSetupColumn("Ordinal");
        ImGuiApi.TableSetupColumn("Last (ms)");
        ImGuiApi.TableSetupColumn("Avg (ms)");
        ImGuiApi.TableSetupColumn("Max (ms)");
        ImGuiApi.TableSetupColumn("Total (ms)");
        ImGuiApi.TableSetupColumn("Runs");
        ImGuiApi.TableHeadersRow();

        var translatorRows = translators.ToList();
        var sortSpecs = ImGuiApi.TableGetSortSpecs();
        if (sortSpecs.NativePtr != null && sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            bool asc = spec.SortDirection != ImGuiSortDirection.Descending;

            translatorRows = spec.ColumnIndex switch
            {
                0 => asc
                    ? translatorRows.OrderBy(r => r.SystemName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : translatorRows.OrderByDescending(r => r.SystemName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                1 => asc
                    ? translatorRows.OrderBy(r => r.Direction, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : translatorRows.OrderByDescending(r => r.Direction, System.StringComparer.OrdinalIgnoreCase).ToList(),
                2 => asc
                    ? translatorRows.OrderBy(r => r.TopicName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : translatorRows.OrderByDescending(r => r.TopicName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                3 => asc
                    ? translatorRows.OrderBy(r => r.DescriptorOrdinal).ToList()
                    : translatorRows.OrderByDescending(r => r.DescriptorOrdinal).ToList(),
                4 => asc ? translatorRows.OrderBy(r => r.Profile.LastMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.LastMs).ToList(),
                5 => asc ? translatorRows.OrderBy(r => r.Profile.AverageMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.AverageMs).ToList(),
                6 => asc ? translatorRows.OrderBy(r => r.Profile.MaxMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.MaxMs).ToList(),
                7 => asc ? translatorRows.OrderBy(r => r.Profile.TotalMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.TotalMs).ToList(),
                8 => asc
                    ? translatorRows.OrderBy(r => r.Direction == "Ingress"
                        ? r.ReceivedSamples
                        : r.Direction == "Egress"
                            ? r.SentSamples
                            : r.Profile.ExecutionCount).ToList()
                    : translatorRows.OrderByDescending(r => r.Direction == "Ingress"
                        ? r.ReceivedSamples
                        : r.Direction == "Egress"
                            ? r.SentSamples
                            : r.Profile.ExecutionCount).ToList(),
                _ => translatorRows.OrderBy(r => r.TopicName, System.StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        foreach (var row in translatorRows)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(row.SystemName);
            ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextUnformatted(row.Direction);
            ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextUnformatted(row.TopicName);
            ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextUnformatted(row.DescriptorOrdinal.ToString());
            ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted($"{row.Profile.LastMs:F3}");
            ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted($"{row.Profile.AverageMs:F3}");
            ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextUnformatted($"{row.Profile.MaxMs:F3}");
            ImGuiApi.TableSetColumnIndex(7); ImGuiApi.TextUnformatted($"{row.Profile.TotalMs:F3}");
            ImGuiApi.TableSetColumnIndex(8);
            if (row.Direction == "Ingress")
                ImGuiApi.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), row.ReceivedSamples.ToString());
            else if (row.Direction == "Egress")
                ImGuiApi.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), row.SentSamples.ToString());
            else
                ImGuiApi.TextUnformatted(row.Profile.ExecutionCount.ToString());
        }

        ImGuiApi.EndTable();
    }
}
