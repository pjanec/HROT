using System.Numerics;
using FDP.Toolkit.ImGui.WindowManager;
using Hrot.Editor;
using Hrot.Editor.UI;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.ClusterRunner.Windows;

/// <summary>Editor subsystem title-bar colour (slate blue).</summary>
internal static class EditorWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.15f, 0.22f, 0.48f, 1f);
}

/// <summary>Editor toolbar panel as a perspective-bound managed window.</summary>
internal sealed class EditorToolbarWindow : ManagedWindow
{
    private readonly EditorToolbarPanel _panel;
    private readonly IEditorLogic       _logic;

    public EditorToolbarWindow(EditorToolbarPanel panel, IEditorLogic logic)
        : base("editor_toolbar", "Editor Toolbar", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
}

/// <summary>Scenario file browser panel as a perspective-bound managed window.</summary>
internal sealed class EditorBrowserWindow : ManagedWindow
{
    private readonly ScenarioBrowserPanel _panel;
    private readonly IEditorLogic         _logic;

    public EditorBrowserWindow(ScenarioBrowserPanel panel, IEditorLogic logic)
        : base("editor_browser", "Scenario Browser", "Editor", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
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

/// <summary>Entity spawner panel (shared with ExCon) as a perspective-bound managed window.</summary>
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
    }

    protected override void DrawClientArea() => _panel.Draw(_spawn);
}

/// <summary>Mission editor panel (shared) as a perspective-bound managed window.</summary>
internal sealed class EditorMissionWindow : ManagedWindow
{
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
    }

    protected override void DrawClientArea() => _panel.DrawContent(_svc, _pick);
}

/// <summary>Map layer / grid configuration panel (shared) as a perspective-bound managed window.</summary>
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
    }

    protected override void DrawClientArea() => _panel.DrawContent(_ctrl);
}

/// <summary>Shared ORBAT tree with drag-and-drop embarkation as a perspective-bound managed window.</summary>
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
    }

    protected override void DrawClientArea() => _panel.DrawContent(_data, _ctrl);
}

/// <summary>Preview/Edit mode toggle panel as a perspective-bound managed window.</summary>
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
    }

    protected override void DrawClientArea() => _panel.DrawContent(_ctrl);
}

/// <summary>Zone editor (road network + LOS obstacle placement) as a perspective-bound managed window.</summary>
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
    }

    protected override void DrawClientArea() => _panel.DrawContent(_ctrl);
}
