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

    private static void DrawModulesTable(ModuleHostKernel kernel)
    {
        if (!ImGuiApi.BeginTable("ArchDiagModulesTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGuiApi.TableSetupColumn("Module");
        ImGuiApi.TableSetupColumn("Type");
        ImGuiApi.TableSetupColumn("Mode");
        ImGuiApi.TableSetupColumn("Strategy");
        ImGuiApi.TableSetupColumn("Target Hz");
        ImGuiApi.TableSetupColumn("Lifecycle");
        ImGuiApi.TableSetupColumn("Circuit");
        ImGuiApi.TableSetupColumn("Runs");
        ImGuiApi.TableSetupColumn("Failures");
        ImGuiApi.TableHeadersRow();

        var moduleDiagnostics = kernel.GetModuleDiagnostics();
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

    private static void DrawSystemsTable(ModuleHostKernel kernel)
    {
        if (!ImGuiApi.BeginTable("ArchDiagSystemsTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGuiApi.TableSetupColumn("Phase");
        ImGuiApi.TableSetupColumn("System");
        ImGuiApi.TableSetupColumn("Last (ms)");
        ImGuiApi.TableSetupColumn("Avg (ms)");
        ImGuiApi.TableSetupColumn("Max (ms)");
        ImGuiApi.TableSetupColumn("Runs");
        ImGuiApi.TableSetupColumn("Errors");
        ImGuiApi.TableHeadersRow();

        var allProfileData = kernel.SystemScheduler.GetAllProfileData();
        foreach (var (phase, profiles) in allProfileData.OrderBy(p => (int)p.Key))
        {
            foreach (var profile in profiles)
            {
                ImGuiApi.TableNextRow();
                ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(phase.ToString());
                ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextUnformatted(profile.SystemName);

                var timeColor = profile.LastMs > 5.0
                    ? new Vector4(1.0f, 0.40f, 0.40f, 1.0f)
                    : profile.LastMs > 1.0
                        ? new Vector4(1.0f, 0.85f, 0.30f, 1.0f)
                        : new Vector4(0.90f, 0.90f, 0.90f, 1.0f);

                ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextColored(timeColor, $"{profile.LastMs:F3}");
                ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextUnformatted($"{profile.AverageMs:F3}");
                ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted($"{profile.MaxMs:F3}");
                ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted(profile.ExecutionCount.ToString());
                ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextUnformatted(profile.ErrorCount.ToString());
            }
        }

        ImGuiApi.EndTable();
    }

    private static void DrawTranslatorsTable(ModuleHostKernel kernel)
    {
        if (!ImGuiApi.BeginTable("ArchDiagTranslatorsTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGuiApi.TableSetupColumn("System");
        ImGuiApi.TableSetupColumn("Direction");
        ImGuiApi.TableSetupColumn("Topic");
        ImGuiApi.TableSetupColumn("Ordinal");
        ImGuiApi.TableSetupColumn("Last (ms)");
        ImGuiApi.TableSetupColumn("Avg (ms)");
        ImGuiApi.TableSetupColumn("Max (ms)");
        ImGuiApi.TableSetupColumn("Runs");
        ImGuiApi.TableHeadersRow();

        foreach (var row in EnumerateTranslatorRows(kernel))
        {
            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0); ImGuiApi.TextUnformatted(row.SystemName);
            ImGuiApi.TableSetColumnIndex(1); ImGuiApi.TextUnformatted(row.Direction);
            ImGuiApi.TableSetColumnIndex(2); ImGuiApi.TextUnformatted(row.Translator.TopicName);
            ImGuiApi.TableSetColumnIndex(3); ImGuiApi.TextUnformatted(row.Translator.DescriptorOrdinal.ToString());
            ImGuiApi.TableSetColumnIndex(4); ImGuiApi.TextUnformatted($"{row.Profile.LastMs:F3}");
            ImGuiApi.TableSetColumnIndex(5); ImGuiApi.TextUnformatted($"{row.Profile.AverageMs:F3}");
            ImGuiApi.TableSetColumnIndex(6); ImGuiApi.TextUnformatted($"{row.Profile.MaxMs:F3}");
            ImGuiApi.TableSetColumnIndex(7); ImGuiApi.TextUnformatted(row.Profile.ExecutionCount.ToString());
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

            if (translatorsProperty.GetValue(system) is not IEnumerable<IDescriptorTranslator> translators)
                continue;

            var direction = GetDirectionLabel(system.GetType().Name);
            foreach (var translator in translators)
            {
                var profile = TryGetTranslatorProfile(system, translator)
                    ?? new SystemProfileData($"{translator.TopicName} [{translator.DescriptorOrdinal}]");
                yield return new TranslatorRow(system.GetType().Name, direction, translator, profile);
            }
        }
    }

    private static string GetDirectionLabel(string systemName)
    {
        if (systemName.Contains("Ingress"))
            return "Ingress";
        if (systemName.Contains("Egress"))
            return "Egress";
        if (systemName.Contains("Cleanup"))
            return "Cleanup";
        return "N/A";
    }

    private static SystemProfileData? TryGetTranslatorProfile(object system, IDescriptorTranslator translator)
    {
        var method = system.GetType().GetMethod("GetTranslatorProfileData", new[] { typeof(IDescriptorTranslator) });
        if (method == null)
            return null;

        return method.Invoke(system, new object[] { translator }) as SystemProfileData;
    }

    private readonly record struct TranslatorRow(
        string SystemName,
        string Direction,
        IDescriptorTranslator Translator,
        SystemProfileData Profile);
}
