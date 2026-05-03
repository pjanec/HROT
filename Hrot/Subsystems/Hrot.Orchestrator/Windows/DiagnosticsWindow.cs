using Fdp.Presentation.WindowManager;
using Hrot.Orchestrator.Panels;

namespace Hrot.Orchestrator.Windows;

/// <summary>
/// Managed window that hosts the <see cref="ClusterDiagnosticsPanel"/>.
/// Always visible (Global scope) — the Orchestrator and ExCon have no map perspective.
/// </summary>
public sealed class DiagnosticsWindow : ManagedWindow
{
    private readonly ClusterDiagnosticsPanel _panel;

    public DiagnosticsWindow(ClusterDiagnosticsPanel panel)
        : base("orchestrator_diagnostics", "Diagnostics", string.Empty, WindowScope.Global)
    {
        _panel = panel;
        IsOpen = true;
    }

    protected override void DrawClientArea() => _panel.Render();
}
