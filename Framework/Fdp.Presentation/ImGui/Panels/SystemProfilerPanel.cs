using System.Collections.Generic;
using System.Numerics;
using ModuleHost.Core;
using ModuleHost.Core.Resilience;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Panels;

public static class SystemProfilerPanel
{
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
