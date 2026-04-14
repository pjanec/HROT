using System;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Fdp.Core;
using ImGuiNET;

namespace Hrot.IG.UI;

/// <summary>
/// ImGui panel providing a lightweight ExCon-style entity spawner (IG.5.3).
///
/// Renders a "Mini ExCon" window containing:
/// <list type="bullet">
///   <item>A TKB type ID input field.</item>
///   <item>An affiliation combo box.</item>
///   <item>X / Y coordinate inputs for initial placement.</item>
///   <item>A "Spawn" button that calls <see cref="MiniExConPanelState.Submit"/>.</item>
/// </list>
///
/// All mutable state lives in <see cref="MiniExConPanelState"/> so that the form
/// data can be exercised in tests without invoking ImGui.
///
/// Call <see cref="Draw"/> each frame between <c>rlImGui.Begin()</c> and
/// <c>rlImGui.End()</c>.
/// </summary>
public class MiniExConPanel
{
    private readonly MiniExConPanelState _state;
    private readonly FdpEventBus       _eventBus;
    private ICommandGateway?           _gateway;

    /// <param name="state">Form state instance shared with the application shell.</param>
    /// <param name="eventBus">Event bus used to publish local spawn commands on submit.</param>
    public MiniExConPanel(MiniExConPanelState state, FdpEventBus eventBus)
    {
        _state    = state    ?? throw new ArgumentNullException(nameof(state));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// Injects the live command gateway so the Spawn button routes requests to SimHost
    /// over DDS rather than publishing a local <see cref="FdpEventBus"/> command.
    /// Pass <c>null</c> to fall back to the local event-bus path (offline mode).
    /// </summary>
    public void SetGateway(ICommandGateway? gateway) => _gateway = gateway;

    /// <summary>
    /// Emits the Mini ExCon ImGui window.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        if (!ImGui.Begin("Mini ExCon")) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
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
        bool useCoords = _state.UseSpecificCoordinates;
        if (ImGui.Checkbox("Use specific coordinates", ref useCoords))
            _state.UseSpecificCoordinates = useCoords;

        if (_state.UseSpecificCoordinates)
        {
            float px = _state.PositionX;
            if (ImGui.InputFloat("Pos X (m)", ref px))
                _state.PositionX = px;

            float py = _state.PositionY;
            if (ImGui.InputFloat("Pos Y (m)", ref py))
                _state.PositionY = py;
        }
        else
        {
            ImGui.TextDisabled($"Random position within {_state.RandomSpawnRadius:F0} m of origin");
        }

        ImGui.Separator();

        // ── Submit ────────────────────────────────────────────────────────────
        if (ImGui.Button("Spawn"))
            _state.SubmitViaGateway(_gateway);

        ImGui.SameLine();

        if (ImGui.Button("Spawn Moving Vehicle"))
            _ = _state.SubmitWithWanderMissionViaGateway(_gateway);
    }
}
