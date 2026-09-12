using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts a replay-scoped <see cref="EventBrowserPanel"/>.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="EventBrowserPanel"/> is a
/// plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its own
/// <see cref="ManagedWindow.Id"/>) and the kind. ⚠ A second host class of the same name exists in
/// <c>Hrot.Presentation.Windows.FdpPanelWindows</c> — both cite <see cref="PanelIds.EventBrowser"/>.</para>
/// </summary>
public sealed class FdpEventBrowserWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND, shared with the Hrot.Presentation host.</summary>
    internal const string Kind = PanelIds.EventBrowser;

    private readonly EventBrowserPanel _panel;

    public FdpEventBrowserWindow(
        string id,
        string title,
        string owningPerspective,
        EventBrowserPanel panel,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ <b>U-obs-5: BUILD · CAPTURE.</b> No ImGui here — published before
    /// <see cref="EventBrowserPanel.DrawContent"/> ever touches ImGui.</summary>
    private EventBrowserPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal EventBrowserPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}
