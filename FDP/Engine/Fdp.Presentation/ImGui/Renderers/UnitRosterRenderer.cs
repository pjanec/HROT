using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Renderers;

/// <summary>
/// Custom read-only renderer for <see cref="UnitRoster"/>.
/// Displays packed entity handles as "[index, vGeneration]" and tactical designation names.
/// </summary>
[ImGuiRenderer(typeof(UnitRoster))]
public sealed class UnitRosterRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var roster = (UnitRoster)value;
        return $"{roster.Count} subordinates";
    }

    public unsafe bool RenderValue(object value)
    {
        var roster = (UnitRoster)value;

        ImGuiApi.TextUnformatted($"Active Count: {roster.Count} / {UnitRoster.Capacity}");

        if (roster.Count == 0)
            return true;

        if (ImGuiApi.BeginTable(
                "UnitRosterTable",
                3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGuiApi.TableSetupColumn("Idx", ImGuiTableColumnFlags.WidthFixed, 36f);
            ImGuiApi.TableSetupColumn("Entity", ImGuiTableColumnFlags.WidthFixed, 120f);
            ImGuiApi.TableSetupColumn("Designation", ImGuiTableColumnFlags.WidthStretch);
            ImGuiApi.TableHeadersRow();

            for (int i = 0; i < roster.Count; i++)
            {
                ImGuiApi.TableNextRow();

                ImGuiApi.TableSetColumnIndex(0);
                ImGuiApi.TextDisabled($"[{i}]");

                ImGuiApi.TableSetColumnIndex(1);
                var entity = new Entity((ulong)roster.SubordinateEntities[i]);
                if (entity.IsNull)
                    ImGuiApi.TextDisabled("[null]");
                else
                    ImGuiApi.TextUnformatted($"[{entity.Index}, v{entity.Generation}]");

                ImGuiApi.TableSetColumnIndex(2);
                var designation = (TacticalDesignation)roster.TacticalDesignations[i];
                ImGuiApi.TextUnformatted(designation.ToString());
            }

            ImGuiApi.EndTable();
        }

        return true;
    }
}
