using System.Text.Json.Nodes;
using ImGuiNET;
using Fdp.Core.Logging;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ConfigPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠⚠ <b>Group 5 twin finding — read before touching this file.</b> This CLASS is physically
/// duplicated in the (unreferenced, not in any <c>.sln</c>) <c>Hrot.UI.Common</c> PROJECT under the
/// same path shape, but both copies declare namespace <c>Hrot.UI.Common.Panels</c> — the copy that
/// actually SHIPS is this one, compiled into <c>Hrot.Presentation.dll</c> (which <c>Hrot.Editor</c> and
/// <c>Hrot.ExCon</c> both reference). The <c>Hrot.UI.Common</c> project itself has zero
/// <c>ProjectReference</c>s anywhere in the repo — measured, and confirmed against
/// <c>docs/projects/Hrot/Engine/Hrot.UI.Common.md</c>'s own intent (a shared hexagonal-ports library
/// meant to be referenced, not source-copied). ⇒ only THIS copy is converted; the orphaned project's
/// copy is left untouched and reported as a finding for the coordinator (ruling-9 question: delete the
/// dead project, or wire it up as a real reference and delete this copy).</para>
/// </summary>
public sealed record ConfigPanelViewModel(
    string PanelId, string PanelKind,
    bool SatelliteLayer, bool GroundUnits, bool AirUnits, bool Vehicles,
    bool TacticalGraphics, bool RoadGraphs, bool Grid, float IconScale) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Shared UI panel that lets the operator configure the map layer visibility interactively.
///
/// <para>The panel maintains current UI settings as simple state fields.
/// When the operator presses "SEND CONFIG PATCH", <see cref="HandleSendConfigPatch"/>
/// calls <see cref="IMapConfigController.ApplyConfig"/> with a
/// <see cref="MapLayerState"/> built from the panel's current state.</para>
///
/// <para><b>Testing:</b> all business logic lives in <c>Handle*</c> methods and
/// state-setter properties, both of which are callable without an active render frame.</para>
/// </summary>
public sealed class ConfigPanel
{
    // ── State ─────────────────────────────────────────────────────────────────
    private readonly long _localNodeId;
    private bool  _satelliteLayer   = true;
    private bool  _groundUnits      = true;
    private bool  _airUnits         = true;
    private bool  _vehicles         = true;
    private bool  _tacticalGraphics = true;
    private bool  _roadGraphs       = true;
    private bool  _grid             = false;
    private float _iconScale        = PanelConstants.IconScaleDefault;

    /// <summary>Creates a new <see cref="ConfigPanel"/> with an optional node ID for log messages.</summary>
    public ConfigPanel(long localNodeId = 0) => _localNodeId = localNodeId;

    // ── Public state accessors ────────────────────────────────────────────────

    /// <summary>Whether the satellite/imagery base layer is visible.</summary>
    public bool SatelliteLayer   { get => _satelliteLayer;   set => _satelliteLayer   = value; }

    /// <summary>Whether the ground unit symbology layer is visible.</summary>
    public bool GroundUnits      { get => _groundUnits;      set => _groundUnits      = value; }

    /// <summary>Whether the tactical graphics layer is visible.</summary>
    public bool TacticalGraphics { get => _tacticalGraphics; set => _tacticalGraphics = value; }

    /// <summary>Whether the air unit symbology layer is visible.</summary>
    public bool AirUnits         { get => _airUnits;         set => _airUnits         = value; }

    /// <summary>Whether the vehicles layer is visible.</summary>
    public bool Vehicles         { get => _vehicles;         set => _vehicles         = value; }

    /// <summary>Whether the road graphs layer is visible.</summary>
    public bool RoadGraphs       { get => _roadGraphs;       set => _roadGraphs       = value; }

    /// <summary>Whether the coordinate grid overlay is visible.</summary>
    public bool Grid             { get => _grid;             set => _grid             = value; }

    /// <summary>Icon scale. Clamped to [<see cref="PanelConstants.IconScaleMin"/>, <see cref="PanelConstants.IconScaleMax"/>].</summary>
    public float IconScale
    {
        get => _iconScale;
        set => _iconScale = Math.Clamp(value, PanelConstants.IconScaleMin, PanelConstants.IconScaleMax);
    }

    // ── Core logic ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the current configuration from the controller, updating the four shared
    /// layer flags from <see cref="MapLayerState"/>.
    /// </summary>
    public void LoadConfig(IMapConfigController ctrl)
    {
        ArgumentNullException.ThrowIfNull(ctrl);
        var state     = ctrl.GetCurrentConfig();
        _satelliteLayer   = state.Satellite;
        _groundUnits      = state.GroundUnits;
        _airUnits         = state.AirUnits;
        _vehicles         = state.Vehicles;
        _tacticalGraphics = state.TacticalGraphics;
        _roadGraphs       = state.RoadGraphs;
        _grid             = state.Grid;
    }

    /// <summary>
    /// Invoked when the operator presses the "SEND CONFIG PATCH" button.
    /// Applies the current panel state via <see cref="IMapConfigController.ApplyConfig"/>.
    /// </summary>
    public void HandleSendConfigPatch(IMapConfigController ctrl)
    {
        ArgumentNullException.ThrowIfNull(ctrl);
        FdpLog<ConfigPanel>.Debug("[Node-{0}] Config: Applying config state", _localNodeId);
        ctrl.ApplyConfig(new MapLayerState(_satelliteLayer, _groundUnits, _airUnits, _vehicles, _tacticalGraphics, _roadGraphs, _grid));
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of current state. No ImGui.</summary>
    public ConfigPanelViewModel BuildViewModel(string panelId, string panelKind) => new(
        panelId, panelKind, _satelliteLayer, _groundUnits, _airUnits, _vehicles,
        _tacticalGraphics, _roadGraphs, _grid, _iconScale);

    // ── Draw ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders only the panel body content (no <c>ImGui.Begin</c>/<c>End</c>).
    /// Called by the Window Manager when this panel is hosted as a managed window.
    /// </summary>
    public void DrawContent(IMapConfigController ctrl)
    {
        ImGui.Checkbox("Satellite Layer",   ref _satelliteLayer);
        ImGui.Checkbox("Ground Units",      ref _groundUnits);
        ImGui.Checkbox("Air Units",         ref _airUnits);
        ImGui.Checkbox("Vehicles",          ref _vehicles);
        ImGui.Checkbox("Tactical Graphics", ref _tacticalGraphics);
        ImGui.Checkbox("Routes",            ref _roadGraphs);
        ImGui.Checkbox("Grid",              ref _grid);

        float scale = _iconScale;
        if (ImGui.SliderFloat("Icon Scale", ref scale, PanelConstants.IconScaleMin, PanelConstants.IconScaleMax))
            IconScale = scale;

        if (ImGui.Button("SEND CONFIG PATCH"))
            HandleSendConfigPatch(ctrl);
    }

    /// <summary>
    /// Renders the panel using ImGui. Called once per frame from the application shell.
    /// </summary>
    public void Draw(IMapConfigController ctrl)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ImGui.Begin("Map Configuration");
        DrawContent(ctrl);
        ImGui.End();
    }
}
