using System;
using System.Numerics;
using Fdp.ModuleHost;
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
    private readonly Func<ModuleHostKernel?> _kernelGetter;

    public ArchitectureDiagnosticsWindow(
        string id,
        string title,
        string owningPerspective,
        ArchitectureDiagnosticsPanel panel,
        Func<ModuleHostKernel?> kernelGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _kernelGetter = kernelGetter;
        IsOpen = false;
        TitleBarColor = titleBarColor;
    }

    protected override void DrawClientArea()
    {
        var kernel = _kernelGetter();
        if (kernel == null)
        {
            ImGuiApi.TextUnformatted("Kernel not available.");
            return;
        }

        _panel.DrawContent(kernel);
    }
}
