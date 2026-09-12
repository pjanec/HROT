using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
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

/// <summary>ExCon Map Configuration panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Config"/>, shared with
/// <c>Hrot.Editor</c>'s <c>EditorConfigWindow</c>.</summary>
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
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private ConfigPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.Config);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal ConfigPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_ctrl);
    }
}

/// <summary>ExCon ORBAT Tree panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.
/// Not the same panel as <c>SharedOrbatPanel</c> (group 5) — see <c>OrbatPanelViewModel</c>'s own
/// remarks.</summary>
internal sealed class ExConOrbatWindow : ManagedWindow
{
    internal const string Kind = "excon-orbat";

    private readonly OrbatPanel  _panel;
    private readonly IExConLogic _logic;

    public ExConOrbatWindow(OrbatPanel panel, IExConLogic logic)
        : base("excon_orbat", "ORBAT Tree", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private OrbatPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_logic, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal OrbatPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_logic);
    }
}

/// <summary>ExCon Selection &amp; Mission panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Mission"/>, shared with
/// <c>Hrot.Editor</c>'s <c>EditorMissionWindow</c>.</summary>
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
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private MissionPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.Mission);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal MissionPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_svc, _pick);
    }
}

/// <summary>ExCon Data Monitor (interaction log) panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class ExConDataMonitorWindow : ManagedWindow
{
    internal const string Kind = "excon-data-monitor";

    private readonly InteractionPanel _panel;
    private readonly IExConLogic      _logic;

    public ExConDataMonitorWindow(InteractionPanel panel, IExConLogic logic)
        : base("excon_data_monitor", "Data Monitor", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private InteractionPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal InteractionPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_logic);
    }
}

/// <summary>ExCon Entity Spawner panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Spawner"/>, shared with
/// <c>Hrot.Editor</c>'s <c>EditorSpawnerWindow</c>.</summary>
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
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private SpawnerPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.Spawner);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal SpawnerPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_spawn);
    }
}

/// <summary>ExCon Diagnostics panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class ExConDiagnosticsWindow : ManagedWindow
{
    internal const string Kind = "excon-diagnostics";

    private readonly DiagnosticsPanel _panel;
    private readonly IExConLogic      _logic;

    public ExConDiagnosticsWindow(DiagnosticsPanel panel, IExConLogic logic)
        : base("excon_diagnostics", "Diagnostics", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private DiagnosticsPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_logic, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal DiagnosticsPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_logic);
    }
}

/// <summary>ExCon DER Entity Inspector panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5, follow-up — <c>DerEntityInspectorPanel</c> had no production host anywhere
/// (measured as one of the six no-host panels, <c>BP-467</c>); this is that host. ⚠ No sibling host:
/// single host, kind stays a local literal.</summary>
internal sealed class ExConDerEntityInspectorWindow : ManagedWindow
{
    internal const string Kind = "excon-der-entity-inspector";

    private readonly DerEntityInspectorPanel _panel;
    private readonly IExConLogic             _logic;

    public ExConDerEntityInspectorWindow(DerEntityInspectorPanel panel, IExConLogic logic)
        : base("excon_der_entity_inspector", "DER Entity Inspector", "ExCon", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen = true;
        TitleBarColor = ExConWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private DerEntityInspectorPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_logic.Repo, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal DerEntityInspectorPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_logic.Repo);
    }
}
