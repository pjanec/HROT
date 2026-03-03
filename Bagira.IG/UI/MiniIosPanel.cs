using System;
using Bagira.IG.Components;
using Bagira.Map.Common.Commands;
using Fdp.Kernel;
using ImGuiNET;

namespace Bagira.IG.UI;

/// <summary>
/// ImGui panel providing a lightweight IOS-style entity spawner (IG.5.3).
///
/// Renders a "Mini IOS" window containing:
/// <list type="bullet">
///   <item>A TKB type ID input field.</item>
///   <item>An affiliation combo box.</item>
///   <item>X / Y coordinate inputs for initial placement.</item>
///   <item>A "Spawn" button that calls <see cref="MiniIosPanelState.Submit"/>.</item>
/// </list>
///
/// All mutable state lives in <see cref="MiniIosPanelState"/> so that the form
/// data can be exercised in tests without invoking ImGui.
///
/// Call <see cref="Draw"/> each frame between <c>rlImGui.Begin()</c> and
/// <c>rlImGui.End()</c>.
/// </summary>
public class MiniIosPanel
{
    private readonly MiniIosPanelState _state;
    private readonly FdpEventBus       _eventBus;
    private BdcCommandGateway?         _gateway;

    /// <param name="state">Form state instance shared with the application shell.</param>
    /// <param name="eventBus">Event bus used to publish local spawn commands on submit.</param>
    public MiniIosPanel(MiniIosPanelState state, FdpEventBus eventBus)
    {
        _state    = state    ?? throw new ArgumentNullException(nameof(state));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// Injects the live command gateway so the Spawn button routes requests to SimHost
    /// over DDS rather than publishing a local <see cref="FdpEventBus"/> command.
    /// Pass <c>null</c> to fall back to the local event-bus path (offline mode).
    /// </summary>
    public void SetGateway(BdcCommandGateway? gateway) => _gateway = gateway;

    /// <summary>
    /// Emits the Mini IOS ImGui window.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        if (!ImGui.Begin("Mini IOS"))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Entity Spawner");
        ImGui.Separator();

        // ── TKB type ──────────────────────────────────────────────────────────
        string tkbTypeStr = _state.TkbType.ToString();
        if (ImGui.InputText("TKB Type", ref tkbTypeStr, 20))
        {
            if (long.TryParse(tkbTypeStr, out long parsed))
                _state.TkbType = parsed;
        }

        // ── Affiliation ───────────────────────────────────────────────────────
        int affil = (int)_state.Affiliation;
        if (ImGui.Combo("Affiliation", ref affil, "Unknown\0Friend\0Hostile\0Neutral\0"))
            _state.Affiliation = (ForceId)affil;

        ImGui.Separator();

        // ── Coordinates ───────────────────────────────────────────────────────
        float px = _state.PositionX;
        if (ImGui.InputFloat("Pos X (m)", ref px))
            _state.PositionX = px;

        float py = _state.PositionY;
        if (ImGui.InputFloat("Pos Y (m)", ref py))
            _state.PositionY = py;

        ImGui.Separator();

        // ── Submit ────────────────────────────────────────────────────────────
        if (ImGui.Button("Spawn"))
            _state.SubmitViaGateway(_gateway);

        ImGui.SameLine();

        if (ImGui.Button("Spawn Moving Vehicle"))
            _ = _state.SubmitWithWanderMissionViaGateway(_gateway);

        ImGui.End();
    }
}
