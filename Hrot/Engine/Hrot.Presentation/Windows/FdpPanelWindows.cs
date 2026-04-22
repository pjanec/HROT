using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

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

/// <summary>
/// Volatile dedicated watch window for a single entity.
/// Spawned on demand from the entity inspector context menu ("Inspect...").
/// Multiple instances may coexist for the same entity (each with its own
/// <see cref="EntityWatchPanel"/> and independent component expand/collapse state).
/// The window is automatically destroyed by the WindowManager when it is closed.
/// </summary>
public sealed class FdpEntityWatchWindow : ManagedWindow
{
    private readonly EntityWatchPanel _panel;
    private readonly Func<IInspectableSession?> _sessionGetter;

    public FdpEntityWatchWindow(
        string id,
        string title,
        string owningPerspective,
        EntityWatchPanel panel,
        Func<IInspectableSession?> sessionGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _sessionGetter = sessionGetter;
        IsOpen = true;
        TitleBarColor = titleBarColor;
        IsVolatile = true;
        ShowInMenu = false;
    }

    protected override void DrawClientArea()
    {
        var session = _sessionGetter();
        if (session == null) return;
        _panel.DrawContent(session);
    }
}
