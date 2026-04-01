using FDP.Toolkit.ImGui.WindowManager;
using Hrot.ClusterRunner.Services;

namespace Hrot.ClusterRunner.Windows;

/// <summary>
/// Managed window for the ExCon cluster control panel.
/// Global scope — ExCon has no map perspective so it is always visible.
/// </summary>
internal sealed class ClusterControlWindow : ManagedWindow
{
    private readonly ClusterScenarioPanel? _panel;
    private readonly ClusterUiCache?       _cache;

    public ClusterControlWindow(ClusterScenarioPanel? panel, ClusterUiCache? cache)
        : base("excon_cluster_control", "Cluster Control", string.Empty, WindowScope.Global)
    {
        _panel = panel;
        _cache = cache;
        IsOpen = true;
    }

    protected override void DrawClientArea()
    {
        _panel?.Render();
    }
}
