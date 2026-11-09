using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using ImGuiNET;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>
/// ⭐ <c>CE-1302</c> R1-b (docs/DESIGN_Director_Timeline_Editor.md §2b, §9) — the shared event timeline: the overview strip with the
/// zoomed window as a bracket, an adaptive ruler, the lane groups (collapsed, a group is one row), the "now" line and the unscanned part
/// shaded. ⭐ <c>R-4079</c> (§9c): the lanes, their groups, names, colours and priorities come from the REGISTERED descriptors the host hands
/// it — nothing here names a lane.
/// <para>⭐⭐ <c>CE-3330</c> (docs/DESIGN_Replay_Time_Axis_And_Review.md §10; Director Timeline §2c / §2d) — ONE input map on every host:
/// the wheel over the ruler / overview zooms about the mouse (proportional to the delta), over the lanes it scrolls them (the ruler and the
/// overview are sticky); Ctrl+wheel zooms anywhere, Shift+wheel / a horizontal wheel / a middle drag pan; a left drag on the ruler pans, on
/// the overview moves the bracket (its EDGES zoom); Shift+drag selects a range; a left click SELECTS an event (⛔ never seeks — R-3059);
/// the playhead ▼ drags to scrub where the cursor can seek; a double-click fits (ruler) or zooms to a cluster (lanes); the right click
/// opens the menu (the mode's seek items + the row's provider's). The view's rules are <see cref="TimelineView"/>'s, the ruler's
/// <see cref="TimelineRuler"/>'s, the marks <see cref="TimelineLayout"/>'s; this class only draws them and maps input to their calls.</para>
/// </summary>
public sealed class TimelinePanel
{
    /// <summary>⭐ CE-4167 X3 — the label column: <see cref="FlatLabelWidth"/> as ever, <see cref="TreeLabelWidth"/> while any shown track has a <see cref="TrackHeader"/> (a tree needs room).</summary>
    private const float FlatLabelWidth = 84f, TreeLabelWidth = 240f;
    private float _labelWidth = FlatLabelWidth;
    /// <summary>⭐ CE-4167 RV-2 — the sim axis the TRACKS sit on while the extent comes from them (no recording, no wall time map); null otherwise.</summary>
    private TimelineAxis? _rvAxis;
    private static readonly Fdp.Core.FlightRecorder.ReplayTimeMap IdentitySimMap = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(
        new[] { 0L, TimeSpan.TicksPerSecond }, new[] { 0d, 1d }, new Fdp.Core.FlightRecorder.FrameFlags[2]);
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private const float RowHeight = 16f, OverviewHeight = 14f, RulerHeight = 18f, EdgeGrabPx = 4f;

    /// <summary>The presets 0…3 (§10b): the last 30 s, 2 min, 10 min, everything.</summary>
    private static readonly long[] Presets = { 30 * TimeSpan.TicksPerSecond, 120 * TimeSpan.TicksPerSecond, 600 * TimeSpan.TicksPerSecond, 0 };

    /// <summary>Colours for a lane registered without one (ABGR), picked by its id so it stays the same across runs.</summary>
    private static readonly uint[] Palette = { 0xFFE0B070, 0xFF70C0E0, 0xFF90E070, 0xFFE070D0, 0xFF70E0C0, 0xFFC0C0C0 };

    private readonly Dictionary<string, bool> _groupOpen = new();
    private readonly Dictionary<string, TimelineLaneDescriptor> _byId = new();
    private readonly Func<string, int> _priorityOf;
    private readonly Func<string, string> _nameOf;
    private readonly List<TimelineMark> _marks = new();
    private readonly List<RulerTick> _ticks = new();
    private readonly List<TimelineMenuItem> _menu = new();
    private readonly TimelineView _view = new();

    /// <summary>One drawn track item's centre in screen pixels (<see cref="TimelinePanel.LastTrackHits"/>).</summary>
    /// <param name="XEnd">⭐ CE-4221 — the item's right end (a segment); NaN for a key (its end is <paramref name="X"/>).</param>
    public readonly record struct TrackItemHit(string TrackId, string ItemId, float X, float Y, float XEnd = float.NaN);

    private enum Drag : byte { None, Pan, Range, Bracket, BracketFrom, BracketTo, Scrub }
    private Drag _drag;
    private long _dragAnchor;     // a range's first edge / the bracket's grab offset
    private long _scrubTo;
    private float _hoverX = -1f;  // the mouse over the lanes / ruler, in panel x (−1 = elsewhere)
    private TimelineEvent? _selected;
    private TimelineAxis? _axis;                                 // the axis last drawn on
    private Fdp.Core.FlightRecorder.ReplayTimeMap? _timeMap;     // its paused stretches
    private long? _livePoint;                                    // ⭐ CE-3330 E2 — the review's live point on the drawn axis (null = none)
    private (long From, long To)? _loop;                         // ⭐ CE-3330 E5 — the master's loop on the drawn axis (null = none)
    private const uint LoopColour = 0xFF60D860;                  // ABGR — a green no lane uses

    /// <summary>⭐ CE-3330 (§10b "an axis switch keeps the view") — the window and the range mapped through wall time onto the new axis.</summary>
    private void Remap(TimelineAxis from, TimelineAxis to)
    {
        if (!_view.HasExtent) return;
        long Map(long t) => to.ToAxis(from.ToWall(t));
        _view.Remap(Map);
    }

    /// <summary>The visible span (ticks). Exposed for tests and hosts that want to restore it.</summary>
    public TimelineWindow Window => _view.Window;

    /// <summary>⭐ <c>CE-3330</c> — the view state (follow, range, history) — the viewer's own (§10b).</summary>
    public TimelineView View => _view;

    /// <summary>⭐ <c>CE-3330</c> (R-4087 addendum 5) — a left click on an event selects it: the host turns it into the ONE selection
    /// (<c>SelectionChangeRequest</c>, as <c>POST /selection</c>). ⛔ Never a seek, never a private selection here.</summary>
    public Action<TimelineEvent>? OnSelect { get; set; }

    /// <summary>⭐ <c>CE-3330</c> (addendum 4) — the items the row under the mouse contributes (the host's <c>TimelineLaneCatalog.MenuItems</c>).</summary>
    public Func<TimelineMenuContext, IReadOnlyList<TimelineMenuItem>>? RowMenu { get; set; }

    /// <summary>
    /// ⭐ <c>CE-4059</c> (docs/DESIGN_Director_Timeline_Editor.md §11q) — the items a TOOL adds to an EVENT's menu, given the index this panel draws (the
    /// director's <c>DirectorDrafts.MenuItems</c>: "Draft a shot sequence here"). Set by the director's pack on every host — ⛔ not the row's provider
    /// (<see cref="RowMenu"/> is the host's lane catalogue; the window sets it).
    /// </summary>
    public Func<TimelineIndex, TimelineMenuContext, IReadOnlyList<TimelineMenuItem>>? EventMenu { get; set; }

    /// <summary>⭐ <c>CE-1364</c> T2 (docs/DESIGN_Director_Timeline_Editor.md §10) — the editable tracks the host shows ABOVE the lanes (the director's Time /
    /// Camera); null or empty = none, every existing host unchanged. The panel draws, hit-tests, selects and drags; it knows nothing about takes.</summary>
    public Func<IReadOnlyList<ITimelineTrack>>? Tracks { get; set; }

    // ⭐ W-1 / P-8 8c-2 — more than one owner of track rows: Tracks (the director's take) keeps its one setter; a feature adds its rows here
    //   (the mission plan's Tasks rows). Drawn after Tracks' rows, in the order added; concatenated into one reused buffer (0 B per steady frame).
    private readonly List<Func<IReadOnlyList<ITimelineTrack>>> _extraTracks = new();
    private readonly List<ITimelineTrack> _allTracks = new();

    /// <summary>⭐ W-1 / P-8 8c-2 — adds a source of track rows drawn after <see cref="Tracks"/>' (a feature's own rows; never replaces another owner's).</summary>
    public void AddTracks(Func<IReadOnlyList<ITimelineTrack>> source) => _extraTracks.Add(source ?? throw new ArgumentNullException(nameof(source)));

    private IReadOnlyList<ITimelineTrack>? CollectTracks()
    {
        var first = Tracks?.Invoke();
        if (_extraTracks.Count == 0) return first;
        _allTracks.Clear();
        if (first != null) for (int i = 0; i < first.Count; i++) _allTracks.Add(first[i]);
        for (int s = 0; s < _extraTracks.Count; s++) { var more = _extraTracks[s](); for (int i = 0; i < more.Count; i++) _allTracks.Add(more[i]); }
        return _allTracks.Count == 0 && first == null ? null : _allTracks;
    }

    /// <summary>⭐ R-6 / V7 — every row the panel draws now (the director's then each feature's), as a host rail reads them. For unit testing only.</summary>
    internal IReadOnlyList<ITimelineTrack>? CollectedTracksForTest() => CollectTracks();
    /// <summary>⭐ R-6 / V7 — how many feature sources added rows with <see cref="AddTracks"/> (a host-composition rail). For unit testing only.</summary>
    internal int ExtraTrackSourceCount => _extraTracks.Count;

    /// <summary>⭐ <c>CE-4216</c> (RV-3a) — draw the ruler and the tracks' headers even when no track holds an item yet (the rules view before a run: authored rows have no past). Off by default: the director's empty tracks keep "No recording.".</summary>
    public bool ExtentWithoutItems { get; set; }

