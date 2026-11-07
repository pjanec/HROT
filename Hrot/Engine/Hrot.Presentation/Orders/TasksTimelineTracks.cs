using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ W-1 / P-8 8c-2 (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8 E4, §5.12; mockup <c>mission-plan-detail-visual.svg</c> C) — the Event Timeline's
/// <b>Tasks</b> rows over the ONE Tasks lane (<see cref="TasksLane"/>): <b>all units</b> — a marker per task start (blue) / Done (green) / Failed
/// (red), each naming its unit and task — and, for the SELECTED unit, its tasks as BARS (start → end, coloured by how they ended). ⭐ A thousand
/// units stay ONE row. Items are rebuilt only when the index grows or the selection changes (the panel reads them every frame — R-2084).
/// </summary>
public sealed class TasksTimelineTracks
{
    public const string GroupId = "tasks";

    // the mockup's colours (ABGR for ImGui): started blue, Done green, Failed red, a running bar amber
    public const uint StartedColour = 0xFFFFA95A, DoneColour = 0xFF6AD05B, FailedColour = 0xFF5555E6, RunningColour = 0xFF33C0F2;

    private readonly Func<ITimelineSource?> _source;
    private readonly Func<EntityRepository?> _world;
    private readonly Func<int> _selectedNetId;
    private readonly Action<int>? _selectUnit;
    private readonly List<TaskMarker> _markers = new();
    private readonly List<TaskBar> _bars = new();
    private readonly TasksTrack _header, _all, _selection;
    private readonly List<ITimelineTrack> _rows = new();
    private TimelineIndex? _builtFor;
    private int _builtFrames = -1, _builtSelection = -1;
    private readonly Dictionary<int, string> _names = new();

    /// <param name="selectedNetId">The host's ONE selection, as a network id (0 = none): its tasks are drawn as bars.</param>
    /// <param name="selectUnit">A click on a marker selects its unit (the host's ONE selection by network id); null = the marker is only shown.</param>
    public TasksTimelineTracks(Func<ITimelineSource?> source, Func<EntityRepository?> world, Func<int> selectedNetId, Action<int>? selectUnit = null)
    {
        _source = source; _world = world; _selectedNetId = selectedNetId; _selectUnit = selectUnit;
        _header = new TasksTrack(GroupId, "Tasks", new TrackHeader(0, "Tasks", null, null, null, Collapsible: true), this);
        _all = new TasksTrack(GroupId + ":all", "all units", new TrackHeader(1, "all units"), this);
        _selection = new TasksTrack(GroupId + ":sel", "", new TrackHeader(1, ""), this);
    }

    /// <summary>The rows now: the header, <i>all units</i>, and the selection's bars when it has any. ⭐ Composed by the host with <c>TimelinePanel.AddTracks</c>.</summary>
    public IReadOnlyList<ITimelineTrack> Tracks()
    {
        var index = _source()?.Index;
        int selected = _selectedNetId();
        int frames = index?.FrameTicks.Count ?? -1;
        if (!ReferenceEquals(index, _builtFor) || frames != _builtFrames || selected != _builtSelection) Rebuild(index, selected);
        return _rows;
    }

    /// <summary>The markers as last built (rails).</summary>
    public IReadOnlyList<TaskMarker> Markers => _markers;
    /// <summary>The selection's bars as last built (rails).</summary>
    public IReadOnlyList<TaskBar> Bars => _bars;

