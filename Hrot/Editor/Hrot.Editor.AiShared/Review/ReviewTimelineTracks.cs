using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.Editor.AiShared.Review;

/// <summary>⭐ R-6 / V7 — one data-breakpoint stop of this session: when it stopped (the shown world's wall ticks, <c>GlobalTime.TotalWallTicks</c> — what the timeline index stamps its frames with), what said so, and the unit (0 = none / no identity).</summary>
public readonly record struct ReviewStop(long WallTicks, string Label, int NetworkId);

/// <summary>⭐ R-6 / V7 — the stops of this session, appended when the breakpoint manager fires (rare: a stop pauses the run). <see cref="Version"/> moves on every change so the row rebuilds only then.</summary>
public sealed class ReviewStopLog
{
    private readonly List<ReviewStop> _stops = new();
    private int _version;

    public IReadOnlyList<ReviewStop> Stops => _stops;
    public int Version => _version;

    public void Add(long wallTicks, string label, int networkId) { _stops.Add(new ReviewStop(wallTicks, label, networkId)); _version++; }
    public void Clear() { if (_stops.Count == 0) return; _stops.Clear(); _version++; }
}

/// <summary>
/// ⭐ R-6 / V7 (docs/UX/DESIGN_Review_Perspective.md §3 D5, §4 V7) — the Event Timeline's two REVIEW rows, composed by BOTH hosts through <c>TimelinePanel.AddTracks</c> (ONE class):
/// <b>Search hits</b> — a marker per hit of the Replay search panel, at its frame — and <b>Breakpoint stops</b> — a marker per stop this session. ⛔ Not lanes: hits are a QUERY RESULT and stops are session
/// events, not recorded events — so they are <see cref="ITimelineTrack"/> rows (the Tasks rows' precedent, <c>TasksTimelineTracks</c>), not an <c>IndexedLaneDefinition</c>.
/// ⭐ A click on a marker seeks to its frame and selects its unit: a hit runs the panel's own row-click path (<see cref="ReplaySearchPanel.ActivateHit"/> — the host's seek + select, the very code the grid's
/// button runs), a stop seeks through the transport seam (<see cref="IReplayTransport"/>, a history waypoint like the seek bar's jump) and selects its unit. ⭐ Rebuilt only when a search lands, a stop is
/// added or the index changes — the panel reads <see cref="Tracks"/> every frame, 0 B steady; a row with no markers is not in the list (hidden when empty).
/// </summary>
public sealed class ReviewTimelineTracks
{
    public const string HitsId = "review:hits", StopsId = "review:stops";
    /// <summary>The most hit markers drawn (a search may return a million rows); the row's status says <i>"first N of M"</i>.</summary>
    public const int MaxMarkers = 5000;

    // ABGR: a hit blue (the search's), a stop red (a breakpoint's)
    public const uint HitColour = 0xFFE0B04A, StopColour = 0xFF4A4AE6;

    private readonly Func<ITimelineSource?> _source;
    private readonly ReplaySearchPanel? _search;
    private readonly ReviewStopLog? _stops;
    private readonly Func<IReplayTransport?> _transport;
    private readonly Action<int>? _selectUnit;
    private readonly ReviewRow _hitsRow, _stopsRow;
    private readonly List<ITimelineTrack> _rows = new();
    private readonly List<int> _stopFrames = new();
    private TimelineIndex? _builtFor;
    private int _builtHits = -1, _builtStops = -1, _builtScanned = -1;

