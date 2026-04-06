using System.Numerics;
using FDP.Toolkit.ImGui.WindowManager;
using Hrot.Editor;
using Hrot.Editor.UI;

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
