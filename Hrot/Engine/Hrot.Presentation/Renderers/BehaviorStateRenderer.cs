using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

[ImGuiRenderer(typeof(BehaviorState))]
public sealed class BehaviorStateRenderer : IImGuiRenderer
{
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    public string? GetSummary(object value)
    {
        var state = (BehaviorState)value;
        return $"{GetBehaviorName(state.ActiveBehaviorHash)} (Tier {state.BrainTier})";
    }

    public bool RenderValue(object value)
    {
        var state = (BehaviorState)value;

        string displayHash = state.ActiveBehaviorHash == 0
            ? "Idle"
            : $"{GetBehaviorName(state.ActiveBehaviorHash)} ({state.ActiveBehaviorHash})";

        ImGui.TextUnformatted($"ActiveBehaviorHash : {displayHash}");
        ImGui.TextUnformatted($"InstanceId         : {state.InstanceId}");
        ImGui.TextUnformatted($"BrainTier          : {state.BrainTier}");
        return true;
    }

    private static string GetBehaviorName(int hash)
    {
        if (hash == 0) return "Idle";
        if (BehaviorRegistryAccessor != null && BehaviorRegistryAccessor.TryGetName(hash, out string? name))
            return name;
        return $"#{hash}";
    }
}
