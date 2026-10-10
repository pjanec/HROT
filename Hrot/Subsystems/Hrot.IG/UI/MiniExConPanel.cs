using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Core.Network;
using Fdp.Core;
using ImGuiNET;
using Fdp.Interfaces;
using Hrot.Core.Mission;
using Hrot.UI.Common.AddEntity;
using NodeEditor.UI.Picker;

namespace Hrot.IG.UI;

/// <summary>⭐⭐⭐ U-obs-5 (group 6) — the whole of what <see cref="MiniExConPanel"/> shows, this frame.
/// ⚠ A plain panel: no <see cref="PanelId"/>/<see cref="PanelKind"/> of its own — the HOST
/// (<c>IgMiniExConWindow</c>) supplies both.</summary>
public sealed record MiniExConPanelViewModel(
    string PanelId, string PanelKind, long TkbType, string Affiliation,
    bool UseSpecificCoordinates, float PositionX, float PositionY, float RandomSpawnRadius) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

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

    private Func<ITkbDatabase?>? _tkb;
    private Func<Action<PickerRequest, Action<PickerResult>>?>? _openPicker;

    /// <summary>⭐ <c>CE-1017</c> S4 — gives the panel the grouped Add Entity type picker (the host's TKB and its
    /// <c>PickerRegistry.OpenPicker</c>, both resolved at call time). Without it the panel keeps the raw type-number
    /// field only. 📄 docs/DESIGN_Add_Entity_Picker.md S4.</summary>
    public void SetPicker(Func<ITkbDatabase?> tkb, Func<Action<PickerRequest, Action<PickerResult>>?> openPicker)
    {
        _tkb = tkb;
        _openPicker = openPicker;
    }

    /// <summary>Opens the type picker for the panel's affiliation; the pick becomes the TKB type the Spawn buttons
    /// use (this panel spawns at coordinates, so a pick SELECTS rather than arms a tool). False ⇒ no picker.</summary>
    public bool HandleChooseType()
    {
        var tkb = _tkb?.Invoke();
        var open = _openPicker?.Invoke();
        if (tkb is null || open is null) return false;

        eForceIdentifier side = _state.Affiliation switch
        {
            ForceId.Friend  => eForceIdentifier.FORCE_FRIENDLY,
            ForceId.Hostile => eForceIdentifier.FORCE_OPPOSING,
            _               => eForceIdentifier.FORCE_NEUTRAL,
        };
        open(AddEntityAction.BuildRequest(tkb, side), result =>
        {
            if (result.First?.Tag is EntityTypeEntry e) _state.TkbType = e.TkbType;
        });
        return true;
    }

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

    /// <summary>⭐⭐⭐ BUILD — a pure projection of <see cref="MiniExConPanelState"/>. No ImGui.</summary>
    public MiniExConPanelViewModel BuildViewModel(string panelId, string panelKind) => new(
        panelId, panelKind, _state.TkbType, _state.Affiliation.ToString(),
        _state.UseSpecificCoordinates, _state.PositionX, _state.PositionY, _state.RandomSpawnRadius);

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
        if (_tkb?.Invoke() is { } tkb && _openPicker?.Invoke() is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Choose..."))   // CE-1017 S4
                HandleChooseType();
            if (tkb.TryGetByType(_state.TkbType, out var t))
                ImGui.TextDisabled(t.Name);
        }

        // ── Affiliation ───────────────────────────────────────────────────────
        int affil = (int)_state.Affiliation;
        // ⚠ CE-1017 — the labels follow ForceId's values (Neutral=0, Friend=1, Hostile=2). They used to read
        //   "Unknown, Friend, Hostile, Neutral": "Unknown" was really Neutral and "Neutral" wrote the undefined 3.
        if (ImGui.Combo("Affiliation", ref affil, "Neutral\0Friend\0Hostile\0"))
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
