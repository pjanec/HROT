using System;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior.Components;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

[ImGuiRenderer(typeof(MissionPlanQueue))]
public sealed class MissionPlanQueueRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var q = (MissionPlanQueue)value;
        return $"Phases: {q.PhaseCount} (Current: {q.CurrentPhase})";
    }

    public bool RenderValue(object value)
    {
        var q = (MissionPlanQueue)value;

        // Start a nested table mirroring the exact flags used by ImGuiPropertyTree
        if (ImGui.BeginTable("MissionPlanQueueTable", 2, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn( "Property", ImGuiTableColumnFlags.WidthFixed, ImGuiPropertyTree.NameColWidth );
            ImGui.TableSetupColumn( "Value", ImGuiTableColumnFlags.WidthStretch );

            DrawLeafRow("CurrentPhase", q.CurrentPhase.ToString());
            DrawLeafRow("PhaseCount", q.PhaseCount.ToString());
            DrawLeafRow("PhaseElapsedSeconds", q.PhaseElapsedSeconds.ToString("F2"));

            // Safe cast to Span bypasses the C# 12 InlineArray trap
            ReadOnlySpan<MissionPhase> phases = q.Phases;

            for (int i = 0; i < q.PhaseCount; i++)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                    
                bool open = ImGui.TreeNodeEx($"Phase [{i}]", ImGuiTreeNodeFlags.SpanAvailWidth);
                    
                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled($"BehaviorId: {phases[i].BehaviorId}"); // Summary in value cell

                if (open)
                {
                    var p = phases[i];
                    DrawLeafRow("  BehaviorId", p.BehaviorId.ToString());
                    DrawLeafRow("  Trigger", p.Trigger.ToString());
                    DrawLeafRow("  TriggerParam", p.TriggerParam.ToString("F2"));
                    ImGui.TreePop();
                }
            }

            ImGui.EndTable();
        }

        return true;
    }

    private static void DrawLeafRow(string propertyName, string propertyValue)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
            
        // Use Leaf | NoTreePushOnOpen to align nicely without pushing actual tree depth
        ImGui.TreeNodeEx(propertyName, 
            ImGuiTreeNodeFlags.Leaf | 
            ImGuiTreeNodeFlags.NoTreePushOnOpen | 
            ImGuiTreeNodeFlags.SpanAvailWidth);
                
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(propertyValue);
    }
}
