using System.Numerics;
using FDP.Toolkit.ImGui.WindowManager;
using Hrot.ExCon;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.ExCon.Windows;

/// <summary>
/// Violet title-bar colour used by all ExCon-perspective managed windows.
/// Matches <c>ExConPanelColors.TitleBg</c> defined in <c>Hrot.ExCon</c>.
/// </summary>
internal static class ExConWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.32f, 0.08f, 0.48f, 1f);
}

/// <summary>ExCon Map Configuration panel as a perspective-bound managed window.</summary>
internal sealed class ExConConfigWindow : ManagedWindow
{
    private readonly ConfigPanel         _panel;
    private readonly IMapConfigController _ctrl;

    public ExConConfigWindow(ConfigPanel panel, IMapConfigController ctrl)
        : base("excon_config", "Map Configuration", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_ctrl);
}

/// <summary>ExCon ORBAT Tree panel as a perspective-bound managed window.</summary>
internal sealed class ExConOrbatWindow : ManagedWindow
{
    private readonly OrbatPanel  _panel;
    private readonly IExConLogic _logic;

    public ExConOrbatWindow(OrbatPanel panel, IExConLogic logic)
        : base("excon_orbat", "ORBAT Tree", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
}

/// <summary>ExCon Selection &amp; Mission panel as a perspective-bound managed window.</summary>
internal sealed class ExConMissionWindow : ManagedWindow
{
    private readonly MissionPanel         _panel;
    private readonly IMissionEditorService _svc;
    private readonly IMapPickService       _pick;

    public ExConMissionWindow(MissionPanel panel, IMissionEditorService svc, IMapPickService pick)
        : base("excon_mission", "Selection & Mission", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _svc   = svc;
        _pick  = pick;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_svc, _pick);
}

/// <summary>ExCon Data Monitor (interaction log) panel as a perspective-bound managed window.</summary>
internal sealed class ExConDataMonitorWindow : ManagedWindow
{
    private readonly InteractionPanel _panel;
    private readonly IExConLogic      _logic;

    public ExConDataMonitorWindow(InteractionPanel panel, IExConLogic logic)
        : base("excon_data_monitor", "Data Monitor", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
}

/// <summary>ExCon Entity Spawner panel as a perspective-bound managed window.</summary>
internal sealed class ExConSpawnerWindow : ManagedWindow
{
    private readonly SpawnerPanel   _panel;
    private readonly ISpawnController _spawn;

    public ExConSpawnerWindow(SpawnerPanel panel, ISpawnController spawn)
        : base("excon_spawner", "Entity Spawner", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _spawn = spawn;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_spawn);
}

/// <summary>ExCon Diagnostics panel as a perspective-bound managed window.</summary>
internal sealed class ExConDiagnosticsWindow : ManagedWindow
{
    private readonly DiagnosticsPanel _panel;
    private readonly IExConLogic      _logic;

    public ExConDiagnosticsWindow(DiagnosticsPanel panel, IExConLogic logic)
        : base("excon_diagnostics", "Diagnostics", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
}
