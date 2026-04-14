using System;
using System.Numerics;
using Fdp.Toolkit.ImGui.Abstractions;
using Fdp.Toolkit.ImGui.Adapters;
using Fdp.Toolkit.ImGui.Panels;
using Fdp.Toolkit.ImGui.WindowManager;

namespace Hrot.Presentation.Windows;

/// <summary>
/// FDP Entity Inspector managed window for a specific subsystem perspective.
/// </summary>
public sealed class FdpEntityInspectorWindow : ManagedWindow
{
    private readonly EntityInspectorPanel _panel;
    private readonly Func<RepositoryAdapter?> _adapterGetter;
    private readonly Func<InspectorState>     _stateGetter;

    public FdpEntityInspectorWindow(
        string id,
        string title,
        string owningPerspective,
        EntityInspectorPanel panel,
        Func<RepositoryAdapter?> adapterGetter,
        Func<InspectorState> stateGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel         = panel;
        _adapterGetter = adapterGetter;
        _stateGetter   = stateGetter;
        IsOpen         = true;
        TitleBarColor  = titleBarColor;
    }

    protected override void DrawClientArea()
    {
        var adapter = _adapterGetter();
        if (adapter == null) return;
        _panel.DrawContent(adapter, _stateGetter());
    }
}

/// <summary>
/// FDP Event Browser managed window for a specific subsystem perspective.
/// </summary>
public sealed class FdpEventBrowserWindow : ManagedWindow
{
    private readonly EventBrowserPanel _panel;

    public FdpEventBrowserWindow(
        string id,
        string title,
        string owningPerspective,
        EventBrowserPanel panel,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = titleBarColor;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