    /// <summary>⭐ <c>CE-1364</c> T3 (TE-14) — Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) while the panel is focused AND it shows tracks: the host's undo of a track edit (the
    /// editor's existing stack). Null = the key does nothing here.</summary>
    public Action? Undo { get; set; }
    public Action? Redo { get; set; }

    /// <summary>Where each track item was drawn last frame (screen pixels) — for a frame rail that must press on one.</summary>
    public IReadOnlyList<TrackItemHit> LastTrackHits => _trackHits;

    /// <summary>⭐ CE-4264 — where the ruler drew the playhead's ▼ last frame (its tip, screen pixels), and the ruler's own rectangle (the frame rails drag it).</summary>
    public Vector2? LastPlayheadAt { get; private set; }
    public (Vector2 Min, Vector2 Max) LastRulerRect { get; private set; }

    /// <summary>⭐ CE-4221 X5 — how many Coming up lines the strip below the lanes shows; 0 (the default) = no strip, so every host that does not ask is unchanged.</summary>
    public int ComingUpCount { get; set; }

    /// <summary>⭐ CE-4221 X5 — the sim seconds "coming up" is counted from; null = the playhead.</summary>
    public Func<double>? ComingUpNow { get; set; }

    /// <summary>What the last frame drew for X1 / X4 / X5 — for the rails.</summary>
    public IReadOnlyList<Connector> LastConnectors => _connectors;
    public IReadOnlyList<(string TrackId, string ItemId, Vector2 Min, Vector2 Max)> LastHandles => _handles;
    public IReadOnlyList<ComingUpEntry> LastComingUp => _comingUp;
    /// <summary>The row bands of the shown tracks (screen y), so a rail can drop on one.</summary>
    public IReadOnlyList<(string TrackId, float Y0, float Y1)> LastRowBands => _bands;

    private float _rowRight = float.PositiveInfinity;
    private readonly List<Connector> _connectors = new();
    private readonly List<(string TrackId, string ItemId, Vector2 Min, Vector2 Max)> _handles = new();
    private readonly List<(ITimelineTrack Track, float Y0, float Y1)> _rowBands = new();
    private readonly List<(string TrackId, float Y0, float Y1)> _bands = new();
    private IReadOnlyList<ComingUpEntry> _comingUp = Array.Empty<ComingUpEntry>();
    private (ITimelineTrack Track, string ItemId, Vector2 Start)? _connect;   // ⭐ CE-4221 X4 — a connect drag in progress

    private readonly TrackGesture _gesture = new();
    private readonly List<TrackItemHit> _trackHits = new();
    private readonly List<TimelineMenuItem> _trackMenu = new();
    private IReadOnlyList<ITimelineTrack>? _tracksNow;

    /// <summary>The event the user selected last (drawn ringed), or null.</summary>
    public TimelineEvent? Selected => _selected;

    /// <summary>Shows <paramref name="window"/> (clamped to the extent) — a host zooming to an event, a rail, R1-d's jump.</summary>
    public void Show(TimelineWindow window) => _view.Show(window);

    private Fdp.Toolkit.Input.KeymapReader _keys = null!;   // ⭐ B-10 K4 (§6g) — the timeline's rows of the one keymap (FocusedWindow, "Timeline"), read here
    private float _panDragPx;                        // the movement accumulated while the pan button is held (the 2 px that start a drag, as ImGui's own threshold did)

    /// <summary>⭐ B-10 K4 — reads the timeline's keys from <paramref name="keymap"/> through <paramref name="input"/> (production: the process's keymap and ImGui; a rail hands in its own).</summary>
    internal void UseKeys(Fdp.Toolkit.Input.Keymap keymap, Fdp.Toolkit.Input.IChordInput input)
    {
        TimelineKeys.Declare(keymap);
        _keys = new Fdp.Toolkit.Input.KeymapReader(keymap, Fdp.Toolkit.Input.InputScope.FocusedWindow, TimelineKeys.Kind) { Input = input };
    }

    public TimelinePanel()
    {
        UseKeys(Fdp.Toolkit.Input.Keymap.Process, new Fdp.Presentation.Input.ImGuiChordInput());
        _priorityOf = id => _byId.TryGetValue(id, out var d) ? d.Priority : 0;
        _nameOf = id => _byId.TryGetValue(id, out var d) ? d.DisplayName : id;
    }

    /// <summary>A lane's colour: its descriptor's, else a stable pick from the palette (ABGR).</summary>
    public static uint ColorOf(TimelineLaneDescriptor lane) =>
        lane.Color != 0 ? lane.Color : Palette[(int)((uint)StableHash(lane.Id) % (uint)Palette.Length)];

    private uint ColorOf(string laneId) => _byId.TryGetValue(laneId, out var d) ? ColorOf(d) : Palette[0];

