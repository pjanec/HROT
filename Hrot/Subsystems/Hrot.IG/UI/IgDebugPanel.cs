using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using ImGuiNET;
using Raylib_cs;

namespace Hrot.IG.UI;

/// <summary>⭐⭐⭐ U-obs-5 (group 6) — the whole of what <see cref="IgDebugPanel"/> shows, this frame.
/// ⚠ A plain panel: no <see cref="PanelId"/>/<see cref="PanelKind"/> of its own — the HOST
/// (<c>IgDebugWindow</c>) supplies both. ⚠ <c>FPS</c> is deliberately NOT modelled: it comes from
/// <c>Raylib.GetFPS()</c>, a native call with no window in a headless test process — capturing it here
/// would make <c>BuildViewModel</c> unsafe to call from a unit test, which is exactly the gotcha table's
/// "CAPTURE … before anything ImGui-dependent" in reverse. The stateful diagnostics
/// (<see cref="DebugPanelState"/>'s own fields) are all real and modelled.</summary>
public sealed record IgDebugPanelViewModel(
    string PanelId, string PanelKind, double CurrentSimTime, long CurrentWallTicks,
    bool ForceHostile, bool HideLabels) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

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

    /// <summary>⭐⭐⭐ BUILD — a pure projection of <see cref="DebugPanelState"/>. No ImGui, no Raylib —
    /// safe to call headless.</summary>
    public IgDebugPanelViewModel BuildViewModel(string panelId, string panelKind) => new(
        panelId, panelKind, _state.CurrentSimTime, _state.CurrentWallTicks,
        _state.ForceHostile, _state.HideLabels);

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        // ── Live stats ────────────────────────────────────────────────────────
        ImGui.Text($"FPS: {Raylib.GetFPS()}");

        // ── Time sync diagnostics ─────────────────────────────────────────────
        ImGui.Text($"Sim Time:   {TimeSpan.FromSeconds(_state.CurrentSimTime)}");
        ImGui.Text($"Wall Ticks: {_state.CurrentWallTicks}");
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
