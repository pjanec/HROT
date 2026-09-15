using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Hrot.Presentation.Windows;

/// <summary>
/// FDP Entity Inspector managed window for a specific subsystem perspective.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="EntityInspectorPanel"/> is a
/// plain <c>*Panel</c> with no window identity of its own; this window supplies the address (its own
/// <see cref="ManagedWindow.Id"/>) and the kind. ⚠ A second host class of the same name exists in
/// <c>Fdp.Presentation.Windows.ReplayBrowser</c> — both cite <see cref="PanelIds.EntityInspector"/> so
/// the kind agrees across the two hosts. 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c>
/// group 5.</para>
/// </summary>
public sealed class FdpEntityInspectorWindow : ManagedWindow
{
    private readonly EntityInspectorPanel _panel;
    private readonly Func<RepositoryAdapter?> _adapterGetter;
    private readonly Func<InspectorState>     _stateGetter;

    public FdpEntityInspectorWindow(
        string id,
        string title,
        string owningPerspective,
        EntityInspectorPanel panel,
        Func<RepositoryAdapter?> adapterGetter,
        Func<InspectorState> stateGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel         = panel;
        _adapterGetter = adapterGetter;
        _stateGetter   = stateGetter;
        IsOpen         = true;
        TitleBarColor  = titleBarColor;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private EntityInspectorPanelViewModel? BuildAndPublish(out RepositoryAdapter? adapter, out InspectorState state)
    {
        adapter = _adapterGetter();
        state = default!;
        if (adapter == null) return null;
        state = _stateGetter();
        var vm = _panel.BuildViewModel(adapter, state, Id, PanelIds.EntityInspector);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal EntityInspectorPanelViewModel? SimulateDrawClientArea() => BuildAndPublish(out _, out _);

    protected override void DrawClientArea()
    {
        var vm = BuildAndPublish(out var adapter, out var state);
        if (vm == null) return;
        _panel.DrawContent(adapter!, state);
    }
}

/// <summary>
/// FDP Event Browser managed window for a specific subsystem perspective.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="EventBrowserPanel"/> is a
/// plain <c>*Panel</c> with no window identity of its own. ⚠ A second host class of the same name exists
/// in <c>Fdp.Presentation.Windows.ReplayBrowser</c> — both cite <see cref="PanelIds.EventBrowser"/>.</para>
/// </summary>
public sealed class FdpEventBrowserWindow : ManagedWindow
{
    private readonly EventBrowserPanel _panel;

    public FdpEventBrowserWindow(
        string id,
        string title,
        string owningPerspective,
        EventBrowserPanel panel,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = true;
        TitleBarColor = titleBarColor;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private EventBrowserPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.EventBrowser);
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

/// <summary>
/// Volatile dedicated watch window for a single entity.
/// Spawned on demand from the entity inspector context menu ("Inspect...").
/// Multiple instances may coexist for the same entity (each with its own
/// <see cref="EntityWatchPanel"/> and independent component expand/collapse state).
/// The window is automatically destroyed by the WindowManager when it is closed.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> This is the ONLY production host of
/// <see cref="EntityWatchPanel"/> (measured — the class exists only in doc comments elsewhere), so its
/// kind stays a local literal.</para>
/// </summary>
public sealed class FdpEntityWatchWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal. ⚠ NOT
    /// <see cref="PanelIds.Watch"/> — that is the blueprint pinned-variable watch, a different source
    /// and different columns from this generic ECS component watch.</summary>
    internal const string Kind = "entity-watch";

    private readonly EntityWatchPanel _panel;
    private readonly Func<IInspectableSession?> _sessionGetter;

    public FdpEntityWatchWindow(
        string id,
        string title,
        string owningPerspective,
        EntityWatchPanel panel,
        Func<IInspectableSession?> sessionGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _sessionGetter = sessionGetter;
        IsOpen = true;
        TitleBarColor = titleBarColor;
        IsVolatile = true;
        ShowInMenu = false;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private EntityWatchPanelViewModel? BuildAndPublish(out IInspectableSession? session)
    {
        session = _sessionGetter();
        if (session == null) return null;
        var vm = _panel.BuildViewModel(session, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal EntityWatchPanelViewModel? SimulateDrawClientArea() => BuildAndPublish(out _);

    protected override void DrawClientArea()
    {
        var vm = BuildAndPublish(out var session);
        if (vm == null) return;
        _panel.DrawContent(session!);
    }
}
