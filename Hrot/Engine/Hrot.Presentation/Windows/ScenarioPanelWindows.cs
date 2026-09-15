using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐⭐⭐ <b>THE shared <see cref="ManagedWindow"/> wrappers for the Scenario-perspective panels.</b>
/// 📄 <c>docs/DESIGN_Cgf_Scenario_Windows_Slice.md</c> — Axis-C <b>E5</b> item ①.
///
/// <para>🔒 <b>User, <c>2026-08-27</c>, <c>--mode all</c>:</b> *"the editor has many windows in its
/// Scenario perspective like mission editor, orbat, entity placement, entity spawner, cgf offers just
/// Entity inspector, Event Browser, architecture diagnostic, System profiler."* ⚠⚠ And <c>--mode all</c>
/// composes <c>orchestrator,simhost,ig,excon,cgf</c> — ⛔ <b>no editor</b> — so on that path CGF IS the
/// editor and every window it lacks is a missing feature.</para>
///
/// <para>⭐⭐ <b>Why these types are NEW but the code is not.</b> 📐 Measured: the PANELS
/// (<c>Hrot.Presentation/Panels</c>) and the FACADE interfaces (<c>…/Facades</c>) were already shared —
/// what was duplicated is this thin wrapper, written twice with the same body:
/// <c>Hrot.Editor/Windows/EditorWindows.cs</c> and <c>Hrot.ExCon/Windows/ExConWindows.cs</c>, differing
/// only in id, title, perspective and title-bar colour. ⇒ ⭐ those four become <b>arguments</b> and there
/// is one implementation (ruling 9), reachable by every host — which is the whole point, since
/// <c>Hrot.Editor → Hrot.CGF</c> means CGF could never see the editor's copy.</para>
///
/// <para>⭐ <b>The <c>SimulateDrawClientArea</c> seam is preserved</b> on each type: the existing
/// <c>ui-probe</c> / panel-model rails drive the view model without an ImGui frame, and dropping it would
/// silently retire them.</para>
///
/// <para>⚠ <b>Preview and Zone Editor are deliberately NOT here</b> (design §4): <c>IPreviewController</c>
/// is the editor's planning-vs-running state, which a cluster node does not have, and the zone adapter
/// still reaches <c>Hrot.Editor.Gizmos.LocationPickerGizmo</c>. ⛔ Adding them here before those two are
/// resolved would put a window on CGF that cannot be serviced — ruling 49.</para>
/// </summary>
public static class ScenarioPanelWindowIds
{
    /// <summary>⭐ The editor's historical ids, kept verbatim so layout files and id-keyed rails resolve.</summary>
    public const string EditorSpawner = "editor_spawner";
    /// <inheritdoc cref="EditorSpawner"/>
    public const string EditorMission = "editor_mission";
    /// <inheritdoc cref="EditorSpawner"/>
    public const string EditorConfig  = "editor_config";
    /// <inheritdoc cref="EditorSpawner"/>
    public const string EditorOrbat   = "editor_shared_orbat";

    /// <summary>⭐ CGF's ids. ⚠ DISTINCT from the editor's on purpose — the two hosts can never run in one
    /// process (<c>HrotRunnerConfiguration.Validate</c> rejects it), but a shared id would make a layout
    /// file written by one host silently reposition the other's window.</summary>
    public const string CgfSpawner = "cgf_spawner";
    /// <inheritdoc cref="CgfSpawner"/>
    public const string CgfMission = "cgf_mission";
    /// <inheritdoc cref="CgfSpawner"/>
    public const string CgfConfig  = "cgf_config";
    /// <inheritdoc cref="CgfSpawner"/>
    public const string CgfOrbat   = "cgf_shared_orbat";
}

/// <summary>⭐ Entity spawner panel as a perspective-bound managed window. ⛔ One implementation, every host.</summary>
public sealed class SpawnerPanelWindow : ManagedWindow
{
    private readonly SpawnerPanel     _panel;
    private readonly ISpawnController _spawn;

    /// <param name="panel">The shared panel.</param>
    /// <param name="spawn">The host's spawn facade.</param>
    /// <param name="id">Window id — ⚠ per host, see <see cref="ScenarioPanelWindowIds"/>.</param>
    /// <param name="perspective">Owning perspective, normally <c>"Scenario"</c>.</param>
    /// <param name="titleBarColor">The host's title-bar colour, so windows stay visually attributable.</param>
    public SpawnerPanelWindow(
        SpawnerPanel panel, ISpawnController spawn, string id, string perspective, Vector4 titleBarColor)
        : base(id, "Entity Spawner", perspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _spawn = spawn;
        IsOpen        = true;
        TitleBarColor = titleBarColor;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private SpawnerPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.Spawner);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Headless seam for the panel-model rails.</summary>
    public SpawnerPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_spawn);
    }
}

/// <summary>⭐ Mission editor panel as a perspective-bound managed window.</summary>
public sealed class MissionPanelWindow : ManagedWindow
{
    /// <summary>⭐ The readable panel kind — SHARED with ExCon's host of the same panel.</summary>
    public const string Kind = PanelIds.Mission;

    private readonly MissionPanel          _panel;
    private readonly IMissionEditorService _svc;
    private readonly IMapPickService       _pick;

    /// <param name="pick">⭐ The host's map-pick facade. 📐 CGF passes the already-shared
    /// <c>CanvasMapPickAdapter</c> it constructs for the inspector — ⛔ no new implementation
    /// (design §8 D2 records why the editor's own copy is NOT reconciled in this slice).</param>
    public MissionPanelWindow(
        MissionPanel panel, IMissionEditorService svc, IMapPickService pick,
        string id, string perspective, Vector4 titleBarColor)
        : base(id, "Mission Editor", perspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _svc   = svc;
        _pick  = pick;
        IsOpen        = true;
        TitleBarColor = titleBarColor;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private MissionPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Headless seam for the panel-model rails.</summary>
    public MissionPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_svc, _pick);
    }
}

/// <summary>⭐ Map layer / grid configuration panel as a perspective-bound managed window.</summary>
public sealed class ConfigPanelWindow : ManagedWindow
{
    private readonly ConfigPanel          _panel;
    private readonly IMapConfigController _ctrl;

    public ConfigPanelWindow(
        ConfigPanel panel, IMapConfigController ctrl, string id, string perspective, Vector4 titleBarColor)
        : base(id, "Map Configuration", perspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = titleBarColor;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private ConfigPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.Config);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Headless seam for the panel-model rails.</summary>
    public ConfigPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_ctrl);
    }
}

/// <summary>⭐ Shared ORBAT tree with drag-and-drop embarkation as a perspective-bound managed window.</summary>
public sealed class SharedOrbatPanelWindow : ManagedWindow
{
    private readonly SharedOrbatPanel   _panel;
    private readonly IOrbatDataProvider _data;
    private readonly IOrbatController   _ctrl;

    public SharedOrbatPanelWindow(
        SharedOrbatPanel panel, IOrbatDataProvider data, IOrbatController ctrl,
        string id, string perspective, Vector4 titleBarColor)
        : base(id, "ORBAT Tree", perspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _data  = data;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = titleBarColor;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private SharedOrbatPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_data, Id, PanelIds.SharedOrbat);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Headless seam for the panel-model rails.</summary>
    public SharedOrbatPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_data, _ctrl);
    }
}
