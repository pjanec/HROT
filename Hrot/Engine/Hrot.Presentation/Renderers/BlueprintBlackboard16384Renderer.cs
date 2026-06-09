using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

[ImGuiRenderer(typeof(BlueprintBlackboard16384))]
public sealed class BlueprintBlackboard16384Renderer : IEntityAwareImGuiRenderer
{
    /// <summary>Set at startup. Required for blueprint id→name resolution.</summary>
    public static BlueprintRegistry? BlueprintRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----
    public string? GetSummary(object value) => "Instance Blueprints (16384 bytes)";
    public bool RenderValue(object value) => false; // non-entity-aware fallback — delegate to default

    // ---- IEntityAwareImGuiRenderer ----
    public string? GetSummary(IInspectableSession session, Entity entity, object value)
    {
        var registry = BlueprintRegistryAccessor;
        if (registry == null || value is not BlueprintBlackboard16384 bb)
            return GetSummary(value);

        unsafe
        {
            byte* mem = bb.Memory;
            int count = BlueprintBlackboardPartitions.GetSlotCount(mem);
            return $"Instance Blueprints ({count} attached)";
        }
    }

    public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;

        var registry = BlueprintRegistryAccessor;
        if (registry == null || value is not BlueprintBlackboard16384 bb)
            return false;

        unsafe
        {
            byte* mem = bb.Memory;
            var summaries = BlueprintTierSummary.Read(mem, registry);
            if (summaries.Count == 0)
            {
                ImGui.TextDisabled("No blueprints attached.");
                return true;
            }

            if (ImGui.BeginTable("##bp16384", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Blueprint");
                ImGui.TableSetupColumn("Version");
                ImGui.TableSetupColumn("Size");
                ImGui.TableSetupColumn("Id");
                ImGui.TableHeadersRow();

                foreach (var s in summaries)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(s.Name);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(s.InstanceVersion.ToString());
                    ImGui.TableNextColumn(); ImGui.TextUnformatted($"{s.PayloadSize} B");
                    ImGui.TableNextColumn(); ImGui.TextDisabled($"0x{s.BlueprintId:X8}");
                }

                ImGui.EndTable();
            }
        }

        return true; // suppress default byte-dump
    }
}
