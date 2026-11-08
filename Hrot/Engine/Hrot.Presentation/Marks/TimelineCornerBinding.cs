using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Marks;

/// <summary>
/// ⭐ CE-4325 (E7, R-4177 Q3) — the Event Timeline's title-bar corner bound to the host: <b>⚑</b> = <see cref="MarksController.Mark"/> (a mark now in Live · <i>Add note here</i> in a DAR / AAR) and <b>S / W</b> = the session's
/// axis (the toolbar's toggle, MOVED here — one rule, <c>MainToolbarTimeControlSection.ToggleAxis</c>). Every string it hands the window is cached: the window reads them every frame (0 B steady).
/// </summary>
public sealed class TimelineCornerBinding : ITimelineCorner
{
    private readonly MarksController _marks;
    private readonly ITimeTransportFacade _facade;
    private string? _tipFor, _reasonFor; private string _tip = "";

    public TimelineCornerBinding(MarksController marks, ITimeTransportFacade facade)
    {
        _marks = marks ?? throw new System.ArgumentNullException(nameof(marks));
        _facade = facade ?? throw new System.ArgumentNullException(nameof(facade));
    }

    public bool HasMark => _marks.Active;

    public string? MarkDisabledReason => _marks.MarkDisabledReason;

    public string MarkTip
    {
        get
        {
            string tip = _marks.MarkTip;
            string? why = _marks.MarkDisabledReason;
            if (!ReferenceEquals(tip, _tipFor) || !ReferenceEquals(why, _reasonFor) || _tipFor == null) { _tip = why == null ? tip : tip + " — " + why; _tipFor = tip; _reasonFor = why; }
            return _tip;
        }
    }

    public void OnMark() => _marks.Mark();

    /// <summary>Always shown (as the toolbar's was); greyed outside a session.</summary>
    public bool HasAxis => true;

    public string AxisFace => Hrot.UI.Common.Panels.MainToolbarTimeControlSection.AxisFace(_facade.Axis);

    public string AxisTip => Hrot.UI.Common.Panels.MainToolbarTimeControlSection.AxisTooltip(_facade.Axis);

    public bool AxisEnabled => Hrot.UI.Common.Panels.MainToolbarTimeControlSection.AxisEnabledIn(_facade.Mode);

    public void OnToggleAxis() => Hrot.UI.Common.Panels.MainToolbarTimeControlSection.ToggleAxis(_facade);
}
