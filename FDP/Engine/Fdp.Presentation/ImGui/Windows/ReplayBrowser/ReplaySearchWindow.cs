using System.Numerics;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts <see cref="ReplaySearchPanel"/>.
/// Stage 4 stub: the panel implementation is deferred.
/// </summary>
public sealed class ReplaySearchWindow : ManagedWindow
{
    private readonly ReplaySearchPanel _panel;

    public ReplaySearchWindow(
        string id,
        string title,
        string owningPerspective,
        ReplaySearchPanel panel,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
