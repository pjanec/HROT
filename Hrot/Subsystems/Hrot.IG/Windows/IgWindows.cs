using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.IG.UI;

namespace Hrot.IG.Windows;

/// <summary>IG subsystem title-bar colour (dark green).</summary>
internal static class IgWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.07f, 0.30f, 0.07f, 1f);
}

/// <summary>IG Debug Panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class IgDebugWindow : ManagedWindow
{
    internal const string Kind = "ig-debug";

    private readonly IgDebugPanel _panel;

    public IgDebugWindow(IgDebugPanel panel)
        : base("ig_debug", "IG Debug", "IG", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = IgWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private IgDebugPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal IgDebugPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}

/// <summary>IG Entity Properties panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class IgEntityPropertiesWindow : ManagedWindow
{
    internal const string Kind = "ig-entity-properties";

    private readonly EntityInspectorPanel _panel;

    public IgEntityPropertiesWindow(EntityInspectorPanel panel)
        : base("ig_entity_properties", "IG Entity Properties", "IG", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = IgWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private EntityInspectorPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal EntityInspectorPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}

/// <summary>IG Waypoint Editor panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class IgWaypointEditorWindow : ManagedWindow
{
    internal const string Kind = "ig-waypoint-editor";

    private readonly WaypointEditorPanel _panel;

    public IgWaypointEditorWindow(WaypointEditorPanel panel)
        : base("ig_waypoint_editor", "Waypoint Editor", "IG", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = IgWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private WaypointEditorPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal WaypointEditorPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}

/// <summary>IG Mini ExCon (entity spawner) panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class IgMiniExConWindow : ManagedWindow
{
    internal const string Kind = "ig-mini-excon";

    private readonly MiniExConPanel _panel;

    public IgMiniExConWindow(MiniExConPanel panel)
        : base("ig_mini_excon", "Mini ExCon", "IG", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = IgWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private MiniExConPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal MiniExConPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}

/// <summary>IG Performance Overlay as a perspective-bound managed window.</summary>
internal sealed class IgPerformanceWindow : ManagedWindow
{
    private readonly PerformanceOverlay _panel;

    public IgPerformanceWindow(PerformanceOverlay panel)
        : base("ig_performance", "IG Performance", "IG", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = false; // off by default — operator enables when needed
        TitleBarColor = IgWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent();
}
