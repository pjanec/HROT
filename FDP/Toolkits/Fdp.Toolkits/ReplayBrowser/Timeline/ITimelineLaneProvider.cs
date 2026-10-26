using System;
using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>How a lane's single events are drawn when there is room for glyphs.</summary>
    public enum TimelineGlyph : byte { Dot = 0, Diamond = 1, Bar = 2 }

    /// <summary>
    /// ⭐ One lane of the shared timeline. <c>R-4078</c> (docs/DESIGN_Director_Timeline_Editor.md §9c) — moved from <c>Hrot.Editor.AiShared</c>
    /// (it was <c>TraceLaneDescriptor</c>, renamed by Roslyn) and generalised: the group it collapses into, its priority in a cluster (§2b
    /// "priority on top"), its colour (0 = the panel picks one) and its glyph are DATA, so no lane is hard-coded anywhere (R-4079).
    /// </summary>
    public sealed record TimelineLaneDescriptor(
        string Id,
        string DisplayName,
        TraceLevel SupportedLevels = TraceLevel.All,
        string Group = "",
        int Priority = 0,
        uint Color = 0,
        TimelineGlyph Glyph = TimelineGlyph.Dot);

    /// <summary>
    /// ⭐ Where a timeline is being shown, for <see cref="ITimelineLaneProvider.Applies"/>: the asset being edited (a trace lane's key —
    /// it replaced the provider's <c>AssetKind Kind</c>), and whether a recording is open (an indexed lane's).
    /// </summary>
    public readonly record struct TimelineLaneContext(string? AssetKind = null, bool Recording = false);

    /// <summary>
    /// ⭐ An INDEXED lane (R-4079): a descriptor, the condition that makes an event (any <see cref="SearchPredicateDto"/> — the one condition
    /// model, R-4077), and which fields of a match to keep. The recording's index evaluates every registered one in its one pass.
    /// </summary>
    public sealed record IndexedLaneDefinition(TimelineLaneDescriptor Lane, SearchPredicateDto When, ResultProjectionDto? Payload = null);

    /// <summary>
    /// ⭐ THE lane seam (R-4078, §9c) — moved from <c>Hrot.Editor.AiShared</c> (it was <c>ITraceLaneProvider</c>, renamed by Roslyn). The module
    /// that owns the data registers its provider: the trace editors their trace lanes, the combat / behaviour / replication toolkits the
    /// recording's lanes. A provider's lanes come either from the recording's index (<see cref="Indexed"/>) or from the provider itself for a
    /// range (<see cref="Marks"/> — a live trace ring, the director's tracks).
    /// </summary>
    public interface ITimelineLaneProvider
    {
        IReadOnlyList<TimelineLaneDescriptor> Lanes { get; }

        /// <summary>Whether these lanes belong where the timeline is shown.</summary>
        bool Applies(TimelineLaneContext context);

        /// <summary>The lanes the recording's index builds — each one a definition, never code.</summary>
        IReadOnlyList<IndexedLaneDefinition> Indexed => Array.Empty<IndexedLaneDefinition>();

        /// <summary>Marks the provider draws itself for frames [<paramref name="fromFrame"/>, <paramref name="toFrame"/>) of a lane it owns.</summary>
        void Marks(string laneId, int fromFrame, int toFrame, List<TimelineEvent> into) { }

        /// <summary>⭐ <c>CE-3330</c> (R-4087 addendum 4; DESIGN_Director_Timeline_Editor.md §2c rule 5) — what the row under the mouse adds to
        /// the timeline's right-click menu (<i>Set breakpoint here</i>, <i>Add key here</i>…), after the mode's own seek items. ⛔ The panel never
        /// switches on a lane's kind: a lane's menu is its provider's.</summary>
        IEnumerable<TimelineMenuItem> MenuItems(TimelineMenuContext context) => Array.Empty<TimelineMenuItem>();
    }

    /// <summary>⭐ <c>CE-3330</c> — one item of the timeline's right-click menu; <see cref="Run"/> is called when it is chosen.</summary>
    public sealed record TimelineMenuItem(string Label, Action Run, bool Enabled = true);

    /// <summary>⭐ <c>CE-3330</c> — where the right click was: the lane (null over the ruler / empty space), the time and frame under the mouse
    /// (snapped to a frame), and the event there, if any.</summary>
    public sealed record TimelineMenuContext(string? LaneId, long Ticks, int Frame, TimelineEvent? Event);
}