    private void Rebuild(TimelineIndex? index, int selected)
    {
        _builtFor = index; _builtFrames = index?.FrameTicks.Count ?? -1; _builtSelection = selected;
        _rows.Clear(); _all.Clear(); _selection.Clear(); _markers.Clear(); _bars.Clear();
        if (index == null || !index.Has(BehaviourTimelineLanes.Tasks.Id)) return;
        TasksLane.Markers(index, _markers);
        _rows.Add(_header); _rows.Add(_all);
        // the starts first, the ends after: at a frame where a task ends and the next starts, the end (✓ / ✕ colour) is drawn ON TOP
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            if ((m.Kind == TaskMarkerKind.Started) != (pass == 0)) continue;
            string what = m.Kind switch { TaskMarkerKind.Done => "done", TaskMarkerKind.Failed => "Failed", _ => "started" };
            _all.Add(new TimelineTrackItem("m" + i, SimOf(index, m.Frame), null, $"{NameOf(m.Unit)} · task {m.Task} {what}", TimelineTrackItemKind.Key, false,
                ColourOf(m.Kind), TimelineTrackItemStyle.Pinned), m.Unit);
        }
        if (selected == 0) return;
        int last = Math.Max(0, index.FrameTicks.Count - 1);
        TasksLane.Bars(_markers, selected, last, _bars);
        if (_bars.Count == 0) return;
        _selection.Rename(NameOf(selected));
        for (int i = 0; i < _bars.Count; i++)
        {
            var b = _bars[i];
            string ended = b.Ended switch { TaskMarkerKind.Done => " · done", TaskMarkerKind.Failed => " · Failed", _ => " · running" };
            _selection.Add(new TimelineTrackItem("b" + i, SimOf(index, b.FromFrame), SimOf(index, b.ToFrame), $"task {b.Task}{ended}", TimelineTrackItemKind.Segment, false,   // ⛔ not Selected: the panel draws a selected item white, and the bars' colour is the point
                b.Ended is { } k ? ColourOf(k) : RunningColour), selected);
        }
        _rows.Add(_selection);
    }

    private static uint ColourOf(TaskMarkerKind k) => k switch { TaskMarkerKind.Done => DoneColour, TaskMarkerKind.Failed => FailedColour, _ => StartedColour };

    private static double SimOf(TimelineIndex index, int frame)
    {
        var sim = index.FrameSim;
        if (sim.Count > 0) return sim[Math.Clamp(frame, 0, sim.Count - 1)];
        var t = index.FrameTicks;
        return t.Count == 0 ? 0 : (t[Math.Clamp(frame, 0, t.Count - 1)] - t[0]) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>A unit's name (its <c>EntityInfo</c>), else <c>#id</c>; cached for the rebuild's life.</summary>
    private string NameOf(int net)
    {
        if (_names.TryGetValue(net, out var n)) return n;
        n = $"#{net}";
        if (_world() is { } w && w.IsComponentTypeRegistered<EntityInfo>())
        {
            var e = NetworkIdResolver.ResolveNetworkId(w, net);
            if (!e.IsNull && w.IsAlive(e) && w.HasComponent<EntityInfo>(e) && w.GetComponentRO<EntityInfo>(e).Name.ToString() is { Length: > 0 } name) n = name;
        }
        return _names[net] = n;
    }

    /// <summary>A host's selection as a network id (0 = none / no identity) — the <c>selectedNetId</c> both hosts pass.</summary>
    public static int NetIdOf(EntityRepository? world, Entity? selected)
        => world != null && selected is { } e && !e.IsNull && world.IsAlive(e) ? NetworkIdResolver.RuntimeNetworkIdOf(world, e) : 0;

    internal void SelectUnit(int net) { if (net != 0) _selectUnit?.Invoke(net); }

    /// <summary>One Tasks row: read-only (⛔ the past is not edited); a click selects the marker's unit.</summary>
    private sealed class TasksTrack : ITimelineTrack
    {
        private readonly TasksTimelineTracks _owner;
        private readonly List<TimelineTrackItem> _items = new();
        private readonly Dictionary<string, int> _unitOf = new();

        public TasksTrack(string id, string label, TrackHeader header, TasksTimelineTracks owner) { Id = id; Label = label; Header = header; _owner = owner; }

        public string Id { get; }
        public string Label { get; private set; }
        public TrackHeader? Header { get; private set; }
        public IReadOnlyList<TimelineTrackItem> Items => _items;

        public void Clear() { _items.Clear(); _unitOf.Clear(); }
        public void Add(TimelineTrackItem item, int unit) { _items.Add(item); _unitOf[item.Id] = unit; }
        public void Rename(string label) { Label = label; Header = Header! with { Text = label + " ▸" }; }

        public void Select(string itemId) { if (_unitOf.TryGetValue(itemId, out var u)) _owner.SelectUnit(u); }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<TimelineMenuItem>();
    }
}
