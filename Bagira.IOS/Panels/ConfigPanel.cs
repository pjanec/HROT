using System;
using ImGuiNET;
using Newtonsoft.Json;
using FDP.Kernel.Logging;

namespace Bagira.IOS.Panels;

/// <summary>
/// IOS UI panel that lets the operator configure the IG map interactively.
///
/// <para>The panel maintains the current UI settings as simple state fields.
/// When the operator presses "SEND CONFIG PATCH", <see cref="HandleSendConfigPatch"/>
/// calls <see cref="BuildPatch"/> to serialise those settings into a JSON Merge
/// Patch (RFC 7396) and forwards the string to
/// <see cref="IIosLogic.SendConfigPatch"/>.</para>
///
/// <para><b>Testing:</b> Because ImGui cannot be driven in unit tests, all
/// business logic lives in <c>Handle*</c> methods and <see cref="BuildPatch"/>,
/// both of which are callable without an active render frame.
/// The <see cref="Draw"/> method contains only the ImGui boilerplate and is
/// wired up during Phase P9 (Application Shell).</para>
/// </summary>
public sealed class ConfigPanel
{
    // ── Tool list (ordered; index maps to Combo widget) ───────────────────────

    /// <summary>Ordered list of map tools available in the toolbar combo-box.</summary>
    public static readonly string[] Tools =
        { "Navigation", "Selection", "Placement", "Measure" };

    // ── State ─────────────────────────────────────────────────────────────────

    private int   _selectedTool    = 0;
    private bool  _satelliteLayer  = true;
    private bool  _tacticalGraphics = true;
    private bool  _airUnits        = false;
    private bool  _grid            = false;
    private float _iconScale       = PanelConstants.IconScaleDefault;

    // ── Public state accessors ────────────────────────────────────────────────

    /// <summary>Index into <see cref="Tools"/>. Clamped to valid range on set.</summary>
    public int SelectedTool
    {
        get => _selectedTool;
        set => _selectedTool = Math.Clamp(value, 0, Tools.Length - 1);
    }

    public bool  SatelliteLayer   { get => _satelliteLayer;   set => _satelliteLayer   = value; }
    public bool  TacticalGraphics { get => _tacticalGraphics; set => _tacticalGraphics = value; }
    public bool  AirUnits         { get => _airUnits;         set => _airUnits         = value; }
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
            interaction = new
            {
                activeTool = Tools[_selectedTool]
            },
            view = new
            {
                iconScale = _iconScale,
                layers = new
                {
                    satellite         = _satelliteLayer,
                    tactical_graphics = _tacticalGraphics,
                    air               = _airUnits,
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
    public void HandleSendConfigPatch(IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        FdpLog<ConfigPanel>.Debug("[TRACE-IOS] Config: Sending JSON Patch");
        logic.SendConfigPatch(BuildPatch());
    }

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the panel using ImGui. Called once per frame from the
    /// application shell (Phase P9 wires this up with rlImGui).
    /// All decision-making is delegated to the <c>Handle*</c> methods above.
    /// </summary>
    public void Draw(IIosLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        IosPanelColors.Push();
        ImGui.Begin("Map Configuration");
        IosPanelColors.Pop();

        ImGui.Combo("Tool", ref _selectedTool, Tools, Tools.Length);
        ImGui.Checkbox("Satellite Layer",    ref _satelliteLayer);
        ImGui.Checkbox("Tactical Graphics",  ref _tacticalGraphics);
        ImGui.Checkbox("Air Units",          ref _airUnits);
        ImGui.Checkbox("Grid",               ref _grid);

        float scale = _iconScale;
        if (ImGui.SliderFloat("Icon Scale", ref scale, PanelConstants.IconScaleMin, PanelConstants.IconScaleMax))
            IconScale = scale;

        if (ImGui.Button("SEND CONFIG PATCH"))
            HandleSendConfigPatch(logic);

        ImGui.End();
    }
}
