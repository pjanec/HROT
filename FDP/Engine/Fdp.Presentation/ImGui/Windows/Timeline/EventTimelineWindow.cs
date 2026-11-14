using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using ImGuiNET;

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
    private readonly ITimelineCorner? _corner;

    /// <param name="lanes">The registered lanes to show — the host's <c>TimelineLaneCatalog.Lanes(context)</c> (R-4079).</param>
    /// <param name="onSelect">⭐ <c>CE-3330</c> (R-4087 addendum 5) — a left click on an event: the host requests the ONE selection.</param>
    /// <param name="rowMenu">⭐ <c>CE-3330</c> (addendum 4) — the row's own menu items: the host's <c>TimelineLaneCatalog.MenuItems</c>.</param>
    /// <param name="corner">⭐ CE-4325 (E7) — the two icons at the title bar's top right: ⚑ (mark / add a note) and S / W (the axis). Null = none (a host with neither).</param>
    public EventTimelineWindow(string id, string title, string owningPerspective, TimelinePanel panel, Func<ITimelineSource?> source,
        ITimelineCursor cursor, Func<IReadOnlyList<TimelineLaneDescriptor>> lanes, Vector4 titleBarColor,
        Action<TimelineEvent>? onSelect = null, Func<TimelineMenuContext, IReadOnlyList<TimelineMenuItem>>? rowMenu = null,
        ITimelineCorner? corner = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        if (onSelect != null) panel.OnSelect = onSelect;
        if (rowMenu != null) panel.RowMenu = rowMenu;
        _source = source;
        _cursor = cursor;
        _lanes = lanes;
        _corner = corner;
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

    /// <summary>⭐ CE-4290 — the first open at a usable size. It opened AUTO-SIZED to its content (~90 × 50 px on a fresh layout), which clips every lane
    /// row: the lanes "looked empty" and the State group was "not among the drawn groups" while the index held the events (Replay Browser UX B3). The Rules
    /// window had the same cause (CE-4216). <see cref="ImGuiCond.FirstUseEver"/>: a size the person dragged is kept.</summary>
    public static readonly System.Numerics.Vector2 FirstSize = new(1100f, 320f);

    protected override void BeforeBegin() => ImGui.SetNextWindowSize(FirstSize, ImGuiCond.FirstUseEver);

    /// <summary>⭐ CE-4325 — where the corner's two icons were drawn last frame (screen pixels; null = not drawn): the ⚑ and the S / W — for the frame rails that press them.</summary>
    public (Vector2 Mark, Vector2 Axis)? LastCornerAt { get; private set; }

    /// <summary>⭐ CE-4325 — the ⚑ and S / W in the title bar, right to left: S / W nearest the pin, ⚑ beside it (the mockup's order: ⚑ then S).</summary>
    protected override void DrawTitleBarCorner(float rightEdgeX, float titleBarY, float size)
    {
        LastCornerAt = null;
        if (_corner == null) return;
        float x = rightEdgeX;
        Vector2 axisAt = default, markAt = default;
        if (_corner.HasAxis)
        {
            if (TitleBarIcon("##tl_axis", _corner.AxisFace, ref x, titleBarY, size, _corner.AxisEnabled, filled: false, colour: 0xFFE0E0E0u, tooltip: _corner.AxisTip)) _corner.OnToggleAxis();
            axisAt = new Vector2(x + size * 0.5f, titleBarY + size * 0.5f);
        }
        if (_corner.HasMark)
        {
            bool enabled = _corner.MarkDisabledReason == null;
            if (TitleBarIcon("##tl_mark", Fdp.Presentation.Fonts.IconsFontAwesome6.Flag, ref x, titleBarY, size, enabled, filled: false, colour: 0xFFF0C060u,
                    tooltip: _corner.MarkTip)) _corner.OnMark();
            markAt = new Vector2(x + size * 0.5f, titleBarY + size * 0.5f);
        }
        LastCornerAt = (markAt, axisAt);
    }

    /// <summary>The window's size at the last draw (rails).</summary>
    public System.Numerics.Vector2 LastWindowSize { get; private set; }

    protected override void DrawClientArea()
    {
        LastWindowSize = ImGui.GetWindowSize();
        BuildAndPublish();
        _panel.Draw(_source(), _cursor, _lanes());
    }
}
