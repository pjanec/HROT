using System.Numerics;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts <see cref="ComponentDiffPanel"/>.
/// </summary>
public sealed class ComponentDiffWindow : ManagedWindow
{
    private readonly ComponentDiffPanel _panel;

    public ComponentDiffWindow(
        string id,
        string title,
        string owningPerspective,
        ComponentDiffPanel panel,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
