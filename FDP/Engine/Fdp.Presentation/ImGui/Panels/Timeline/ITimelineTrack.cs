using System;
using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>What a track item is: a point in time (a camera key) or a stretch of it (a time segment).</summary>
public enum TimelineTrackItemKind : byte { Key, Segment }

/// <summary>
/// ⭐ <c>CE-1364</c> T2 (docs/DESIGN_Director_Timeline_Editor.md §10) — one editable item of a track. Times are SIM seconds (§2c row 10): the panel
/// places them on the active axis through the session's time map, the track never sees a tick.
/// </summary>
/// <param name="To">The end of a segment, else null (a key).</param>
public sealed record TimelineTrackItem(string Id, double From, double? To, string Label, TimelineTrackItemKind Kind, bool Selected);

/// <summary>
/// ⭐ <c>CE-1364</c> T2 (§10, T-L1) — the seam between the timeline panel and an EDITABLE row (the director's Time / Camera tracks). ⛔ Not a lane
/// provider: lanes are READ from an index, a track's items are EDITED — two lifetimes. The panel draws the rows, hit-tests, selects and drags;
/// it knows nothing about takes. An edit is the track's business (undo, saving): the panel only says WHAT moved WHERE.
/// </summary>
public interface ITimelineTrack
{
    string Id { get; }
    string Label { get; }

    /// <summary>The items now (SIM seconds); read every frame.</summary>
    IReadOnlyList<TimelineTrackItem> Items { get; }

    /// <summary>A left click on an item. ⛔ Never moves anything and never seeks.</summary>
    void Select(string itemId);

    /// <summary>Moves <paramref name="itemId"/> to <paramref name="toSimSeconds"/> (a segment's start; it keeps its length). While dragging the panel calls
    /// this every frame with <paramref name="commit"/> false (a preview — not an undo step); on release ONCE with true.</summary>
    void Move(string itemId, double toSimSeconds, bool commit);

    /// <summary>A key (<c>K</c> / <c>J</c> / <c>L</c>) pressed while the panel is focused and this track holds the selected item; the playhead is at
    /// <paramref name="atSimSeconds"/>. True when the track took it.</summary>
    bool Key(char key, double atSimSeconds);

    /// <summary>The track's right-click items for <paramref name="itemId"/> (null = empty track space).</summary>
    IEnumerable<TimelineMenuItem> MenuItems(string? itemId);
}
