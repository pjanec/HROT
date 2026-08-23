using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Presentation.Panels.Breakpoints;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Data Breakpoint Manager window. Registered per-perspective
/// (<see cref="WindowScope.PerspectiveBound"/>) so each AI/CGF subsystem
/// has its own isolated manager UI.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="DataBreakpointManagerPanel"/>
/// is a plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its
/// own <see cref="ManagedWindow.Id"/>) and the kind.</para>
/// </summary>
public sealed class DataBreakpointManagerWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Per-perspective windows share this literal.</summary>
    internal const string Kind = "data-breakpoint-manager";

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

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private DataBreakpointManagerPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal DataBreakpointManagerPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}
