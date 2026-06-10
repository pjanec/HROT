using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Shared ImGui rendering for the blueprint debug step-control row.
/// Renders: Continue / Step Back / Step Over / Step Into / Step Out buttons,
/// plus a "node X / N" position indicator when node-granular recordings exist.
///
/// Used by both the standalone DebugPanelWindow and the Blueprint Tools panel section.
///
/// The position-indicator text (<see cref="FormatNodePosition"/>) is extracted as a
/// testable static helper — it does not touch ImGui.
/// </summary>
public static class DebugStepControls
{
    /// <summary>
    /// Renders the step-control button row.
    /// </summary>
    /// <param name="session">The debug session.</param>
    /// <param name="onStepAction">Optional callback invoked with the action name
    /// ("Continue"/"StepBack"/"StepOver"/"StepInto"/"StepOut") when a button is clicked.
    /// Used by DebugPanelWindow for test capture; pass null when not needed.</param>
    public static void Draw(IBlueprintDebugSession session, System.Action<string>? onStepAction = null)
    {
        // Skip if no ImGui context (headless/test environment).
        if (ImGui.GetCurrentContext() == System.IntPtr.Zero) return;

        if (session.IsPaused)
        {
            ImGui.Text("PAUSED");

            // Node-position indicator (NGS-2.4c): shown when recordings exist.
            var posText = FormatNodePosition(session);
            if (!string.IsNullOrEmpty(posText))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(posText);
            }

            if (ImGui.Button("Continue"))
            {
                session.Continue();
                onStepAction?.Invoke("Continue");
            }
            ImGui.SameLine();

            // NGS-2.4c: Step Back button — moves the virtual pointer backward.
            // Disabled when at the start of the recording (pointer == 0 or no recordings).
            bool canStepBack = session.CurrentNodePointer > 0;
            if (!canStepBack) ImGui.BeginDisabled();
            if (ImGui.Button("Step Back"))
            {
                session.StepBack();
                onStepAction?.Invoke("StepBack");
            }
            if (!canStepBack) ImGui.EndDisabled();
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

    // ---- Testable helpers (no ImGui) ----------------------------------------

    /// <summary>
    /// Returns the node-position indicator string for the current session state.
    /// Returns <see cref="string.Empty"/> when no node-granular recordings exist
    /// (i.e. <see cref="IBlueprintDebugSession.RecordedNodeCount"/> is zero or the
    /// session is not paused).
    ///
    /// Format: <c>"node {pointer+1} / {count}"</c> — 1-based for user display.
    /// </summary>
    /// <param name="session">The active debug session.</param>
    /// <returns>A human-readable position string, or <see cref="string.Empty"/>.</returns>
    public static string FormatNodePosition(IBlueprintDebugSession session)
    {
        int count = session.RecordedNodeCount;
        if (!session.IsPaused || count <= 0) return string.Empty;

        int pointer = session.CurrentNodePointer;
        // When pointer is -1 (paused but no recording for the entity, CF-6 path),
        // there is nothing meaningful to show.
        if (pointer < 0) return string.Empty;

        return $"node {pointer + 1} / {count}";
    }
}
