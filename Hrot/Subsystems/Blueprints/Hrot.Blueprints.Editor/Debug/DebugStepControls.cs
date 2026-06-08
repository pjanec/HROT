using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Shared ImGui rendering for the blueprint debug step-control row
/// (Continue / Step Over / Step Into / Step Out). Used by both the
/// standalone DebugPanelWindow and the Blueprint Tools panel section.
/// </summary>
public static class DebugStepControls
{
    /// <summary>
    /// Renders the step-control button row.
    /// </summary>
    /// <param name="session">The debug session.</param>
    /// <param name="onStepAction">Optional callback invoked with the action name
    /// ("Continue"/"StepOver"/"StepInto"/"StepOut") when a button is clicked.
    /// Used by DebugPanelWindow for test capture; pass null when not needed.</param>
    public static void Draw(IBlueprintDebugSession session, System.Action<string>? onStepAction = null)
    {
        // Skip if no ImGui context (headless/test environment).
        if (ImGui.GetCurrentContext() == System.IntPtr.Zero) return;

        if (session.IsPaused)
        {
            ImGui.Text("PAUSED");

            if (ImGui.Button("Continue"))
            {
                session.Continue();
                onStepAction?.Invoke("Continue");
            }
            ImGui.SameLine();
            if (ImGui.Button("Step Over"))
            {
                session.StepOver();
                onStepAction?.Invoke("StepOver");
            }
            ImGui.SameLine();
            if (ImGui.Button("Step Into"))
            {
                session.StepInto();
                onStepAction?.Invoke("StepInto");
            }
            ImGui.SameLine();
            if (ImGui.Button("Step Out"))
            {
                session.StepOut();
                onStepAction?.Invoke("StepOut");
            }
        }
        else
        {
            ImGui.TextDisabled("Not paused.");
        }
    }
}
