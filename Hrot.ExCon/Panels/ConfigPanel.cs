using System;
using ImGuiNET;
using Newtonsoft.Json;
using FDP.Kernel.Logging;

namespace Hrot.ExCon.Panels;

/// <summary>
/// ExCon UI panel that lets the operator configure the IG map interactively.
///
/// <para>The panel maintains the current UI settings as simple state fields.
/// When the operator presses "SEND CONFIG PATCH", <see cref="HandleSendConfigPatch"/>
/// calls <see cref="BuildPatch"/> to serialise those settings into a JSON Merge
/// Patch (RFC 7396) and forwards the string to
/// <see cref="IExConLogic.SendConfigPatch"/>.</para>
///
/// <para><b>Testing:</b> Because ImGui cannot be driven in unit tests, all
/// business logic lives in <c>Handle*</c> methods and <see cref="BuildPatch"/>,
/// both of which are callable without an active render frame.
/// The <see cref="Draw"/> method contains only the ImGui boilerplate and is
/// wired up during Phase P9 (Application Shell).</para>
/// </summary>
public sealed class ConfigPanel
{
    // ── State ─────────────────────────────────────────────────────

    private bool  _satelliteLayer  = true;
    private bool  _groundUnits     = true;
    private bool  _airUnits        = true;
    private bool  _vehicles        = true;
    private bool  _tacticalGraphics = true;
    private bool  _roadGraphs      = true;
    private bool  _grid            = false;
    private float _iconScale       = PanelConstants.IconScaleDefault;

    // ── Public state accessors ────────────────────────────────────────────────

    public bool  SatelliteLayer   { get => _satelliteLayer;   set => _satelliteLayer   = value; }
    public bool  GroundUnits      { get => _groundUnits;      set => _groundUnits      = value; }
    public bool  TacticalGraphics { get => _tacticalGraphics; set => _tacticalGraphics = value; }
    public bool  AirUnits         { get => _airUnits;         set => _airUnits         = value; }
    public bool  Vehicles         { get => _vehicles;         set => _vehicles         = value; }
    public bool  RoadGraphs       { get => _roadGraphs;       set => _roadGraphs       = value; }
    public bool  Grid             { get => _grid;             set => _grid             = value; }

    /// <summary>Icon scale. Clamped to [<see cref="PanelConstants.IconScaleMin"/>, <see cref="PanelConstants.IconScaleMax"/>].</summary>
    public float IconScale
    {
        get => _iconScale;
        set => _iconScale = Math.Clamp(value, PanelConstants.IconScaleMin, PanelConstants.IconScaleMax);
    }

    // ── Core logic ────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises the current panel state into a JSON Merge Patch string.
    ///
    /// <para>Public so unit tests can assert the exact JSON structure
    /// independently of the UI render loop.</para>
    /// </summary>
    public string BuildPatch()
    {
        return JsonConvert.SerializeObject(new
        {
            view = new
            {
                iconScale = _iconScale,
                layers = new
                {
                    satellite         = _satelliteLayer,
                    units_ground      = _groundUnits,
                    units_air         = _airUnits,
                    vehicles          = _vehicles,
                    tactical_graphics = _tacticalGraphics,
                    road_graphs       = _roadGraphs,
                    grid              = _grid
                }
            }
        });
    }

    /// <summary>
    /// Invoked when the operator presses the "SEND CONFIG PATCH" button.
    /// Builds the JSON patch and forwards it to <paramref name="logic"/>.
    ///
    /// <para>Exposed as a public method so tests can simulate a button-click
    /// without requiring an active ImGui render frame.</para>
    /// </summary>
    public void HandleSendConfigPatch(IExConLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        FdpLog<ConfigPanel>.Debug("[TRACE-ExCon] Config: Sending JSON Patch");
        logic.SendConfigPatch(BuildPatch());
    }

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the panel using ImGui. Called once per frame from the
    /// application shell (Phase P9 wires this up with rlImGui).
    /// All decision-making is delegated to the <c>Handle*</c> methods above.
    /// </summary>
    public void Draw(IExConLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ExConPanelColors.Push();
        ImGui.Begin("Map Configuration");
        ExConPanelColors.Pop();

        ImGui.Checkbox("Satellite Layer",    ref _satelliteLayer);
        ImGui.Checkbox("Ground Units",       ref _groundUnits);
        ImGui.Checkbox("Air Units",          ref _airUnits);
        ImGui.Checkbox("Vehicles",           ref _vehicles);
        ImGui.Checkbox("Tactical Graphics",  ref _tacticalGraphics);
        ImGui.Checkbox("Routes",             ref _roadGraphs);
        ImGui.Checkbox("Grid",               ref _grid);

        float scale = _iconScale;
        if (ImGui.SliderFloat("Icon Scale", ref scale, PanelConstants.IconScaleMin, PanelConstants.IconScaleMax))
            IconScale = scale;

        if (ImGui.Button("SEND CONFIG PATCH"))
            HandleSendConfigPatch(logic);

        ImGui.End();
    }
}
