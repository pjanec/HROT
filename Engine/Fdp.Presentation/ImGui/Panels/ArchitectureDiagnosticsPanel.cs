using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Resilience;
using Fdp.ModuleHost.Scheduling;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

public sealed class ArchitectureDiagnosticsPanel
{
    public void DrawContent(ModuleHostKernel kernel)
    {
        if (ImGuiApi.CollapsingHeader("Modules", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawModulesTable(kernel);
        }

        if (ImGuiApi.CollapsingHeader("Systems", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSystemsTable(kernel);
        }

        if (ImGuiApi.CollapsingHeader("Translators", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTranslatorsTable(kernel);
        }
    }

    private static unsafe void DrawModulesTable(ModuleHostKernel kernel)
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

        var moduleDiagnostics = kernel.GetModuleDiagnostics().ToList();
        var sortSpecs = ImGuiApi.TableGetSortSpecs();
        if (sortSpecs.NativePtr != null && sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            bool asc = spec.SortDirection != ImGuiSortDirection.Descending;

            moduleDiagnostics = spec.ColumnIndex switch
            {
                0 => asc
                    ? moduleDiagnostics.OrderBy(m => m.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                1 => asc
                    ? moduleDiagnostics.OrderBy(m => m.ModuleTypeName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.ModuleTypeName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                2 => asc
                    ? moduleDiagnostics.OrderBy(m => m.RunMode).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.RunMode).ToList(),
                3 => asc
                    ? moduleDiagnostics.OrderBy(m => m.DataStrategy).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.DataStrategy).ToList(),
                4 => asc
                    ? moduleDiagnostics.OrderBy(m => m.TargetFrequencyHz).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.TargetFrequencyHz).ToList(),
                5 => asc
                    ? moduleDiagnostics.OrderBy(m => m.LifecycleState).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.LifecycleState).ToList(),
                6 => asc
                    ? moduleDiagnostics.OrderBy(m => m.CircuitState).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.CircuitState).ToList(),
                7 => asc
                    ? moduleDiagnostics.OrderBy(m => m.ExecutionCount).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.ExecutionCount).ToList(),
                8 => asc
                    ? moduleDiagnostics.OrderBy(m => m.FailureCount).ToList()
                    : moduleDiagnostics.OrderByDescending(m => m.FailureCount).ToList(),
                _ => moduleDiagnostics.OrderBy(m => m.ModuleName, System.StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        foreach (var module in moduleDiagnostics)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(module.ModuleName);
            ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextUnformatted(module.ModuleTypeName);
            ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextUnformatted(module.RunMode.ToString());
            ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextUnformatted(module.DataStrategy.ToString());
            ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted(module.TargetFrequencyHz.ToString());
            ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted(module.LifecycleState.ToString());

            var circuitColor = module.CircuitState == CircuitState.Closed
                ? new Vector4(0.45f, 0.90f, 0.45f, 1.0f)
                : new Vector4(1.0f, 0.40f, 0.40f, 1.0f);
            ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextColored(circuitColor, module.CircuitState.ToString());

            ImGuiApi.TableSetColumnIndex(7); ImGuiApi.TextUnformatted(module.ExecutionCount.ToString());
            ImGuiApi.TableSetColumnIndex(8); ImGuiApi.TextUnformatted(module.FailureCount.ToString());
        }

        ImGuiApi.EndTable();
    }

    private static unsafe void DrawSystemsTable(ModuleHostKernel kernel)
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

        var allProfileData = kernel.SystemScheduler.GetAllProfileData()
            .SelectMany(kvp => kvp.Value.Select(item => new
            {
                Phase = kvp.Key,
                Profile = item.Profile,
                ModuleName = kernel.GetModuleNameForSystem(item.System)
            }))
            .ToList();

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
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(entry.Phase.ToString());
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

    private static unsafe void DrawTranslatorsTable(ModuleHostKernel kernel)
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

