using System.Numerics;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts a replay-scoped <see cref="EventBrowserPanel"/>.
/// </summary>
public sealed class FdpEventBrowserWindow : ManagedWindow
{
    private readonly EventBrowserPanel _panel;

    public FdpEventBrowserWindow(
        string id,
        string title,
        string owningPerspective,
        EventBrowserPanel panel,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