    /// <param name="source">The shown timeline (the same func the Event Timeline window reads).</param>
    /// <param name="search">The host's Replay search panel, or null (no hits row).</param>
    /// <param name="stops">The session's stops, or null (a host with no breakpoint manager — the Replay Browser — has no stops row).</param>
    /// <param name="transport">THE seek (the AAR transport seam) a stop's click goes through; null / no recording ⇒ the click only selects.</param>
    /// <param name="selectUnit">The host's ONE selection by network id.</param>
    public ReviewTimelineTracks(Func<ITimelineSource?> source, ReplaySearchPanel? search, ReviewStopLog? stops, Func<IReplayTransport?> transport, Action<int>? selectUnit)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _search = search; _stops = stops; _transport = transport ?? (() => null); _selectUnit = selectUnit;
        _hitsRow = new ReviewRow(HitsId, "Search hits", this, isHits: true);
        _stopsRow = new ReviewRow(StopsId, "Breakpoint stops", this, isHits: false);
    }

    /// <summary>The rows now (⭐ composed with <c>TimelinePanel.AddTracks</c>): Search hits and/or Breakpoint stops — only those that have markers.</summary>
    public IReadOnlyList<ITimelineTrack> Tracks()
    {
        var index = _source()?.Index;
        int hits = _search?.ResultsVersion ?? 0, stops = _stops?.Version ?? 0;
        int scanned = index is { IsComplete: false } ? index.ScannedFrames : -2;   // a recording still being scanned has partial sim times: follow it until complete
        if (!ReferenceEquals(index, _builtFor) || hits != _builtHits || stops != _builtStops || scanned != _builtScanned) Rebuild(index, hits, stops, scanned);
        return _rows;
    }

    /// <summary>A click on hit <paramref name="i"/>'s marker (rails drive it; the row calls it).</summary>
    public void ClickHit(int i) => _search?.ActivateHit(i);

    /// <summary>A click on stop <paramref name="i"/>'s marker: seek to its frame through the transport seam (a waypoint) and select its unit.</summary>
    public void ClickStop(int i)
    {
        if (_stops == null || (uint)i >= (uint)_stops.Stops.Count || i >= _stopFrames.Count) return;
        int frame = _stopFrames[i];
        if (_transport() is { HasRecording: true } t)
        {
            int cur = t.CurrentFrame;
            if (cur >= 0 && cur != frame) { t.Waypoints.PushWaypoint(cur, Entity.Null); t.Waypoints.PushWaypoint(frame, Entity.Null); }
            t.Seek(frame);
        }
        int unit = _stops.Stops[i].NetworkId;
        if (unit != 0) _selectUnit?.Invoke(unit);
    }

    /// <summary>The hit markers as last built (rails).</summary>
    public IReadOnlyList<TimelineTrackItem> HitItems => _hitsRow.Items;
    /// <summary>The stop markers as last built (rails).</summary>
    public IReadOnlyList<TimelineTrackItem> StopItems => _stopsRow.Items;
    /// <summary>The frame each stop marker sits at (rails).</summary>
    public IReadOnlyList<int> StopFrames => _stopFrames;

    private void Rebuild(TimelineIndex? index, int hits, int stops, int scanned)
    {
        _builtFor = index; _builtHits = hits; _builtStops = stops; _builtScanned = scanned;
        _rows.Clear(); _hitsRow.Clear(); _stopsRow.Clear(); _stopFrames.Clear();
        if (index == null || index.FrameTicks.Count == 0) return;

        if (_search != null)
        {
            var list = _search.Hits;
            int n = Math.Min(list.Count, MaxMarkers);
            for (int i = 0; i < n; i++)
            {
                var h = list[i];
                _hitsRow.Add(new TimelineTrackItem("h" + i, SimOf(index, h.Frame), null, $"{h.Entity} · {h.Context}", TimelineTrackItemKind.Key, false, HitColour, TimelineTrackItemStyle.Pinned));
            }
            if (n > 0)
            {
                _hitsRow.Head("Search hits", list.Count > n ? $"first {n} of {list.Count}" : n.ToString());
                _rows.Add(_hitsRow);
            }
        }
        if (_stops != null && _stops.Stops.Count > 0)
        {
            var list = _stops.Stops;
            for (int i = 0; i < list.Count; i++)
            {
                int frame = index.FrameAt(list[i].WallTicks);
                _stopFrames.Add(frame);
                _stopsRow.Add(new TimelineTrackItem("s" + i, SimOf(index, frame), null, list[i].Label, TimelineTrackItemKind.Key, false, StopColour, TimelineTrackItemStyle.Pinned));
            }
            _stopsRow.Head("Breakpoint stops", list.Count.ToString());
            _rows.Add(_stopsRow);
        }
    }

    private static double SimOf(TimelineIndex index, int frame)
    {
        var sim = index.FrameSim;
        if (sim.Count > 0) return sim[Math.Clamp(frame, 0, sim.Count - 1)];
        var t = index.FrameTicks;
        return t.Count == 0 ? 0 : (t[Math.Clamp(frame, 0, t.Count - 1)] - t[0]) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>One review row: read-only (⛔ a past hit or stop is not edited); a click on a marker is the owner's click.</summary>
    private sealed class ReviewRow : ITimelineTrack
    {
        private readonly ReviewTimelineTracks _owner;
        private readonly bool _isHits;
        private readonly List<TimelineTrackItem> _items = new();

        public ReviewRow(string id, string label, ReviewTimelineTracks owner, bool isHits)
        { Id = id; Label = label; _owner = owner; _isHits = isHits; Header = new TrackHeader(0, label); }

        public string Id { get; }
        public string Label { get; }
        public TrackHeader? Header { get; private set; }
        public IReadOnlyList<TimelineTrackItem> Items => _items;

        public void Clear() => _items.Clear();
        public void Add(TimelineTrackItem item) => _items.Add(item);
        public void Head(string text, string status) => Header = new TrackHeader(0, text, status);

        public void Select(string itemId)
        {
            if (itemId.Length < 2 || !int.TryParse(itemId.AsSpan(1), out int i)) return;
            if (_isHits) _owner.ClickHit(i); else _owner.ClickStop(i);
        }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<TimelineMenuItem>();
    }
}
