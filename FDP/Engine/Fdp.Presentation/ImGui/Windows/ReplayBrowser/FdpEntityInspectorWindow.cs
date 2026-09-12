using System;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// PerspectiveBound window that hosts a replay-scoped <see cref="EntityInspectorPanel"/>.
/// Uses factory delegates to obtain the session and inspector state on each frame so that
/// the panel always reflects the current sandbox repository state.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="EntityInspectorPanel"/> is a
/// plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its own
/// <see cref="ManagedWindow.Id"/>) and the kind. ⚠ A second host class of the same name exists in
/// <c>Hrot.Presentation.Windows.FdpPanelWindows</c>, hosting the same panel for the Hrot perspectives —
/// both cite <see cref="PanelIds.EntityInspector"/> so the kind agrees across the two hosts.</para>
/// </summary>
public sealed class FdpEntityInspectorWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND, shared with the Hrot.Presentation host.</summary>
    internal const string Kind = PanelIds.EntityInspector;

    private readonly EntityInspectorPanel _panel;
    private readonly Func<IInspectableSession?> _sessionFactory;
    private readonly Func<InspectorState> _stateFactory;

    public FdpEntityInspectorWindow(
        string id,
        string title,
        string owningPerspective,
        EntityInspectorPanel panel,
        Func<IInspectableSession?> sessionFactory,
        Func<InspectorState> stateFactory,
        Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _sessionFactory = sessionFactory;
        _stateFactory = stateFactory;
        TitleBarColor = titleBarColor;
        IsOpen = true;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>U-obs-5: BUILD · CAPTURE.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
    /// ⛔⛔ No ImGui here — published before <see cref="EntityInspectorPanel.DrawContent"/> ever touches
    /// ImGui, so a headless run still observes the panel. Returns <c>null</c> when the session is not
    /// available this frame (mirrors the render guard in <see cref="DrawClientArea"/>).
    /// </summary>
    private EntityInspectorPanelViewModel? BuildAndPublish(out IInspectableSession? session, out InspectorState state)
    {
        session = _sessionFactory();
        state = default!;
        if (session == null) return null;
        state = _stateFactory();
        var vm = _panel.BuildViewModel(session, state, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal EntityInspectorPanelViewModel? SimulateDrawClientArea() => BuildAndPublish(out _, out _);

    protected override void DrawClientArea()
    {
        var vm = BuildAndPublish(out var session, out var state);
        if (vm == null) return;
        _panel.DrawContent(session!, state);
    }
}
