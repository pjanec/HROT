using System;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Hrot.IG.UI;

/// <summary>
/// ImGui panel providing operator debug controls for the IG session (IG.5.1).
///
/// Renders an immediate-mode "Debug Panel" window containing:
/// <list type="bullet">
///   <item>Live FPS readout from Raylib.</item>
///   <item>Toggle checkboxes for <see cref="DebugPanelState.ForceHostile"/> and
///         <see cref="DebugPanelState.HideLabels"/>.</item>
/// </list>
///
/// Visual states are driven entirely through <see cref="DebugPanelState"/>;
/// the panel itself is not unit-tested.  Test the <see cref="DebugPanelState"/>
/// class directly instead.
///
/// Call <see cref="Draw"/> each frame between <c>rlImGui.Begin()</c> and
/// <c>rlImGui.End()</c>.
/// </summary>
public class IgDebugPanel
{
    private readonly DebugPanelState _state;

    /// <param name="state">Logic state instance shared with the application shell.</param>
    public IgDebugPanel(DebugPanelState state)
        => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>
    /// Emits the Debug Panel ImGui window.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        IgPanelColors.Push();
        bool panelVisible = ImGui.Begin("Debug Panel");
        IgPanelColors.Pop();
        if (!panelVisible) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        // ── Live stats ────────────────────────────────────────────────────────
        ImGui.Text($"FPS: {Raylib.GetFPS()}");
        ImGui.Separator();

        // ── MapUserConfig toggles ─────────────────────────────────────────────
        ImGui.Text("Render Overrides");

        bool forceHostile = _state.ForceHostile;
        if (ImGui.Checkbox("Force Hostile", ref forceHostile))
            _state.ForceHostile = forceHostile;

        bool hideLabels = _state.HideLabels;
        if (ImGui.Checkbox("Hide Labels", ref hideLabels))
            _state.HideLabels = hideLabels;
    }
}
