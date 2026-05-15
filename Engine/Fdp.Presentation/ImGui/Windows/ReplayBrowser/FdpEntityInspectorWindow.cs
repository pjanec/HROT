using System;
using System.Numerics;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts a replay-scoped <see cref="EntityInspectorPanel"/>.
/// Uses factory delegates to obtain the session and inspector state on each frame so that
/// the panel always reflects the current sandbox repository state.
/// </summary>
public sealed class FdpEntityInspectorWindow : ManagedWindow
{
    private readonly EntityInspectorPanel _panel;
    private readonly Func<IInspectableSession?> _sessionFactory;
    private readonly Func<InspectorState> _stateFactory;

    public FdpEntityInspectorWindow(
        string id,
        string title,
        string owningPerspective,
        EntityInspectorPanel panel,
        Func<IInspectableSession?> sessionFactory,
        Func<InspectorState> stateFactory,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _sessionFactory = sessionFactory;
        _stateFactory = stateFactory;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }

    protected override void DrawClientArea()
    {
        var session = _sessionFactory();
        if (session == null) return;
        var state = _stateFactory();
        _panel.DrawContent(session, state);
    }
}