        var translatorRows = EnumerateTranslatorRows(kernel).ToList();
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
                    ? translatorRows.OrderBy(r => r.Translator.TopicName, System.StringComparer.OrdinalIgnoreCase).ToList()
                    : translatorRows.OrderByDescending(r => r.Translator.TopicName, System.StringComparer.OrdinalIgnoreCase).ToList(),
                3 => asc
                    ? translatorRows.OrderBy(r => (r.Translator as IDescriptorTranslator)?.DescriptorOrdinal ?? 0L).ToList()
                    : translatorRows.OrderByDescending(r => (r.Translator as IDescriptorTranslator)?.DescriptorOrdinal ?? 0L).ToList(),
                4 => asc ? translatorRows.OrderBy(r => r.Profile.LastMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.LastMs).ToList(),
                5 => asc ? translatorRows.OrderBy(r => r.Profile.AverageMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.AverageMs).ToList(),
                6 => asc ? translatorRows.OrderBy(r => r.Profile.MaxMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.MaxMs).ToList(),
                7 => asc ? translatorRows.OrderBy(r => r.Profile.TotalMs).ToList() : translatorRows.OrderByDescending(r => r.Profile.TotalMs).ToList(),
                8 => asc
                    ? translatorRows.OrderBy(r => r.Direction == "Ingress"
                        ? r.Translator.ReceivedSampleCount
                        : r.Direction == "Egress"
                            ? r.Translator.SentSampleCount
                            : r.Profile.ExecutionCount).ToList()
                    : translatorRows.OrderByDescending(r => r.Direction == "Ingress"
                        ? r.Translator.ReceivedSampleCount
                        : r.Direction == "Egress"
                            ? r.Translator.SentSampleCount
                            : r.Profile.ExecutionCount).ToList(),
                _ => translatorRows.OrderBy(r => r.Translator.TopicName, System.StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        foreach (var row in translatorRows)
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(row.SystemName);
            ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextUnformatted(row.Direction);
            ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextUnformatted(row.Translator.TopicName);
            ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextUnformatted(((row.Translator as IDescriptorTranslator)?.DescriptorOrdinal ?? 0L).ToString());
            ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted($"{row.Profile.LastMs:F3}");
            ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted($"{row.Profile.AverageMs:F3}");
            ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextUnformatted($"{row.Profile.MaxMs:F3}");
            ImGuiApi.TableSetColumnIndex(7); ImGuiApi.TextUnformatted($"{row.Profile.TotalMs:F3}");
            ImGuiApi.TableSetColumnIndex(8);
            if (row.Direction == "Ingress")
                ImGuiApi.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), row.Translator.ReceivedSampleCount.ToString());
            else if (row.Direction == "Egress")
                ImGuiApi.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), row.Translator.SentSampleCount.ToString());
            else
                ImGuiApi.TextUnformatted(row.Profile.ExecutionCount.ToString());
        }

        ImGuiApi.EndTable();
    }

    private static IEnumerable<TranslatorRow> EnumerateTranslatorRows(ModuleHostKernel kernel)
    {
        foreach (var system in kernel.SystemScheduler.GetAllSystems())
        {
            var translatorsProperty = system.GetType().GetProperty("Translators");
            if (translatorsProperty == null)
                continue;

            if (translatorsProperty.GetValue(system) is not IEnumerable<INetworkTranslator> translators)
                continue;

            if (system.GetType().Name.Contains("Cleanup"))
                continue;
            foreach (var translator in translators)
            {
                var profile = TryGetTranslatorProfile(system, translator)
                    ?? new SystemProfileData($"{translator.TopicName} [{(translator as IDescriptorTranslator)?.DescriptorOrdinal}]");
                yield return new TranslatorRow(system.GetType().Name, translator.Direction.ToString(), translator, profile);
            }
        }
    }

    private static SystemProfileData? TryGetTranslatorProfile(object system, INetworkTranslator translator)
    {
        var method = system.GetType().GetMethod("GetTranslatorProfileData", new[] { typeof(INetworkTranslator) });
        if (method == null)
            return null;

        return method.Invoke(system, new object[] { translator }) as SystemProfileData;
    }

    private readonly record struct TranslatorRow(
        string SystemName,
        string Direction,
        INetworkTranslator Translator,
        SystemProfileData Profile);
}
