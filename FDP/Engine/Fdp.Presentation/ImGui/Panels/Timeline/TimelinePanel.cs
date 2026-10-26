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
    private const float LabelWidth = 84f, RowHeight = 16f, OverviewHeight = 14f, RulerHeight = 18f, EdgeGrabPx = 4f;

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

    private enum Drag : byte { None, Pan, Range, Bracket, BracketFrom, BracketTo, Scrub }
    private Drag _drag;
    private long _dragAnchor;     // a range's first edge / the bracket's grab offset
    private long _scrubTo;
    private float _hoverX = -1f;  // the mouse over the lanes / ruler, in panel x (−1 = elsewhere)
    private TimelineEvent? _selected;

    /// <summary>The visible span (ticks). Exposed for tests and hosts that want to restore it.</summary>
    public TimelineWindow Window => _view.Window;

    /// <summary>⭐ <c>CE-3330</c> — the view state (follow, range, history) — the viewer's own (§10b).</summary>
    public TimelineView View => _view;

    /// <summary>⭐ <c>CE-3330</c> (R-4087 addendum 5) — a left click on an event selects it: the host turns it into the ONE selection
    /// (<c>SelectionChangeRequest</c>, as <c>POST /selection</c>). ⛔ Never a seek, never a private selection here.</summary>
    public Action<TimelineEvent>? OnSelect { get; set; }

    /// <summary>⭐ <c>CE-3330</c> (addendum 4) — the items the row under the mouse contributes (the host's <c>TimelineLaneCatalog.MenuItems</c>).</summary>
    public Func<TimelineMenuContext, IReadOnlyList<TimelineMenuItem>>? RowMenu { get; set; }

    /// <summary>The event the user selected last (drawn ringed), or null.</summary>
    public TimelineEvent? Selected => _selected;

    /// <summary>Shows <paramref name="window"/> (clamped to the extent) — a host zooming to an event, a rail, R1-d's jump.</summary>
    public void Show(TimelineWindow window) => _view.Show(window);

    public TimelinePanel()
    {
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
        var index = source?.Index ?? TimelineIndex.Empty;
        long from = cursor?.ExtentFrom ?? (index.FrameTicks.Count > 0 ? index.FrameTicks[0] : 0);
        long to = cursor?.ExtentTo ?? (index.FrameTicks.Count > 0 ? index.FrameTicks[^1] : 0);
        if (to <= from)
        {
            Plain(source == null ? "No recording." : "Indexing...", Dim);
            return;
        }
        long spacing = TimelineLayout.FrameSpacing(index);
        if (spacing > 0) _view.MinSpan = spacing * TimelineView.MinSpanFrames;
        _view.OnExtent(from, to);   // ⭐ T1 — kept, not reset
        long now = cursor?.Now ?? from;
        _view.KeepInView(now, cursor?.IsPlaying ?? false);   // ⭐ T12

        if (source?.Error is { } error) Plain($"Index stopped at {source.Progress * 100f:0}%: {error}", new Vector4(1f, 0.5f, 0.4f, 1f));
        else if (source is { IsComplete: false }) Plain($"Indexing... {source.Progress * 100f:0}%", Dim);

        var io = ImGui.GetIO();
        var dl = ImGui.GetWindowDrawList();
        float width = Math.Max(10f, ImGui.GetContentRegionAvail().X - LabelWidth);
        bool focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _drag != Drag.None) EndDrag(index, cursor);   // ⭐ T10 — wherever it is released
        _hoverX = -1f;

        var all = new string[lanes.Count];
        for (int i = 0; i < all.Length; i++) all[i] = lanes[i].Id;
        float x0 = ImGui.GetCursorScreenPos().X + LabelWidth;
        DrawOverview(dl, index, all, width, now);
        DrawRuler(dl, index, width, now, cursor);

        // ⭐ T11 — the lanes scroll in a child of their own, so the overview and the ruler stay put
        float lanesHeight = Math.Max(RowHeight * 2, ImGui.GetContentRegionAvail().Y);
        ImGui.BeginChild("##tl-lanes", new Vector2(0, lanesHeight), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        var cdl = ImGui.GetWindowDrawList();
        float lanesTop = ImGui.GetCursorScreenPos().Y;
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

        PanAnywhere(io, width);
        if (focused) Keys(io, index, all, width, now, cursor);
        DrawMenu(index, cursor);
    }

    // ── the overview strip ────────────────────────────────────────────────────────────────────────────────────

    private void DrawOverview(ImDrawListPtr dl, TimelineIndex index, string[] allLanes, float width, long now)
    {
        ImGui.Dummy(new Vector2(LabelWidth, OverviewHeight));
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
        if (hovered && io.MouseWheel != 0)   // ⭐ T4 — the wheel zooms over the overview too, about the tick under the mouse
            _view.Zoom(_view.Window.XOf(all.TicksAt(mx, width), width), width, TimelineView.WheelFactor(io.MouseWheel));
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

    // ── the ruler ────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawRuler(ImDrawListPtr dl, TimelineIndex index, float width, long now, ITimelineCursor? cursor)
    {
        ImGui.Dummy(new Vector2(LabelWidth, RulerHeight));
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

        // the playhead ▼ (a handle where the cursor seeks), or an arrow at the edge it left by (§10b)
        float xn = w.XOf(_drag == Drag.Scrub ? _scrubTo : now, width);
        bool canSeek = cursor is { CanSeek: true };
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
        if (!hovered) return;
        _hoverX = mx;
        if (io.MouseWheel != 0 && !io.KeyShift)   // ⭐ T4 / T5 — the plain wheel zooms over the ruler, proportional to the delta
            _view.Zoom(mx, width, TimelineView.WheelFactor(io.MouseWheel));
        bool offscreenArrow = (xn < 0 && mx <= 10) || (xn > width && mx >= width - 10);
        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { _view.Fit(); _drag = Drag.None; return; }   // fit all
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

    private void DragUpdate(ImGuiIOPtr io, TimelineWindow w, float width, float mx, TimelineIndex index)
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;
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
            ImGui.SameLine(LabelWidth);
        }
        else ImGui.SameLine(LabelWidth);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-row" + string.Join("-", lanes), new Vector2(width, RowHeight),
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        dl.AddRectFilled(p, p + new Vector2(width, RowHeight), 0x18FFFFFF);
        var w = _view.Window;
        TimelineLayout.Marks(index, lanes, w, width, _priorityOf, into: _marks);
        for (int i = 0; i < _marks.Count; i++)   // a count label only where it fits before the next mark
            DrawMark(dl, _marks[i], p, RowHeight, (i + 1 < _marks.Count ? _marks[i + 1].X0 : width) - _marks[i].X0 - 6f);
        if (_selected is { } sel && Array.IndexOf(lanes, sel.Lane) >= 0 && sel.Frame < index.FrameTicks.Count)
        {
            float xs = w.XOf(index.FrameTicks[sel.Frame], width);
            if (xs >= 0 && xs <= width) dl.AddCircle(p + new Vector2(xs, RowHeight / 2), 6f, 0xFFFFFFFF, 12, 1.5f);
        }
        if (!hovered) return;

        var io = ImGui.GetIO();
        float mx = io.MousePos.X - p.X;
        _hoverX = mx;
        if (io.KeyCtrl && io.MouseWheel != 0)   // Ctrl+wheel zooms anywhere; the plain wheel scrolls the lanes (the child's own)
            _view.Zoom(mx, width, TimelineView.WheelFactor(io.MouseWheel));
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

    private void Select(TimelineEvent e)
    {
        _selected = e;
        OnSelect?.Invoke(e);
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

    private void PanAnywhere(ImGuiIOPtr io, float width)
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) return;
        float h = io.MouseWheelH != 0 ? io.MouseWheelH : io.KeyShift && !io.KeyCtrl ? io.MouseWheel : 0f;   // ⭐ T6
        if (h != 0) _view.Pan(h * 40f, width);
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 2f) && io.MouseDelta.X != 0) _view.Pan(io.MouseDelta.X, width);   // middle drag pans
    }

    /// <summary>⭐ §10b / R-4087 addendum 2 — the keymap, only while the panel is focused (⛔ never stolen from the map).</summary>
    private void Keys(ImGuiIOPtr io, TimelineIndex index, string[] lanes, float width, long now, ITimelineCursor? cursor)
    {
        bool seek = cursor is { CanSeek: true };
        if (ImGui.IsKeyPressed(ImGuiKey.Space, false) && cursor is { CanPlay: true }) cursor.TogglePlay();
        if (ImGui.IsKeyPressed(ImGuiKey.F, false)) _view.Fit();
        for (int i = 0; i < Presets.Length; i++)
            if (ImGui.IsKeyPressed(ImGuiKey._0 + i, false)) { if (Presets[i] == 0) _view.Fit(); else _view.Last(Presets[i]); }
        if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false)) _view.Back();
        if (ImGui.IsKeyPressed(ImGuiKey.Equal) || ImGui.IsKeyPressed(ImGuiKey.KeypadAdd)) _view.Zoom(width / 2, width, 0.8);
        if (ImGui.IsKeyPressed(ImGuiKey.Minus) || ImGui.IsKeyPressed(ImGuiKey.KeypadSubtract)) _view.Zoom(width / 2, width, 1.25);
        if (ImGui.IsKeyPressed(ImGuiKey.I, false)) _view.Range = (now, Math.Max(now, _view.Range?.To ?? now));
        if (ImGui.IsKeyPressed(ImGuiKey.O, false)) _view.Range = (Math.Min(now, _view.Range?.From ?? now), now);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false)) _view.Range = null;
        if (index.FrameTicks.Count == 0) return;

        bool left = ImGui.IsKeyPressed(ImGuiKey.LeftArrow), right = ImGui.IsKeyPressed(ImGuiKey.RightArrow);
        if (left || right)
        {
            int frame = _selected is { } s && !seek ? s.Frame : index.FrameAt(now);
            if (io.KeyShift || !seek)
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
        if (seek && ImGui.IsKeyPressed(ImGuiKey.Home, false)) { cursor!.Seek(index.FrameTicks[0]); _view.Reveal(index.FrameTicks[0]); }
        if (seek && ImGui.IsKeyPressed(ImGuiKey.End, false)) { cursor!.Seek(index.FrameTicks[^1]); _view.Reveal(index.FrameTicks[^1]); }
    }

    // ── the right-click menu ─────────────────────────────────────────────────────────────────────────────────

    private void OpenMenu(TimelineIndex index, ITimelineCursor? cursor, string? lane, long ticks, TimelineEvent? ev)
    {
        _menu.Clear();
        long at = TimelineLayout.SnapToFrame(index, ticks);
        int frame = index.FrameTicks.Count > 0 ? index.FrameAt(at) : 0;
        string time = TimelineRuler.Label(at - _view.ExtentFrom, TimeSpan.TicksPerSecond / 10, _view.ExtentTo - _view.ExtentFrom);
        _menu.AddRange(MenuItems(index, cursor, lane, at, frame, ev, time, _view, _nameOf));
        if (RowMenu != null) _menu.AddRange(RowMenu(new TimelineMenuContext(lane, at, frame, ev)));
        ImGui.OpenPopup("##tl-menu");
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> (DAR §9b) — the MODE's items: where the cursor seeks (a review, a replay, the Replay Browser) "Seek here", "Play from
    /// here", "Seek to this event", the previous / next event in the lane; a range's zoom / clear; always "Copy the time". ⏳ A live host's
    /// "Review from here" waits on backend's <c>EnterReview</c> (handoff P1, row 21): until then a live host offers none. Pure — a rail lists it.
    /// </summary>
    public static List<TimelineMenuItem> MenuItems(TimelineIndex index, ITimelineCursor? cursor, string? lane, long at, int frame,
        TimelineEvent? ev, string time, TimelineView view, Func<string, string>? nameOf = null)
    {
        var items = new List<TimelineMenuItem>();
        var t = index.FrameTicks;
        if (cursor is { CanSeek: true })
        {
            items.Add(new TimelineMenuItem($"Seek here ({time})", () => cursor.Seek(at)));
            if (cursor.CanPlay) items.Add(new TimelineMenuItem("Play from here", () => { cursor.Seek(at); if (!cursor.IsPlaying) cursor.TogglePlay(); }));
            if (ev is { } e && e.Frame < t.Count)
                items.Add(new TimelineMenuItem($"Seek to this event ({nameOf?.Invoke(e.Lane) ?? e.Lane})", () => cursor.Seek(t[e.Frame])));
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
