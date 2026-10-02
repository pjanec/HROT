using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts <see cref="ReplayTimelinePanel"/>.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="ReplayTimelinePanel"/> is a
/// plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its own
/// <see cref="ManagedWindow.Id"/>) and the kind.</para>
/// </summary>
public sealed class ReplayTimelineWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal.</summary>
    internal const string Kind = "replay-timeline";

    private readonly ReplayTimelinePanel _panel;

    public ReplayTimelineWindow(
        string id,
        string title,
        string owningPerspective,
        ReplayTimelinePanel panel,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private ReplayTimelinePanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ReplayTimelinePanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}
