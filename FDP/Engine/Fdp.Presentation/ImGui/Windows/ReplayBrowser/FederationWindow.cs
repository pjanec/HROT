using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts <see cref="FederationPanel"/>.
/// 📄 <c>docs/designs/replay-browser-frankenstein/DESIGN.md</c> §8 — the mode toggle, per-node
/// offset rows, and local-entities provider dropdown for Merged View.
///
/// <para>⭐⭐⭐ <b>U-obs-5 follow-up — the HOST registers, not the panel.</b> <see cref="FederationPanel"/>
/// is a plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its
/// own <see cref="ManagedWindow.Id"/>) and the kind.</para>
///
/// <para>⚠⚠ <b>The panel is created LAZILY.</b> <c>ReplayBrowserSubsystem</c> has no
/// <see cref="FederationPanel"/> until the operator loads a replay group, and REPLACES it (a fresh
/// <c>FederatedReplayManager</c>) on every subsequent group load. <c>ReplayBrowserSubsystem</c>
/// re-registers this window under the same id each time the panel is (re)created —
/// <c>WindowManager.RegisterWindow</c>'s dictionary-indexer semantics replace the prior entry, so the
/// window always reflects the CURRENT panel/manager rather than a stale one.</para>
/// </summary>
public sealed class FederationWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal.</summary>
    internal const string Kind = "replay-federation";

    private readonly FederationPanel _panel;

    public FederationWindow(
        string id,
        string title,
        string owningPerspective,
        FederationPanel panel,
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
    private FederationPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal FederationPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}
