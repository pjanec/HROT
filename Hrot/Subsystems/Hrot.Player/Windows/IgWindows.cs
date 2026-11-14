using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Player.UI;

namespace Hrot.Player.Windows;

/// <summary>IG subsystem title-bar colour (dark green).</summary>
internal static class PlayerWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.07f, 0.30f, 0.07f, 1f);
}

/// <summary>IG Debug Panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class PlayerDebugWindow : ManagedWindow
{
    internal const string Kind = "player-debug";

    private readonly PlayerDebugPanel _panel;

    public PlayerDebugWindow(PlayerDebugPanel panel)
        : base("player_debug", "Player Debug", "Player", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = PlayerWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private PlayerDebugPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal PlayerDebugPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}

// ⭐ CE-1096 (ROUTES1 §11.4 W-1) — the waypoint editor's window is the SHARED Hrot.Presentation.Windows.WaypointEditorWindow now
//   (IgSubsystem registers it under the IG's historical id); IgWaypointEditorWindow is gone.


/// <summary>IG Performance Overlay as a perspective-bound managed window.</summary>
internal sealed class PlayerPerformanceWindow : ManagedWindow
{
    private readonly PerformanceOverlay _panel;

    public PlayerPerformanceWindow(PerformanceOverlay panel)
        : base("player_performance", "Player Performance", "Player", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = false; // off by default — operator enables when needed
        TitleBarColor = PlayerWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
