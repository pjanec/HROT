using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Resilience;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>⭐ One module row, projected for the dump.</summary>
public sealed record SystemProfilerRowViewModel(string ModuleName, int ExecutionCount, int FailureCount, bool IsHealthy);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="SystemProfilerPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠⚠ <b>No production host exists.</b> 📐 Measured — <c>SystemProfilerPanel</c> has ZERO callers
/// anywhere in the tree (only listed as a "standalone panel" in <c>docs/designs/win-mgr-1/DESIGN.md</c>,
/// predating the window-manager unification). ⇒ per the queue's caller-registers rule there is no host
/// to call <c>DeclareInstrumented</c>/<c>Register</c> from — <see cref="SystemProfilerPanel.BuildViewModel"/>
/// exists so the projection is ready the moment a host is written, but this panel is NOT wired into
/// <c>PanelSnapshot</c> yet. Reported rather than silently skipped, per the sweep's own rule.</para>
/// </summary>
public sealed record SystemProfilerPanelViewModel(
    string PanelId,
    string PanelKind,
    IReadOnlyList<SystemProfilerRowViewModel> Rows) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

public static class SystemProfilerPanel
{
    /// <summary>⭐⭐⭐ BUILD — a pure projection of <paramref name="stats"/>. No ImGui. ⚠ Not wired to any
    /// host yet — see the view-model's own remarks.</summary>
    public static SystemProfilerPanelViewModel BuildViewModel(List<ModuleStats>? stats, string panelId, string panelKind)
    {
        var rows = (stats ?? new List<ModuleStats>())
            .Select(s => new SystemProfilerRowViewModel(
                s.ModuleName ?? "Unknown", s.ExecutionCount, s.FailureCount, s.CircuitState == CircuitState.Closed))
            .ToList();
        return new SystemProfilerPanelViewModel(panelId, panelKind, rows);
    }

    public static void Draw(List<ModuleStats> stats)
    {
        if (ImGuiApi.Begin("System Profiler"))
        {
            if (ImGuiApi.BeginTable("ProfilerTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGuiApi.TableSetupColumn("Module");
                ImGuiApi.TableSetupColumn("Frequency");
                ImGuiApi.TableSetupColumn("Failures");
                ImGuiApi.TableSetupColumn("Status");
                ImGuiApi.TableHeadersRow();

                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        ImGuiApi.TableNextRow();

                        ImGuiApi.TableSetColumnIndex(0);
                        ImGuiApi.Text(stat.ModuleName ?? "Unknown");

                        ImGuiApi.TableSetColumnIndex(1);
                        ImGuiApi.Text($"{stat.ExecutionCount}");

                        ImGuiApi.TableSetColumnIndex(2);
                        ImGuiApi.Text($"{stat.FailureCount}");

                        ImGuiApi.TableSetColumnIndex(3);
                        
                        // Status Indicator
                        bool isHealthy = stat.CircuitState == CircuitState.Closed;
                        Vector4 color = isHealthy ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1);
                        
                        ImGuiApi.TextColored(color, isHealthy ? "OK" : "CRITICAL");
                        ImGuiApi.SameLine();
                        
                        // Small circle indicator
                        var drawList = ImGuiApi.GetWindowDrawList();
                        var cursorPos = ImGuiApi.GetCursorScreenPos();
                        float radius = 5.0f;
                        // Adjust position to be vertically centered on the line
                        Vector2 center = new Vector2(cursorPos.X + radius, cursorPos.Y + ImGuiApi.GetTextLineHeight() * 0.5f);
                        drawList.AddCircleFilled(center, radius, ImGuiApi.ColorConvertFloat4ToU32(color));
                    }
                }
                ImGuiApi.EndTable();
            }
        }
        ImGuiApi.End();
    }
}
