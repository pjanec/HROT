using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Presentation.Windows.Timeline;

/// <summary>
/// ⭐ <c>CE-1302</c> R1-c (docs/DESIGN_Director_Timeline_Editor.md §9) — the window that hosts <see cref="TimelinePanel"/>. The host hands
/// it two things it owns: where the index comes from and how time moves (<see cref="ITimelineCursor"/>, TL-2). U-obs-5: it declares
/// itself and publishes what it shows, so an agent reads the timeline through <c>GET /panels/{id}</c>.
/// </summary>
public sealed class EventTimelineWindow : ManagedWindow
{
    /// <summary>⭐ U-obs-5 — THE KIND.</summary>
    public const string Kind = "event-timeline";

    private readonly TimelinePanel _panel;
    private readonly Func<ITimelineSource?> _source;
    private readonly ITimelineCursor _cursor;
    private readonly Func<IReadOnlyList<TimelineLaneDescriptor>> _lanes;

    /// <param name="lanes">The registered lanes to show — the host's <c>TimelineLaneCatalog.Lanes(context)</c> (R-4079).</param>
    public EventTimelineWindow(string id, string title, string owningPerspective, TimelinePanel panel, Func<ITimelineSource?> source,
        ITimelineCursor cursor, Func<IReadOnlyList<TimelineLaneDescriptor>> lanes, Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _source = source;
        _cursor = cursor;
        _lanes = lanes;
        TitleBarColor = titleBarColor;
        IsOpen = true;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private TimelinePanelViewModel BuildAndPublish()
    {
        var vm = TimelinePanelViewModel.Build(Id, Kind, _source(), _cursor, _panel.Window, _lanes());
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, with no live ImGui context.</summary>
    internal TimelinePanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.Draw(_source(), _cursor, _lanes());
    }
}
