using System.Numerics;
using Fdp.Presentation.WindowManager;
using Hrot.Presentation.Panels.Breakpoints;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Data Breakpoint Manager window. Registered per-perspective
/// (<see cref="WindowScope.PerspectiveBound"/>) so each AI/CGF subsystem
/// has its own isolated manager UI.
/// </summary>
public sealed class DataBreakpointManagerWindow : ManagedWindow
{
    private readonly DataBreakpointManagerPanel _panel;

    public DataBreakpointManagerWindow(
        string id,
        string owningPerspective,
        DataBreakpointManagerPanel panel,
        Vector4? titleBarColor = null)
        : base(id, "Data Breakpoints", owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = false;
        TitleBarColor = titleBarColor;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
