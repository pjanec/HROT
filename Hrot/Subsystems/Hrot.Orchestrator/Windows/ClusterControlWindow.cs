using System.Numerics;
using Fdp.Toolkit.ImGui.WindowManager;
using Hrot.Orchestrator.Panels;

namespace Hrot.Orchestrator.Windows;

/// <summary>
/// Managed window for the ExCon cluster control panel.
/// Global scope — ExCon has no map perspective so it is always visible.
/// </summary>
public sealed class ClusterControlWindow : ManagedWindow
{
    private readonly ClusterScenarioPanel? _panel;
    private readonly ClusterUiCache?       _cache;

    public ClusterControlWindow(ClusterScenarioPanel? panel, ClusterUiCache? cache)
        : base("excon_cluster_control", "Cluster Control", string.Empty, WindowScope.Global)
    {
        _panel = panel;
        _cache = cache;
        IsOpen = true;
        TitleBarColor = new Vector4(0.32f, 0.08f, 0.48f, 1f);  // ExCon violet
    }

    protected override void DrawClientArea()
    {
        _panel?.Render();
    }
}
