using System.Numerics;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Shared architecture diagnostics managed window for module/system/translator inspection.
/// </summary>
public sealed class ArchitectureDiagnosticsWindow : ManagedWindow
{
    private readonly ArchitectureDiagnosticsPanel _panel;

    public ArchitectureDiagnosticsWindow(
        string id,
        string title,
        string owningPerspective,
        ArchitectureDiagnosticsPanel panel,
        System.Numerics.Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = false;
        TitleBarColor = titleBarColor;
    }

    protected override void DrawClientArea()
    {
        _panel.DrawContent();
    }
}
