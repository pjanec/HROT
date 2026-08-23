using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Editor.UI;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Editor.Windows;

/// <summary>Editor subsystem title-bar colour (slate blue).</summary>
internal static class EditorWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.15f, 0.22f, 0.48f, 1f);
}

/// <summary>Editor toolbar panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class EditorToolbarWindow : ManagedWindow
{
    internal const string Kind = "editor-toolbar";

    private readonly EditorToolbarPanel _panel;
    private readonly IEditorLogic       _logic;

    public EditorToolbarWindow(EditorToolbarPanel panel, IEditorLogic logic)
        : base("editor_toolbar", "Editor Toolbar", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private EditorToolbarPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_logic, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal EditorToolbarPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_logic);
    }
}

/// <summary>Editor ORBAT panel as a perspective-bound managed window.</summary>
internal sealed class EditorOrbatWindow : ManagedWindow
{
    private readonly EditorOrbatPanel _panel;
    private readonly IEditorLogic     _logic;

    public EditorOrbatWindow(EditorOrbatPanel panel, IEditorLogic logic)
        : base("editor_orbat", "Editor ORBAT", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
}

/// <summary>Entity spawner panel (shared with ExCon) as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Spawner"/>, shared with the
/// (not yet converted) ExCon host of the same <see cref="SpawnerPanel"/>.</summary>
internal sealed class EditorSpawnerWindow : ManagedWindow
{
    private readonly SpawnerPanel    _panel;
    private readonly ISpawnController _spawn;

    public EditorSpawnerWindow(SpawnerPanel panel, ISpawnController spawn)
        : base("editor_spawner", "Entity Spawner", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _spawn = spawn;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
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

/// <summary>Mission editor panel (shared) as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Mission"/>. ⚠⚠ CORRECTED — an
/// earlier commit in this sweep claimed no ExCon host exists for this panel; that was a false
/// negative (checked usages everywhere except <c>ExConWindows.cs</c>, which hosts it as
/// <c>ExConMissionWindow</c>). Both hosts now cite the shared constant.</summary>
internal sealed class EditorMissionWindow : ManagedWindow
{
    internal const string Kind = PanelIds.Mission;

    private readonly MissionPanel          _panel;
    private readonly IMissionEditorService _svc;
    private readonly IMapPickService       _pick;

    public EditorMissionWindow(MissionPanel panel, IMissionEditorService svc, IMapPickService pick)
        : base("editor_mission", "Mission Editor", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _svc   = svc;
        _pick  = pick;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private MissionPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
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

/// <summary>Map layer / grid configuration panel (shared) as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Config"/>, shared with the
/// (not yet converted) ExCon host of the same <see cref="ConfigPanel"/>.</summary>
internal sealed class EditorConfigWindow : ManagedWindow
{
    private readonly ConfigPanel         _panel;
    private readonly IMapConfigController _ctrl;

    public EditorConfigWindow(ConfigPanel panel, IMapConfigController ctrl)
        : base("editor_config", "Map Configuration", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
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

/// <summary>Shared ORBAT tree with drag-and-drop embarkation as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.SharedOrbat"/>, shared with the
/// (not yet converted) ExCon host of the same <see cref="SharedOrbatPanel"/>.</summary>
internal sealed class EditorSharedOrbatWindow : ManagedWindow
{
    private readonly SharedOrbatPanel   _panel;
    private readonly IOrbatDataProvider _data;
    private readonly IOrbatController   _ctrl;

    public EditorSharedOrbatWindow(SharedOrbatPanel panel, IOrbatDataProvider data, IOrbatController ctrl)
        : base("editor_shared_orbat", "ORBAT Tree", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _data  = data;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private SharedOrbatPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_data, Id, PanelIds.SharedOrbat);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal SharedOrbatPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_data, _ctrl);
    }
}

/// <summary>Preview/Edit mode toggle panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Preview"/>, shared with the
/// (not yet converted) ExCon host of the same <see cref="PreviewPanel"/>.</summary>
internal sealed class EditorPreviewWindow : ManagedWindow
{
    private readonly PreviewPanel       _panel;
    private readonly IPreviewController _ctrl;

    public EditorPreviewWindow(PreviewPanel panel, IPreviewController ctrl)
        : base("editor_preview", "Preview", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private PreviewPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_ctrl, Id, PanelIds.Preview);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal PreviewPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_ctrl);
    }
}

/// <summary>Zone editor (road network + LOS obstacle placement) as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.ZoneEditor"/>. ⚠ No ExCon host
/// exists for this one (measured) — single host, but the constant already lives in
/// <c>PanelIds</c> alongside its four siblings for consistency.</summary>
internal sealed class EditorZoneEditorWindow : ManagedWindow
{
    private readonly ZoneEditorPanel          _panel;
    private readonly IZoneAuthoringController _ctrl;

    public EditorZoneEditorWindow(ZoneEditorPanel panel, IZoneAuthoringController ctrl)
        : base("editor_zone_editor", "Zone Editor", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private ZoneEditorPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.ZoneEditor);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal ZoneEditorPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_ctrl);
    }
}