    private static int StableHash(string s)
    {
        unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; }
    }

    /// <param name="lanes">The registered lanes to show (<c>TimelineLaneCatalog.Lanes</c>), in order; their <c>Group</c> makes the sections.</param>
    public void Draw(ITimelineSource? source, ITimelineCursor? cursor, IReadOnlyList<TimelineLaneDescriptor> lanes)
    {
        _byId.Clear();
        foreach (var lane in lanes) _byId[lane.Id] = lane;
        lanes = TimelineLayout.Drawn(lanes);   // ⭐ CE-4117: a hidden lane is not drawn
        if (_gesture.Active && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) _gesture.Release();   // ⭐ CE-1364 T2 — commits once, wherever it is released (T10's lesson)
        _trackHits.Clear(); _handles.Clear(); _rowBands.Clear(); _connectors.Clear();
        _tracksNow = CollectTracks();
        _labelWidth = TrackLayout.LabelWidthFor(_tracksNow, FlatLabelWidth, TreeLabelWidth);   // ⭐ CE-4167 X3
        // ⭐ CE-3330 T13 — everything below is on the ACTIVE axis: the index's frame times, the extent, "now" and every seek go through it
        var axis = cursor?.Axis ?? TimelineAxis.Wall;
        if (_axis != null && !ReferenceEquals(axis, _axis) && axis.IsSim != _axis.IsSim) Remap(_axis, axis);   // §10b: the same FRAMES stay in view
        _axis = axis;
        _rvAxis = null;
        _timeMap = cursor?.TimeMap;
        _rawIndex = source?.Index;   // ⭐ CE-4263 — the WALL index and extent start, kept so a click can name its time on both axes
        _wallFrom = cursor?.ExtentFrom ?? (_rawIndex is { FrameTicks.Count: > 0 } ri ? ri.FrameTicks[0] : 0);
        if (cursor != null && axis.IsSim) cursor = new AxisCursor(cursor, axis);
        _livePoint = cursor?.LivePoint;
        _loop = cursor?.Loop;
        var index = axis.Project(source?.Index ?? TimelineIndex.Empty);
        long from = cursor?.ExtentFrom ?? (index.FrameTicks.Count > 0 ? index.FrameTicks[0] : 0);
        long to = cursor?.ExtentTo ?? (index.FrameTicks.Count > 0 ? index.FrameTicks[^1] : 0);
        if (to <= from)
        {
            // ⭐ CE-4167 RV-2 — no recording, but TRACKS (a rules view in edit mode): their items give the extent, [min(0, From), max(now, To)], so they draw. The lanes keep their own rule (no index ⇒ none).
            var ext = TrackLayout.ExtentOfTracks(_tracksNow, SimNow(cursor), ExtentWithoutItems);
            if (ext is { } e)
            {
                // the items are SIM seconds: a host with no recording has no time map to place them on the wall axis, so they sit on a plain sim axis (ticks = seconds) of their own
                if (!TrackLayout.Drawable(axis, _timeMap)) _rvAxis = TimelineAxis.Sim(IdentitySimMap);
                var tracksAxis = _rvAxis ?? axis;
                from = TrackLayout.AxisTicks(e.From, tracksAxis, _timeMap) ?? 0; to = TrackLayout.AxisTicks(e.To, tracksAxis, _timeMap) ?? 0;
            }
            if (to <= from)
            {
                Plain(source == null ? "No recording." : "Indexing...", Dim);
                return;
            }
        }
        long spacing = TimelineLayout.FrameSpacing(index);
        if (spacing > 0) _view.MinSpan = spacing * TimelineView.MinSpanFrames;
        _view.OnExtent(from, to);   // ⭐ T1 — kept, not reset
        long now = _rvAxis != null ? 0L : cursor?.Now ?? from;   // ⭐ CE-4167 RV-2: on the tracks' own sim axis the playhead is its sim zero (the host's wall ticks mean nothing there)
        _view.KeepInView(now, cursor?.IsPlaying ?? false);   // ⭐ T12

        if (source?.Error is { } error) Plain($"Index stopped at {source.Progress * 100f:0}%: {error}", new Vector4(1f, 0.5f, 0.4f, 1f));
        else if (source is { IsComplete: false }) Plain($"Indexing... {source.Progress * 100f:0}%", Dim);

        var io = ImGui.GetIO();
        var dl = ImGui.GetWindowDrawList();
        float width = Math.Max(10f, ImGui.GetContentRegionAvail().X - _labelWidth);
        bool focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _drag != Drag.None) EndDrag(index, cursor);   // ⭐ T10 — wherever it is released
        _hoverX = -1f;

        var all = new string[lanes.Count];
        for (int i = 0; i < all.Length; i++) all[i] = lanes[i].Id;
        float x0 = ImGui.GetCursorScreenPos().X + _labelWidth;
        DrawOverview(dl, index, all, width, now);
        DrawRuler(dl, index, width, now, cursor);

        // ⭐ T11 — the lanes scroll in a child of their own, so the overview and the ruler stay put
        // ⭐ CE-4221 X5 — Coming up (host opt-in): the next Expected / Pinned things, as a strip BELOW the lanes, so the lanes give it its room
        _comingUp = ComingUpCount > 0 && _tracksNow != null ? ComingUp.Of(_tracksNow, ComingUpNow?.Invoke() ?? SimNow(cursor), ComingUpCount) : Array.Empty<ComingUpEntry>();
        float comingUpHeight = ComingUpCount > 0 ? (1 + Math.Max(1, _comingUp.Count)) * ImGui.GetTextLineHeightWithSpacing() + 6f : 0f;
        float lanesHeight = Math.Max(RowHeight * 2, ImGui.GetContentRegionAvail().Y - comingUpHeight);
        ImGui.BeginChild("##tl-lanes", new Vector2(0, lanesHeight), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        var cdl = ImGui.GetWindowDrawList();
        float lanesTop = ImGui.GetCursorScreenPos().Y;
        if (_tracksNow != null)   // ⭐ CE-1364 T2 — the editable tracks, ABOVE the lanes
        {
            var shown = TrackLayout.Visible(_tracksNow, _collapsed);
            foreach (var track in shown) DrawTrack(cdl, track, index, width, cursor);   // ⭐ CE-4167 X3 — a collapsed node hides its deeper tracks
            DrawConnectors(cdl, shown);        // ⭐ CE-4221 X1 — after ALL tracks, from their hit positions
            DrawConnectDrag(cdl, io);          // ⭐ CE-4221 X4
        }
        foreach (var (group, members) in Groups(lanes))
        {
            bool open = !_groupOpen.TryGetValue(group, out var o) || o;
            if (ImGui.SmallButton((open ? "v " : "> ") + (group.Length == 0 ? "Other" : group) + "##tlg" + group)) _groupOpen[group] = !open;
            if (!open)
            {
                ImGui.SameLine();
                DrawRow(cdl, index, members, "", width, cursor);
                continue;
            }
            foreach (var lane in members) DrawRow(cdl, index, new[] { lane }, _nameOf(lane), width, cursor);
        }
        float lanesBottom = Math.Max(ImGui.GetCursorScreenPos().Y, lanesTop + lanesHeight);
        DrawOverlays(cdl, index, x0, width, lanesTop, lanesBottom, now);
        ImGui.EndChild();
        if (ComingUpCount > 0) DrawComingUp();

        PanAnywhere(width);
        if (focused) Keys(index, all, width, now, cursor);
        DrawMenu(index, cursor);
    }

    /// <summary>⭐ CE-4167 RV-2 — the playhead as sim seconds (0 without a cursor or a way to place it).</summary>
    private double SimNow(ITimelineCursor? cursor)
        => cursor is null ? 0d : TrackLayout.SimSeconds(cursor.Now, _axis ?? TimelineAxis.Wall, _timeMap) ?? 0d;

    /// <summary>⭐ CE-4167 X3 — is the tree node <paramref name="trackId"/> collapsed (its deeper tracks hidden)?</summary>
    public bool IsCollapsed(string trackId) => _collapsed.Contains(trackId);

    /// <summary>⭐ CE-4167 X3 — collapses / expands a tree node (the panel keeps the set by track id; a host may restore it).</summary>
    public void SetCollapsed(string trackId, bool collapsed) { if (collapsed) _collapsed.Add(trackId); else _collapsed.Remove(trackId); }

    // ── the overview strip ────────────────────────────────────────────────────────────────────────────────────

    private void DrawOverview(ImDrawListPtr dl, TimelineIndex index, string[] allLanes, float width, long now)
    {
        ImGui.Dummy(new Vector2(_labelWidth, OverviewHeight));
        ImGui.SameLine(0, 0);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-overview", new Vector2(width, OverviewHeight));
        bool hovered = ImGui.IsItemHovered();
        var all = new TimelineWindow(_view.ExtentFrom, _view.ExtentTo);
        dl.AddRectFilled(p, p + new Vector2(width, OverviewHeight), 0x30FFFFFF);
        foreach (var m in TimelineLayout.Marks(index, allLanes, all, width, _priorityOf, into: _marks))
            DrawMark(dl, m, p, OverviewHeight, labelRoom: 0);   // the overview shows marks, never counts
        var (b0, b1) = TimelineLayout.Bracket(_view.Window, _view.ExtentFrom, _view.ExtentTo, width);
        dl.AddRect(p + new Vector2(b0, 0), p + new Vector2(Math.Max(b1, b0 + 2), OverviewHeight), 0xFFFFFFFF);
        float xn = all.XOf(now, width);
        dl.AddLine(p + new Vector2(xn, 0), p + new Vector2(xn, OverviewHeight), 0xFF4040FF);

        var io = ImGui.GetIO();
        float mx = io.MousePos.X - p.X;
        if (hovered && WheelZoom() is var wheel and not 0f)   // ⭐ T4 — the wheel zooms over the overview too, about the tick under the mouse
            _view.Zoom(_view.Window.XOf(all.TicksAt(mx, width), width), width, TimelineView.WheelFactor(wheel));
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _view.Remember();
            // ⭐ T9 — grab an EDGE to zoom, the inside to move, outside to jump the bracket there
            _drag = Math.Abs(mx - b0) <= EdgeGrabPx ? Drag.BracketFrom : Math.Abs(mx - b1) <= EdgeGrabPx ? Drag.BracketTo : Drag.Bracket;
            long at = all.TicksAt(mx, width);
            _dragAnchor = _drag == Drag.Bracket && (mx < b0 || mx > b1) ? _view.Window.Span / 2 : at - _view.Window.From;
        }
        if (_drag is Drag.Bracket or Drag.BracketFrom or Drag.BracketTo && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            long at = all.TicksAt(Math.Clamp(mx, 0, width), width);
            var w = _view.Window;
            var next = _drag switch
            {
                Drag.BracketFrom => new TimelineWindow(Math.Min(at, w.To - _view.MinSpan), w.To),
                Drag.BracketTo => new TimelineWindow(w.From, Math.Max(at, w.From + _view.MinSpan)),
                _ => new TimelineWindow(at - _dragAnchor, at - _dragAnchor + w.Span),
            };
            _view.DragTo(next);
        }
        if (hovered && (Math.Abs(mx - b0) <= EdgeGrabPx || Math.Abs(mx - b1) <= EdgeGrabPx)) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
    }

    // ── ⭐ CE-4325 (E7) — the notes on the ruler ─────────────────────────────────────────────────────────────────

    private const uint NoteColour = 0xFF30B0F0;          // ABGR — the amber of a note (the Marks window's note glyph)
    private const uint FlagColour = 0xFFF0C060;          // ABGR — the blue of a mark (⭐ CE-4325: the mockup's ⚑ is blue, the note dot beside it amber — it was amber, which hid the dot)
    private long _markHitTicks = long.MinValue;           // the mark / note under the mouse this frame (the ruler's double-click seeks to it)

    /// <summary>The note riding mark <paramref name="label"/>, or −1. An index loop: the ruler draws every frame.</summary>
    private static int NoteOfMark(IReadOnlyList<TimelineNote> notes, string label)
    {
        for (int i = 0; i < notes.Count; i++) if (notes[i].Mark.Length > 0 && string.Equals(notes[i].Mark, label, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>The tooltip of a note: a framed block of three lines — name · time, the words, unit · who (only on hover).</summary>
    private static void NoteTooltip(in TimelineNote n)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(n.Title);
        if (n.Text.Length > 0) ImGui.TextUnformatted("\"" + n.Text + "\"");
        if (n.Detail.Length > 0) ImGui.TextUnformatted(n.Detail);
        ImGui.EndTooltip();
    }

    // ── the ruler ────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawRuler(ImDrawListPtr dl, TimelineIndex index, float width, long now, ITimelineCursor? cursor)
    {
        ImGui.Dummy(new Vector2(_labelWidth, RulerHeight));
        ImGui.SameLine(0, 0);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-ruler", new Vector2(width, RulerHeight), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGui.IsItemHovered();
        var w = _view.Window;
        long spacing = TimelineLayout.FrameSpacing(index);
        foreach (var t in TimelineRuler.Ticks(w, width, _view.ExtentFrom, _view.ExtentTo, s => ImGui.CalcTextSize(s).X, spacing, _ticks))
        {
            dl.AddLine(p + new Vector2(t.X, RulerHeight - (t.Major ? 6 : 3)), p + new Vector2(t.X, RulerHeight), t.Major ? 0xFFA0A0A0 : 0xFF707070);
            if (t.Label != null) dl.AddText(p + new Vector2(t.X + 2, 0), 0xFFA0A0A0, t.Label);
        }

        // ⭐ CE-3330 T13 (R10, §10b) — on the Sim axis each collapsed pause is a break mark on the ruler with its tick: ◯ unchanged, ● with edits
        if (_axis is { IsSim: true } && _timeMap != null)
            foreach (var pz in _axis.Pauses(_timeMap))
            {
                float xp = w.XOf(pz.From, width);
                if (xp < 0 || xp > width) continue;
                var c = p + new Vector2(xp, RulerHeight - 4);
                dl.AddLine(c + new Vector2(-3, -9), c + new Vector2(0, -6), 0xFFD0A060);   // a small zigzag: time is continuous, the world is not
                dl.AddLine(c + new Vector2(0, -6), c + new Vector2(-3, -3), 0xFFD0A060);
                if (pz.Changed) dl.AddCircleFilled(c, 3f, 0xFFD0A060); else dl.AddCircle(c, 3f, 0xFFD0A060);
                if (hovered && Math.Abs(ImGui.GetIO().MousePos.X - p.X - xp) <= 4)
                    ImGui.SetTooltip(pz.Changed ? "A pause, with edits (they appear at the unpause)" : "A pause (nothing changed)");
            }

        // ⭐ CE-3330 E2 (§2e) — the session's named checkpoints: a flag on the ruler, its label in a tooltip (off-screen ones are not drawn)
        // ⭐ CE-4325 (E7) — a mark's NOTE: an amber dot beside its flag and the note's words in the tooltip (name · time / the words / unit · who); an AAR note (no flag) is a dot of its own.
        //   Index loops over the lists — the ruler draws every frame (0 B steady). Double-click on one seeks there where the cursor can (R-3059: a click selects, never seeks — this is the explicit gesture).
        _markHitTicks = long.MinValue;
        if (cursor != null)
        {
            var marks = cursor.Checkpoints;
            var notes = cursor.Notes;
            float mouseX = ImGui.GetIO().MousePos.X - p.X;
            for (int ci = 0; ci < marks.Count; ci++)
            {
                var cp = marks[ci];
                float xc = w.XOf(cp.WallTicks, width);
                if (xc < 0 || xc > width) continue;
                dl.AddText(p + new Vector2(xc - 3, 0), FlagColour, Fdp.Presentation.Fonts.IconsFontAwesome6.Flag);
                int ni = NoteOfMark(notes, cp.Label);
                if (ni >= 0) dl.AddCircleFilled(p + new Vector2(xc + 7, 3), 3f, NoteColour);
                if (hovered && Math.Abs(mouseX - xc) <= 5)
                {
                    _markHitTicks = cp.WallTicks;
                    if (ni >= 0) NoteTooltip(notes[ni]); else ImGui.SetTooltip(cp.Label);
                }
            }
            for (int ni = 0; ni < notes.Count; ni++)
            {
                var n = notes[ni];
                if (n.Mark.Length > 0) continue;   // on a mark: drawn with it above
                float xn2 = w.XOf(n.WallTicks, width);
                if (xn2 < 0 || xn2 > width) continue;
                dl.AddCircleFilled(p + new Vector2(xn2, RulerHeight - 8), 3.5f, NoteColour);
                if (hovered && Math.Abs(mouseX - xn2) <= 5) { _markHitTicks = n.WallTicks; NoteTooltip(n); }
            }
        }

        // ⭐ CE-3330 E5 (DAR §12n) — the MASTER's loop as a bracket on the ruler: every host draws it, whoever set it
        if (_loop is { } lp && lp.To > lp.From)
        {
            float la = Math.Clamp(w.XOf(lp.From, width), 0, width), lb = Math.Clamp(w.XOf(lp.To, width), 0, width);
            dl.AddRectFilled(p + new Vector2(la, RulerHeight - 4), p + new Vector2(Math.Max(lb, la + 1), RulerHeight), LoopColour);
            if (w.XOf(lp.From, width) >= 0) dl.AddLine(p + new Vector2(la, 2), p + new Vector2(la, RulerHeight), LoopColour, 2f);
            if (w.XOf(lp.To, width) <= width) dl.AddLine(p + new Vector2(lb, 2), p + new Vector2(lb, RulerHeight), LoopColour, 2f);
        }

        // the playhead ▼ (a handle where the cursor seeks), or an arrow at the edge it left by (§10b)
        float xn = w.XOf(_drag == Drag.Scrub ? _scrubTo : now, width);
        bool canSeek = cursor is { CanSeek: true };
        LastRulerRect = (p, p + new Vector2(width, RulerHeight));
        LastPlayheadAt = xn >= 0 && xn <= width ? p + new Vector2(xn, RulerHeight - 4) : null;
        if (xn >= 0 && xn <= width)
            dl.AddTriangleFilled(p + new Vector2(xn - 5, RulerHeight - 8), p + new Vector2(xn + 5, RulerHeight - 8), p + new Vector2(xn, RulerHeight),
                canSeek ? 0xFF4040FF : 0xFF4040A0);
        else
        {
            float ex = xn < 0 ? 2 : width - 2, dir = xn < 0 ? -1 : 1;
            dl.AddTriangleFilled(p + new Vector2(ex - 6 * dir, 3), p + new Vector2(ex - 6 * dir, RulerHeight - 3), p + new Vector2(ex, RulerHeight / 2), 0xFF4040FF);
        }

        var io = ImGui.GetIO();
        float mx = io.MousePos.X - p.X;
        // ⭐ CE-4264 — a drag started here (scrub ▼, pan, Shift-range) follows the mouse once it leaves the one-row ruler: at a low frame rate one fast move is enough
        // to leave it, and the drag used to freeze until the mouse came back. The release ends it wherever it happens (T10).
        if (!hovered) { if (_drag is Drag.Scrub or Drag.Pan or Drag.Range) DragUpdate(io, w, width, mx, index); return; }
        _hoverX = mx;
        if (WheelZoom() is var wheel and not 0f)   // ⭐ T4 / T5 — the wheel zooms over the ruler, proportional to the delta (Shift+wheel is the pan's: its own row shadows the zoom's)
            _view.Zoom(mx, width, TimelineView.WheelFactor(wheel));
        bool offscreenArrow = (xn < 0 && mx <= 10) || (xn > width && mx >= width - 10);
        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (_markHitTicks != long.MinValue && canSeek) { cursor!.Seek(_markHitTicks); _drag = Drag.None; return; }   // ⭐ CE-4325 — double-click a mark / note: seek there
            _view.Fit(); _drag = Drag.None; return;   // fit all
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (offscreenArrow) { _view.Reveal(now); return; }
            if (io.KeyShift) { _drag = Drag.Range; _dragAnchor = w.TicksAt(mx, width); _view.Range = (_dragAnchor, _dragAnchor); }
            else if (canSeek && Math.Abs(mx - xn) <= 6) { _drag = Drag.Scrub; _scrubTo = now; }   // drag ▼ = scrub (review / replay)
            else _drag = Drag.Pan;
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) OpenMenu(index, cursor, null, w.TicksAt(mx, width), null);
        if (canSeek && Math.Abs(mx - xn) <= 6) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        DragUpdate(io, w, width, mx, index);
    }

    private int _dragUpdatedFrame = -1;

    private void DragUpdate(ImGuiIOPtr io, TimelineWindow w, float width, float mx, TimelineIndex index)
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;
        // ⭐ CE-4264 — once a frame: the ruler now updates an active drag off the ruler too, and a hovered row would apply the same pan delta a second time
        int frame = ImGui.GetFrameCount();
        if (_dragUpdatedFrame == frame) return;
        _dragUpdatedFrame = frame;
        switch (_drag)
        {
            case Drag.Pan: if (io.MouseDelta.X != 0) _view.Pan(io.MouseDelta.X, width); break;
            case Drag.Range:
                long at = w.TicksAt(Math.Clamp(mx, 0, width), width);
                _view.Range = at < _dragAnchor ? (at, _dragAnchor) : (_dragAnchor, at);
                break;
            case Drag.Scrub: _scrubTo = TimelineLayout.SnapToFrame(index, w.TicksAt(Math.Clamp(mx, 0, width), width)); break;
        }
    }

    private void EndDrag(TimelineIndex index, ITimelineCursor? cursor)
    {
        if (_drag == Drag.Scrub && cursor is { CanSeek: true }) cursor.Seek(_scrubTo);   // a scrub seeks on release (§10c)
        if (_drag == Drag.Range && _view.Range is { } r && r.To - r.From < _view.MinSpan / 4) _view.Range = null;   // a Shift+click, not a range
        _drag = Drag.None;
    }

    // ── a lane row ───────────────────────────────────────────────────────────────────────────────────────────

    private void DrawRow(ImDrawListPtr dl, TimelineIndex index, string[] lanes, string label, float width, ITimelineCursor? cursor)
    {
        if (label.Length > 0)
        {
            Plain(label, ImGui.ColorConvertU32ToFloat4(ColorOf(lanes[0])));
            ImGui.SameLine(_labelWidth);
        }
        else ImGui.SameLine(_labelWidth);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-row" + string.Join("-", lanes), new Vector2(width, RowHeight),
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        dl.AddRectFilled(p, p + new Vector2(width, RowHeight), 0x18FFFFFF);
        var w = _view.Window;
        TimelineLayout.Marks(index, lanes, w, width, _priorityOf, into: _marks);
        dl.PushClipRect(p, p + new Vector2(width, RowHeight), true);   // ⭐ CE-4265 — nothing of the row's content reaches the label column
        for (int i = 0; i < _marks.Count; i++)   // a count label only where it fits before the next mark
            DrawMark(dl, _marks[i], p, RowHeight, (i + 1 < _marks.Count ? _marks[i + 1].X0 : width) - _marks[i].X0 - 6f);
        if (_selected is { } sel && Array.IndexOf(lanes, sel.Lane) >= 0 && sel.Frame < index.FrameTicks.Count)
        {
            float xs = w.XOf(index.FrameTicks[sel.Frame], width);
            if (xs >= 0 && xs <= width) dl.AddCircle(p + new Vector2(xs, RowHeight / 2), 6f, 0xFFFFFFFF, 12, 1.5f);
        }
        dl.PopClipRect();
        if (!hovered) return;

        var io = ImGui.GetIO();
        float mx = io.MousePos.X - p.X;
        _hoverX = mx;
        if (_keys.Axis(TimelineKeys.ZoomWheelAnywhere) is var anywhere and not 0f)   // Ctrl+wheel zooms anywhere; the plain wheel scrolls the lanes (the child's own)
            _view.Zoom(mx, width, TimelineView.WheelFactor(anywhere));
        int hit = TimelineLayout.MarkAt(_marks, mx, TimelineLayout.DefaultGlyphPx);
        var nearest = TimelineLayout.NearestEvent(index, lanes, w, width, mx, TimelineLayout.DefaultGlyphPx);

        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && hit >= 0)   // zoom to the cluster (§10c), ⛔ never a seek
        {
            var m = _marks[hit];
            long a = index.FrameTicks[Math.Min(m.FromFrame, index.FrameTicks.Count - 1)];
            long b = index.FrameTicks[Math.Min(Math.Max(m.ToFrame - 1, m.FromFrame), index.FrameTicks.Count - 1)];
            long pad = Math.Max(_view.MinSpan, (b - a) / 4);
            _view.Show(new TimelineWindow(a - pad, b + pad));
        }
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (io.KeyShift) { _drag = Drag.Range; _dragAnchor = w.TicksAt(mx, width); _view.Range = (_dragAnchor, _dragAnchor); }
            else if (nearest is { } e) Select(e);   // ⭐ T2 — a left click SELECTS (R-3059); ⛔ T3 — a drag on empty space does nothing
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            OpenMenu(index, cursor, lanes.Length == 1 ? lanes[0] : nearest?.Lane, w.TicksAt(mx, width), nearest);
        DragUpdate(io, w, width, mx, index);
        if (hit >= 0)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(TimelineLayout.Describe(index, lanes, _marks[hit], _view.ExtentFrom, _nameOf));
            ImGui.EndTooltip();
        }
    }

    private TimelineIndex? _rawIndex;
    private long _wallFrom;

    /// <summary>⭐ CE-4263 — the lane's display name (the catalogue's) as of the last draw; its id when unknown.</summary>
    public string LaneNameOf(string laneId) => _nameOf(laneId);

    /// <summary>⭐ CE-4263 — the last SELECTED event's time as the timeline shows it (<c>h:mm:ss.f</c> from the extent's start): wall, and sim when the session has a time map. Set just before <see cref="OnSelect"/> runs.</summary>
    public string? LastSelectedWallText { get; private set; }
    public string? LastSelectedSimText { get; private set; }

    private void Select(TimelineEvent e)
    {
        LastSelectedWallText = LastSelectedSimText = null;
        if (_rawIndex is { } idx && e.Frame >= 0 && e.Frame < idx.FrameTicks.Count)
        {
            long wall = idx.FrameTicks[e.Frame];
            LastSelectedWallText = TimeSpan.FromTicks(wall - _wallFrom).ToString(@"h\:mm\:ss\.f");
            if (_timeMap is { HasSimTime: true } map) LastSelectedSimText = TimeSpan.FromSeconds(map.SimAt(wall)).ToString(@"h\:mm\:ss\.f");
        }
        _selected = e;
        OnSelect?.Invoke(e);
    }

    // ── an editable track row (CE-1364 T2) ───────────────────────────────────────────────────────────────────

    private const uint TrackKeyColour = 0xFF50B0F0, TrackSegmentColour = 0xB0C09050;

    private void DrawTrack(ImDrawListPtr dl, ITimelineTrack track, TimelineIndex index, float width, ITimelineCursor? cursor)
    {
        if (track.Header is { } header) DrawHeader(dl, track, header);
        else Plain(track.Label, Dim);
        ImGui.SameLine(_labelWidth);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-track" + track.Id, new Vector2(width, RowHeight), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGui.IsItemHovered();
        _rowBands.Add((track, p.Y, p.Y + RowHeight)); _bands.Add((track.Id, p.Y, p.Y + RowHeight)); _rowRight = p.X + width;
        dl.AddRectFilled(p, p + new Vector2(width, RowHeight), 0x28FFB060);
        var w = _view.Window;
        var axis = _rvAxis ?? _axis ?? TimelineAxis.Wall;   // ⭐ CE-4167 RV-2: the tracks' own sim axis while the extent comes from them
        var map = axis.Map ?? _timeMap;
        bool drawable = TrackLayout.Drawable(axis, map);
        var items = track.Items;
        // ⭐ CE-4265 — a span that starts left of the view (the timeline panned left) is clipped at the lane's left edge: it used to draw over the label column.
        // The connect handle may stick out past the right end by its own width, so the clip leaves it that room.
        dl.PushClipRect(p, p + new Vector2(width + 16f, RowHeight), true);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (TrackLayout.SpanOf(it, w, width, axis, map) is not { } s) continue;
            float cy = p.Y + RowHeight / 2;
            uint col = it.Selected ? 0xFFFFFFFF : it.Colour ?? TrackKeyColour;
            var look = TrackLayout.DrawOf(it, Math.Max(s.X1, s.X0 + 2f) - s.X0);   // ⭐ CE-4167 X2 — Solid is exactly the drawing below
            if (it.Kind == TimelineTrackItemKind.Segment)
            {
                var a = new Vector2(p.X + s.X0, p.Y + 2);
                var b = new Vector2(p.X + Math.Max(s.X1, s.X0 + 2f), p.Y + RowHeight - 2);
                uint edge = it.Selected ? 0xFFFFFFFF : it.Colour ?? 0xFFC09050;
                // ⭐ CE-4216 — an item with its OWN colour tints its fill and outline (the rules view's state colours); one without keeps the track's blue exactly as before
                uint fill = it.Colour is { } own ? (own & 0x00FFFFFFu) | 0xB0000000u : TrackSegmentColour;
                if (look.Fill) dl.AddRectFilled(a, b, fill);
                if (look.Dashed) DashedRect(dl, a, b, edge, p.X - 1f, p.X + width + 1f);   // ⭐ CE-4223 — clipped to the row
                else dl.AddRect(a, b, it.Selected ? 0xFFFFFFFF : it.Colour ?? 0xFFC09050);
                if (look.Hatch)
                {
                    // ⭐ CE-4223 — hatch only the part inside the row (the same unbounded-width blow-up as the dashes)
                    var ha = new Vector2(Math.Max(a.X, p.X - 1f), a.Y); var hb = new Vector2(Math.Min(b.X, p.X + width + 1f), b.Y);
                    if (hb.X > ha.X)
                        foreach (var (hx0, hy0, hx1, hy1) in TrackLayout.HatchLines(hb.X - ha.X, hb.Y - ha.Y, Math.Max(5f, (hb.X - ha.X) / 30f)))
                            dl.AddLine(ha + new Vector2(hx0, hy0), ha + new Vector2(hx1, hy1), edge);
                }
                if (look.Handles)   // CE-1364: the two edge handles
                {
                    dl.AddRectFilled(a, new Vector2(a.X + 3f, b.Y), 0xFFE0B070);
                    dl.AddRectFilled(new Vector2(b.X - 3f, a.Y), b, 0xFFE0B070);
                }
                if (look.Diamond) DrawKeyDiamond(dl, new Vector2(p.X + s.X0, cy), col, hollow: false);
                if (look.Tick) dl.AddLine(new Vector2(p.X + s.X0, p.Y), new Vector2(p.X + s.X0, p.Y + RowHeight), col);
            }
            else
            {
                var c = new Vector2(p.X + s.X0, cy);
                DrawKeyDiamond(dl, c, col, look.HollowDiamond);
                if (look.Tick) dl.AddLine(new Vector2(c.X, p.Y), new Vector2(c.X, p.Y + RowHeight), col);
            }
            float endX = p.X + (it.Kind == TimelineTrackItemKind.Segment ? Math.Max(s.X1, s.X0 + 2f) : s.X0);
            _trackHits.Add(new TrackItemHit(track.Id, it.Id, p.X + s.X0 + (it.Kind == TimelineTrackItemKind.Segment ? 2f : 0f), cy, endX));
            if (track.ConnectsFrom(it.Id))   // ⭐ CE-4221 X4 — the small connect handle at the item's right end
            {
                var hmin = new Vector2(endX + (it.Kind == TimelineTrackItemKind.Segment ? 2f : 7f), cy - 5f); var hmax = hmin + new Vector2(6f, 10f);
                dl.AddRectFilled(hmin, hmax, ImGui.IsMouseHoveringRect(hmin, hmax) ? 0xFFFFFFFF : 0xFFE0B070, 2f);
                _handles.Add((track.Id, it.Id, hmin, hmax));
            }
        }
        dl.PopClipRect();

        var io = ImGui.GetIO();
        float mx = io.MousePos.X - p.X;
        if (_gesture.Active && ReferenceEquals(_gesture.Track, track) && ImGui.IsMouseDown(ImGuiMouseButton.Left))   // a held drag follows the mouse out of the row too
            _gesture.Update(mx, x => TrackLayout.SimAtX(x, w, width, axis, map, index));
        if (!hovered) return;
        _hoverX = mx;
        int hit = drawable ? TrackLayout.HitTest(items, w, width, axis, map, mx) : -1;
        var part = hit >= 0 ? TrackLayout.HitPart(items[hit], w, width, axis, map, mx) : TrackItemPart.Body;
        if (part != TrackItemPart.Body) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        if (!drawable)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("These items are in sim time — this host's wall axis has no time map to place them on.");
            ImGui.EndTooltip();
        }
        else if (hit >= 0)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(items[hit].Label);
            ImGui.EndTooltip();
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && HandleUnder(track, io.MousePos) is { } grabbed)   // ⭐ CE-4221 X4 — a press on a connect handle starts a CONNECT drag, not an item drag
        {
            _connect = (track, grabbed.ItemId, (grabbed.Min + grabbed.Max) / 2f);
        }
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))   // §2c row 2: a click SELECTS, a drag moves, empty space does nothing — ⛔ never a seek
        {
            _gesture.Press(track, hit >= 0 ? items[hit] : null, mx,
                hit >= 0 && TrackLayout.SpanOf(items[hit], w, width, axis, map) is { } sp ? (part == TrackItemPart.EndEdge ? sp.X1 : sp.X0) : mx, part);
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            if (hit >= 0) track.Select(items[hit].Id);
            _trackMenu.Clear();
            _trackMenu.AddRange(track.MenuItems(hit >= 0 ? items[hit].Id : null, TrackLayout.SimAtX(mx, w, width, axis, map) ?? 0d));
            OpenMenu(index, cursor, null, w.TicksAt(mx, width), null, _trackMenu);
        }
    }

    private (string TrackId, string ItemId, Vector2 Min, Vector2 Max)? HandleUnder(ITimelineTrack track, Vector2 mouse)
    {
        foreach (var h in _handles)
            if (h.TrackId == track.Id && mouse.X >= h.Min.X - 2f && mouse.X <= h.Max.X + 2f && mouse.Y >= h.Min.Y && mouse.Y <= h.Max.Y) return h;
        return null;
    }

    /// <summary>⭐ CE-4221 X1 — every link whose two ends were drawn this frame, as an elbow with an arrowhead.</summary>
    private void DrawConnectors(ImDrawListPtr dl, IEnumerable<ITimelineTrack> shown)
    {
        _connectors.AddRange(TrackConnectors.Resolve(shown, _trackHits));
        foreach (var c in _connectors)
        {
            uint col = c.Colour ?? 0xFFE0E0E0;
            var path = TrackConnectors.Elbow(c.From, c.To, maxX: _rowRight);
            for (int i = 0; i + 1 < path.Length; i++) dl.AddLine(path[i], path[i + 1], col, 1.5f);
            var a = TrackConnectors.Arrow(path);
            dl.AddTriangleFilled(a[0], a[1], a[2], col);
        }
    }

    /// <summary>⭐ CE-4221 X4 — the connect drag: a line from the handle to the mouse; a row that answers <c>CanConnect</c> is outlined; the release over one calls <c>Connect</c> (once). Nothing happens on a track that does not opt in.</summary>
    private void DrawConnectDrag(ImDrawListPtr dl, ImGuiIOPtr io)
    {
        if (_connect is not { } c) return;
        ITimelineTrack? target = null;
        foreach (var (t, y0, y1) in _rowBands)
            if (!ReferenceEquals(t, c.Track) && io.MousePos.Y >= y0 && io.MousePos.Y < y1) { target = t; break; }
        bool ok = target != null && c.Track.CanConnect(c.ItemId, target);
        dl.AddLine(c.Start, io.MousePos, ok ? 0xFF60E060 : 0xFFE0B070, 2f);
        if (ok)
            foreach (var (t, y0, y1) in _rowBands)
                if (ReferenceEquals(t, target)) dl.AddRect(new Vector2(ImGui.GetWindowPos().X, y0), new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth(), y1), 0xFF60E060);
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _connect = null;
            if (ok) c.Track.Connect(c.ItemId, target!);
        }
        else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _connect = null;
    }

    /// <summary>⭐ CE-4221 X5 — the strip below the lanes.</summary>
    private void DrawComingUp()
    {
        ImGui.Separator();
        Plain("Coming up", Dim);
        if (_comingUp.Count == 0) { Plain("nothing expected", Dim); return; }
        foreach (var e in _comingUp)
            ImGui.TextUnformatted($"{(int)(e.At / 60)}:{(int)(e.At % 60):00}  {(e.TrackLabel.Length > 0 ? e.TrackLabel + "  " : "")}{e.ItemLabel}");
    }

    private static void DrawKeyDiamond(ImDrawListPtr dl, Vector2 c, uint col, bool hollow)
    {
        var top = c + new Vector2(0, -5); var right = c + new Vector2(5, 0); var bottom = c + new Vector2(0, 5); var left = c + new Vector2(-5, 0);
        if (!hollow) { dl.AddQuadFilled(top, right, bottom, left, col); return; }
        dl.AddLine(top, right, col); dl.AddLine(right, bottom, col); dl.AddLine(bottom, left, col); dl.AddLine(left, top, col);   // ⭐ X2 Expected: an outline, nothing inside
    }

    /// <summary>
    /// A dashed outline. ⭐ CE-4223 — only the part inside [<paramref name="clipX0"/>, <paramref name="clipX1"/>] (the row) is dashed: an item whose end is far outside the window (an expected stretch to a deadline an hour away) used to be dashed over its WHOLE
    /// length — hundreds of thousands of vertices in one draw list, and ImGui asserts at 65 536 (16-bit indices) and the editor dies. A side that is clipped away has no vertical edge.
    /// </summary>
    private static void DashedRect(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col, float clipX0 = float.NegativeInfinity, float clipX1 = float.PositiveInfinity)
    {
        float lo = Math.Max(a.X, clipX0), hi = Math.Min(b.X, clipX1);
        if (hi <= lo) return;
        // a budget, not a fixed pitch: at most ~40 dashes an edge however wide the item (a row of 1 000 px used to be 140 of them, and a few dozen such rows fill a 16-bit draw list)
        float step = Math.Max(7f, (hi - lo) / 40f), dash = step * 0.57f;
        foreach (var (x0, x1) in TrackLayout.Dashes(lo, hi, dash, step - dash)) { dl.AddLine(new Vector2(x0, a.Y), new Vector2(x1, a.Y), col); dl.AddLine(new Vector2(x0, b.Y), new Vector2(x1, b.Y), col); }
        foreach (var (y0, y1) in TrackLayout.Dashes(a.Y, b.Y))
        {
            if (a.X >= clipX0) dl.AddLine(new Vector2(a.X, y0), new Vector2(a.X, y1), col);
            if (b.X <= clipX1) dl.AddLine(new Vector2(b.X, y0), new Vector2(b.X, y1), col);
        }
    }

    /// <summary>⭐ CE-4167 X3 — a tree row's label column: indent by depth, the expand arrow (a click toggles the node), the icon, the text and the status right-aligned in its colour.</summary>
    private void DrawHeader(ImDrawListPtr dl, ITimelineTrack track, TrackHeader h)
    {
        var o = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(_labelWidth, RowHeight));
        bool collapsed = _collapsed.Contains(track.Id);
        var plan = TrackLayout.PlanHeader(h, _labelWidth, ImGui.CalcTextSize(h.Text).X, h.Status is { Length: > 0 } st ? ImGui.CalcTextSize(st).X : 0f);
        float ty = o.Y + (RowHeight - ImGui.GetTextLineHeight()) / 2f;
        dl.PushClipRect(o, o + new Vector2(_labelWidth, RowHeight), true);
        if (plan.ArrowX is { } ax) dl.AddText(new Vector2(o.X + ax, ty), 0xFFC0C0C0, collapsed ? ">" : "v");
        if (plan.IconX is { } ix && h.Icon != null) dl.AddText(new Vector2(o.X + ix, ty), 0xFFE0C080, h.Icon);
        dl.AddText(new Vector2(o.X + plan.TextX, ty), 0xFFA0A0A0, h.Text);
        float trailW = plan.TrailX != null && h.TrailingGlyph != null ? ImGui.CalcTextSize(h.TrailingGlyph).X : 0f;
        if (plan.TrailX is { } tx && h.TrailingGlyph != null) dl.AddText(new Vector2(o.X + tx, ty), 0xFFC0C0C0, h.TrailingGlyph);
        if (plan.StatusX is { } sx && h.Status != null) dl.AddText(new Vector2(o.X + sx, ty), h.StatusColour ?? 0xFFA0A0A0, h.Status);
        dl.PopClipRect();
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && ImGui.GetIO().MousePos is var m && m.X >= o.X && m.X < o.X + _labelWidth && m.Y >= o.Y && m.Y < o.Y + RowHeight)
        {
            bool onArrow = plan.ArrowX is { } arrowX && m.X < o.X + arrowX + 14f;
            bool onTrail = plan.TrailX is { } tlx && m.X >= o.X + tlx - 2f && m.X < o.X + tlx + trailW + 2f;
            if (onTrail && h.TrailingClick != null) h.TrailingClick();   // ⭐ the header's trailing button (the replica watch's gear)
            else if (h.Collapsible && onArrow) SetCollapsed(track.Id, !collapsed);   // the arrow folds the node …
            else track.SelectRow();   // … a click elsewhere on the header picks the row (⭐ CE-4216)
        }
    }

    // ── what is drawn over the lanes ─────────────────────────────────────────────────────────────────────────

    private void DrawOverlays(ImDrawListPtr dl, TimelineIndex index, float x0, float width, float top, float bottom, long now)
    {
        var w = _view.Window;
        // ⭐ §10b "unavailable zones": before the index's first frame (a late joiner) and the part not scanned yet
        if (index.FrameTicks.Count > 0)
        {
            float xf = w.XOf(index.FrameTicks[0], width);
            if (xf > 0) dl.AddRectFilled(new Vector2(x0, top), new Vector2(x0 + Math.Min(xf, width), bottom), 0x40000000);
            if (!index.IsComplete)
            {
                float xs = Math.Max(0f, w.XOf(index.FrameTicks[^1], width));
                if (xs < width) dl.AddRectFilled(new Vector2(x0 + xs, top), new Vector2(x0 + width, bottom), 0x40000000);
            }
        }
        if (_view.Range is { } r && r.To > r.From)   // the selected range, with its duration
        {
            float a = Math.Clamp(w.XOf(r.From, width), 0, width), b = Math.Clamp(w.XOf(r.To, width), 0, width);
            dl.AddRectFilled(new Vector2(x0 + a, top), new Vector2(x0 + b, bottom), 0x30FFD080);
            string len = TimelineRuler.Label(r.To - r.From, TimeSpan.TicksPerSecond / 10, r.To - r.From);
            dl.AddText(new Vector2(x0 + a + 3, top + 1), 0xFFFFD080, len);
        }
        if (_loop is { } lq && lq.To > lq.From)   // ⭐ CE-3330 E5 — the loop's tinted band over the lanes
        {
            float a = Math.Clamp(w.XOf(lq.From, width), 0, width), b = Math.Clamp(w.XOf(lq.To, width), 0, width);
            dl.AddRectFilled(new Vector2(x0 + a, top), new Vector2(x0 + b, bottom), 0x2860D860);
        }
        // ⭐ CE-3330 T13 (R10) — the paused stretches: shaded spans on the Wall axis (darker = with edits); on Sim each is one instant
        //   (its ◯ / ● tick sits on the ruler) and the events inside it cluster there by themselves (addendum 10)
        if (_axis is { IsSim: false } && _timeMap != null)
            foreach (var pz in _axis.Pauses(_timeMap))
            {
                float a = Math.Clamp(w.XOf(pz.From, width), 0, width), b = Math.Clamp(w.XOf(pz.To, width), 0, width);
                if (b > a) dl.AddRectFilled(new Vector2(x0 + a, top), new Vector2(x0 + b, bottom), pz.Changed ? 0x40804020u : 0x28804020u);
            }
        if (_livePoint is { } lp)   // ⭐ CE-3330 E2 — a review's live point: where the live run stopped
        {
            float xl = w.XOf(lp, width);
            if (xl >= 0 && xl <= width)
            {
                dl.AddLine(new Vector2(x0 + xl, top), new Vector2(x0 + xl, bottom), 0xFF40C0FF, 1.5f);
                dl.AddText(new Vector2(x0 + xl + 3, top + 1), 0xFF40C0FF, "live");
            }
        }
        float xn = w.XOf(now, width);
        if (xn >= 0 && xn <= width) dl.AddLine(new Vector2(x0 + xn, top), new Vector2(x0 + xn, bottom), 0xFF4040FF, 1.5f);
        if (_hoverX >= 0 && _hoverX <= width)   // ⭐ §10b hover readout: a thin line and the time at the mouse
        {
            dl.AddLine(new Vector2(x0 + _hoverX, top), new Vector2(x0 + _hoverX, bottom), 0x60FFFFFF);
            long at = TimelineLayout.SnapToFrame(index, w.TicksAt(_hoverX, width));
            string text = TimelineRuler.Label(at - _view.ExtentFrom, TimelineRuler.MajorStep(w, width), _view.ExtentTo - _view.ExtentFrom,
                TimelineLayout.FrameSpacing(index) is var f && TimelineRuler.MajorStep(w, width) <= 2 * f ? f : 0);
            dl.AddText(new Vector2(x0 + _hoverX + 3, bottom - ImGui.GetTextLineHeight() - 1), 0xC0FFFFFF, text);
        }
    }

    // ── input that applies over the whole panel ──────────────────────────────────────────────────────────────

    /// <summary>⭐ B-10 K4 — the tracks of the last frame, for the key rails (no ImGui frame is needed to drive <see cref="Keys"/> and <see cref="PanFromInput"/>).</summary>
    internal IReadOnlyList<ITimelineTrack>? TracksNow { set => _tracksNow = value; }

    /// <summary>⭐ B-10 K4 — the wheel's zoom this frame: the plain wheel's row, or Ctrl+wheel's (the more specific row shadows the plain one when Ctrl is held, so the two never add up).</summary>
    internal float WheelZoom() => _keys.Axis(TimelineKeys.ZoomWheel) + _keys.Axis(TimelineKeys.ZoomWheelAnywhere);

    private void PanAnywhere(float width)
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) return;
        PanFromInput(width);
    }

    /// <summary>
    /// ⭐ B-10 K4 — the pan the input asks for: the horizontal wheel, else Shift + the wheel (never with Ctrl — that zooms); the pan button held and moved. The hover is the caller's (ImGui's window state).
    /// </summary>
    internal void PanFromInput(float width)
    {
        float h = _keys.Axis(TimelineKeys.PanWheelH);
        if (h == 0f && (_keys.Input!.Modifiers & NodeEditor.Primitives.KeyModifiers.Ctrl) == 0) h = _keys.Axis(TimelineKeys.PanWheel);   // ⭐ T6
        if (h != 0) _view.Pan(h * 40f, width);
        if (!_keys.Held(TimelineKeys.PanDrag)) { _panDragPx = 0f; return; }
        float dx = _keys.Axis(TimelineKeys.PanDragX);
        _panDragPx += Math.Abs(dx);
        if (_panDragPx >= 2f && dx != 0) _view.Pan(dx, width);   // dragging pans (a click that moves under 2 px is not a drag)
    }

    /// <summary>⭐ §10b / R-4087 addendum 2 — the keymap, only while the panel is focused (⛔ never stolen from the map).</summary>
    internal void Keys(TimelineIndex index, string[] lanes, float width, long now, ITimelineCursor? cursor)
    {
        var k = _keys;
        var mods = k.Input!.Modifiers;
        bool ctrl = (mods & NodeEditor.Primitives.KeyModifiers.Ctrl) != 0, alt = (mods & NodeEditor.Primitives.KeyModifiers.Alt) != 0;
        bool seek = cursor is { CanSeek: true };
        if (_tracksNow is { Count: > 0 } && ctrl && !alt)   // ⭐ CE-1364 T3 — undo / redo of a track edit
        {
            if (k.Pressed(TimelineKeys.Redo, allowRepeat: true)) Redo?.Invoke();
            else if (k.Pressed(TimelineKeys.Undo, allowRepeat: true)) Undo?.Invoke();
        }
        if (_tracksNow is { Count: > 0 } tracks && _axis != null && !ctrl && !alt &&   // ⭐ CE-1364 T2 — K / J / L go to the selected item's track first
            TrackLayout.SimSeconds(now, _axis, _timeMap) is { } playheadSim)
        {
            if (k.Pressed(TimelineKeys.OfferK)) TrackLayout.OfferKey(tracks, 'K', playheadSim);
            if (k.Pressed(TimelineKeys.OfferJ)) TrackLayout.OfferKey(tracks, 'J', playheadSim);
            if (k.Pressed(TimelineKeys.OfferL)) TrackLayout.OfferKey(tracks, 'L', playheadSim);
        }
        if (k.Pressed(TimelineKeys.Play) && cursor is { CanPlay: true }) cursor.TogglePlay();
        if (k.Pressed(TimelineKeys.Fit)) _view.Fit();
        for (int i = 0; i < Presets.Length; i++)
            if (k.Pressed(TimelineKeys.Preset(i))) { if (Presets[i] == 0) _view.Fit(); else _view.Last(Presets[i]); }
        if (k.Pressed(TimelineKeys.Back)) _view.Back();
        if (k.Pressed(TimelineKeys.ZoomIn, allowRepeat: true)) _view.Zoom(width / 2, width, 0.8);
        if (k.Pressed(TimelineKeys.ZoomOut, allowRepeat: true)) _view.Zoom(width / 2, width, 1.25);
        if (k.Pressed(TimelineKeys.RangeIn)) _view.Range = (now, Math.Max(now, _view.Range?.To ?? now));
        if (k.Pressed(TimelineKeys.RangeOut)) _view.Range = (Math.Min(now, _view.Range?.From ?? now), now);
        if (k.Pressed(TimelineKeys.RangeClear)) _view.Range = null;
        if (index.FrameTicks.Count == 0) return;

        bool eventBack = k.Pressed(TimelineKeys.EventPrevious, allowRepeat: true), eventForward = k.Pressed(TimelineKeys.EventNext, allowRepeat: true);
        bool left = eventBack || k.Pressed(TimelineKeys.StepBack, allowRepeat: true), right = eventForward || k.Pressed(TimelineKeys.StepForward, allowRepeat: true);
        if (left || right)
        {
            int frame = _selected is { } s && !seek ? s.Frame : index.FrameAt(now);
            if (eventBack || eventForward || !seek)
            {
                // Shift+←/→ — the previous / next event; ⭐ on a LIVE host (no seek) the arrows move the SELECTION, never the time
                var e = TimelineLayout.Adjacent(index, lanes, frame, forward: right);
                if (e is { } ev)
                {
                    if (seek) cursor!.Seek(index.FrameTicks[ev.Frame]); else Select(ev);
                    _view.KeepInView(index.FrameTicks[ev.Frame], playing: true);
                }
            }
            else
            {
                int f = Math.Clamp(frame + (right ? 1 : -1), 0, index.FrameTicks.Count - 1);
                cursor!.Seek(index.FrameTicks[f]);
            }
        }
        if (seek && k.Pressed(TimelineKeys.First)) { cursor!.Seek(index.FrameTicks[0]); _view.Reveal(index.FrameTicks[0]); }
        if (seek && k.Pressed(TimelineKeys.Last)) { cursor!.Seek(index.FrameTicks[^1]); _view.Reveal(index.FrameTicks[^1]); }
    }

    // ── the right-click menu ─────────────────────────────────────────────────────────────────────────────────

    private void OpenMenu(TimelineIndex index, ITimelineCursor? cursor, string? lane, long ticks, TimelineEvent? ev, IEnumerable<TimelineMenuItem>? extra = null)
    {
        _menu.Clear();
        long at = TimelineLayout.SnapToFrame(index, ticks);
        int frame = index.FrameTicks.Count > 0 ? index.FrameAt(at) : 0;
        string time = TimelineRuler.Label(at - _view.ExtentFrom, TimeSpan.TicksPerSecond / 10, _view.ExtentTo - _view.ExtentFrom);
        _menu.AddRange(MenuItems(index, cursor, lane, at, frame, ev, time, _view, _nameOf));
        if (RowMenu != null) _menu.AddRange(RowMenu(new TimelineMenuContext(lane, at, frame, ev)));
        if (EventMenu != null && ev != null) _menu.AddRange(EventMenu(index, new TimelineMenuContext(lane, at, frame, ev)));   // ⭐ CE-4059 — e.g. the director's "Draft a shot sequence here"
        if (extra != null) _menu.InsertRange(0, extra);   // ⭐ CE-1364 T2 — a track's own items lead its menu
        ImGui.OpenPopup("##tl-menu");
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> (DAR §9b) — the MODE's items: where the cursor seeks (a review, a replay, the Replay Browser) "Seek here", "Play from
    /// here", "Seek to this event", the previous / next event in the lane (in a review also "Seek to the live point"); a range's zoom / clear;
    /// always "Copy the time". ⭐ A live host that <c>CanReview</c> offers "Review from here" / "from this event" instead of any seek (§2e). Pure — a rail lists it.
    /// </summary>
    public static List<TimelineMenuItem> MenuItems(TimelineIndex index, ITimelineCursor? cursor, string? lane, long at, int frame,
        TimelineEvent? ev, string time, TimelineView view, Func<string, string>? nameOf = null)
    {
        var items = new List<TimelineMenuItem>();
        var t = index.FrameTicks;
        if (cursor is { Mode: TimelineMode.Live, CanReview: true, ComposesReviewItems: false })   // ⭐ CE-3330 E2 (R-3059) — a live host seeks by ENTERING A REVIEW; ⭐ A6: unless the host composes it
        {
            // ⭐ AQ108 D17 — the user's word is DAR (During Action Review); the code keeps EnterReview
            items.Add(new TimelineMenuItem($"{Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft} DAR from {time}", () => cursor.EnterReview(at)));
            if (ev is { } re && re.Frame < t.Count)
                items.Add(new TimelineMenuItem($"{Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft} DAR from this event ({nameOf?.Invoke(re.Lane) ?? re.Lane})", () => cursor.EnterReview(t[re.Frame])));
        }
        if (cursor != null)   // ⭐ CE-4097 part two (D21) — AAR from here · Go live from here · Take checkpoint here; ⭐ A6 — with the clicked event, for the host's composed DAR rows
            items.AddRange(cursor.ModeMenuItems(at, time, ev is { } ce && ce.Frame < t.Count ? t[ce.Frame] : null, ev is { } le ? nameOf?.Invoke(le.Lane) ?? le.Lane : null));
        if (cursor is { CanSeek: true })
        {
            items.Add(new TimelineMenuItem($"Seek here ({time})", () => cursor.Seek(at)));
            if (cursor.CanPlay) items.Add(new TimelineMenuItem("Play from here", () => { cursor.Seek(at); if (!cursor.IsPlaying) cursor.TogglePlay(); }));
            if (ev is { } e && e.Frame < t.Count)
                items.Add(new TimelineMenuItem($"Seek to this event ({nameOf?.Invoke(e.Lane) ?? e.Lane})", () => cursor.Seek(t[e.Frame])));
            if (cursor is { Mode: TimelineMode.Review, LivePoint: { } live })
                items.Add(new TimelineMenuItem("⏏ Seek to the live point", () => cursor.Seek(live)));
            if (lane != null)
            {
                var lanes = new[] { lane };
                var prev = TimelineLayout.Adjacent(index, lanes, frame, forward: false);
                var next = TimelineLayout.Adjacent(index, lanes, frame, forward: true);
                items.Add(new TimelineMenuItem("Previous event in this lane", () => { if (prev is { } p) cursor.Seek(t[p.Frame]); }, prev != null));
                items.Add(new TimelineMenuItem("Next event in this lane", () => { if (next is { } n) cursor.Seek(t[n.Frame]); }, next != null));
            }
        }
        if (view.Range is { } r && r.To > r.From)
        {
            items.Add(new TimelineMenuItem("Zoom to the range", view.ZoomToRange));
            items.Add(new TimelineMenuItem("Clear the range", () => view.Range = null));
        }
        if (cursor is { CanLoop: true, Mode: TimelineMode.Review or TimelineMode.Replay })   // ⭐ CE-3330 E5 — the MASTER's loop (never offered in Live / None)
        {
            if (view.Range is { } lr && lr.To > lr.From) items.Add(new TimelineMenuItem("Loop this range", () => cursor.SetLoop(lr.From, lr.To)));
            if (cursor.Loop != null) items.Add(new TimelineMenuItem("Clear the loop", cursor.ClearLoop));
        }
        items.Add(new TimelineMenuItem($"Copy the time ({time})", () => ImGui.SetClipboardText(time)));
        return items;
    }

    private void DrawMenu(TimelineIndex index, ITimelineCursor? cursor)
    {
        if (!ImGui.BeginPopup("##tl-menu")) return;
        foreach (var item in _menu)
            if (ImGui.MenuItem(item.Label, "", false, item.Enabled)) item.Run();
        ImGui.EndPopup();
    }

    // ── drawing helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The lanes grouped by their descriptor's <c>Group</c>, groups in order of first appearance.</summary>
    private static List<(string Group, string[] Lanes)> Groups(IReadOnlyList<TimelineLaneDescriptor> lanes)
    {
        var order = new List<string>();
        var members = new Dictionary<string, List<string>>();
        foreach (var lane in lanes)
        {
            if (!members.TryGetValue(lane.Group, out var list)) { members[lane.Group] = list = new List<string>(); order.Add(lane.Group); }
            list.Add(lane.Id);
        }
        var groups = new List<(string, string[])>(order.Count);
        foreach (var g in order) groups.Add((g, members[g].ToArray()));
        return groups;
    }

    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>⛔ Never <c>ImGui.Text*(string)</c> with data in it: those take a printf FORMAT — 📐 "Indexing… 50%" read garbage after its
    /// trailing <c>%</c> and drew a run of <c>?</c> (the frame rail's screenshot). <c>TextUnformatted</c> takes the text as is.</summary>
    private static void Plain(string text, Vector4 colour)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private void DrawMark(ImDrawListPtr dl, in TimelineMark m, Vector2 p, float h, float labelRoom)
    {
        uint c = ColorOf(m.Top);
        if (m.Kind == TimelineMarkKind.Density)
        {
            uint a = (uint)(0x40 + 0xBF * m.Intensity) << 24;
            dl.AddRectFilled(p + new Vector2(m.X0, 2), p + new Vector2(Math.Max(m.X1, m.X0 + 1), h - 2), (c & 0x00FFFFFF) | a);
            return;
        }
        var centre = p + new Vector2(m.X0, h / 2);
        dl.AddCircleFilled(centre, m.Count > 1 ? 4f : 3f, c);
        if (m.Count <= 1) return;
        string label = "×" + m.Count;
        if (ImGui.CalcTextSize(label).X + 5 <= labelRoom) dl.AddText(centre + new Vector2(5, -h / 2), 0xFFE0E0E0, label);
    }
}
