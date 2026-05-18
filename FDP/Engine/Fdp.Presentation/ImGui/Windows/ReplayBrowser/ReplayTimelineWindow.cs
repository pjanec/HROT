using System.Numerics;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts <see cref="ReplayTimelinePanel"/>.
/// </summary>
public sealed class ReplayTimelineWindow : ManagedWindow
{
    private readonly ReplayTimelinePanel _panel;

    public ReplayTimelineWindow(
        string id,
        string title,
        string owningPerspective,
        ReplayTimelinePanel panel,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
