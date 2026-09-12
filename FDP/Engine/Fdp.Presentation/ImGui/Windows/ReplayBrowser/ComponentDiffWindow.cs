using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts <see cref="ComponentDiffPanel"/>.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="ComponentDiffPanel"/> is a
/// plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its own
/// <see cref="ManagedWindow.Id"/>) and the kind.</para>
/// </summary>
public sealed class ComponentDiffWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal.</summary>
    internal const string Kind = "component-diff";

    private readonly ComponentDiffPanel _panel;

    public ComponentDiffWindow(
        string id,
        string title,
        string owningPerspective,
        ComponentDiffPanel panel,
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
    private ComponentDiffPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ComponentDiffPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}
