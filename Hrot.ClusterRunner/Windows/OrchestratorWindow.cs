using System.Numerics;
using FDP.Toolkit.ImGui.WindowManager;
using Hrot.ClusterRunner.Services;

namespace Hrot.ClusterRunner.Windows;

/// <summary>
/// Managed window for the Orchestrator scenario/cluster control panel.
/// Always visible (Global scope) since Orchestrator has no map perspective.
/// </summary>
internal sealed class OrchestratorWindow : ManagedWindow
{
    private readonly ClusterScenarioPanel _panel;

    public OrchestratorWindow(ClusterScenarioPanel panel)
        : base("orchestrator_main", "Orchestrator", string.Empty, WindowScope.Global)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = new Vector4(0.30f, 0.16f, 0.04f, 1f);  // Orchestrator brown
    }

    protected override void DrawClientArea() => _panel.Render();
}
