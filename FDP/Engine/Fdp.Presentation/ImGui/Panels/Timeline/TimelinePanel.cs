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
/// it — nothing here names a lane. Ctrl+wheel zooms about the mouse, a drag pans, a click seeks through the host's
/// <see cref="ITimelineCursor"/>, hovering a mark lists what it stands for. ⭐ Every rule is <see cref="TimelineLayout"/>'s; this class only
/// draws its marks and turns mouse input into its calls.
/// </summary>
public sealed class TimelinePanel
{
    private const float LabelWidth = 84f, RowHeight = 16f, OverviewHeight = 14f, RulerHeight = 16f;

    /// <summary>Colours for a lane registered without one (ABGR), picked by its id so it stays the same across runs.</summary>
    private static readonly uint[] Palette = { 0xFFE0B070, 0xFF70C0E0, 0xFF90E070, 0xFFE070D0, 0xFF70E0C0, 0xFFC0C0C0 };

    private readonly Dictionary<string, bool> _groupOpen = new();
    private readonly Dictionary<string, TimelineLaneDescriptor> _byId = new();
    private readonly Func<string, int> _priorityOf;
    private readonly Func<string, string> _nameOf;
    private readonly List<TimelineMark> _marks = new();
    private TimelineWindow _window;
    private long _extentFrom, _extentTo;
    private bool _dragging;
    private bool _shown;

    /// <summary>The visible span (ticks). Set by the panel; exposed for tests and hosts that want to restore it.</summary>
    public TimelineWindow Window => _window;

    /// <summary>Shows <paramref name="window"/> (clamped to the extent on the next draw) — a host zooming to an event, a rail, R1-d's jump.</summary>
    public void Show(TimelineWindow window) => (_window, _shown) = (window, true);

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
        if (from != _extentFrom || to != _extentTo || _window.Span <= 1)
        {
            (_extentFrom, _extentTo) = (from, to);
            _window = _shown ? TimelineLayout.Clamp(_window, from, to) : new TimelineWindow(from, to);
        }
        _shown = false;

        if (source?.Error is { } error) Plain($"Index stopped at {source.Progress * 100f:0}%: {error}", new Vector4(1f, 0.5f, 0.4f, 1f));
        else if (source is { IsComplete: false }) Plain($"Indexing... {source.Progress * 100f:0}%", Dim);

        var dl = ImGui.GetWindowDrawList();
        float width = Math.Max(10f, ImGui.GetContentRegionAvail().X - LabelWidth);
        long now = cursor?.Now ?? from;

        var all = new string[lanes.Count];
        for (int i = 0; i < all.Length; i++) all[i] = lanes[i].Id;
        DrawOverview(dl, index, all, width, now, cursor);
        DrawRuler(dl, width);

        float lanesTop = ImGui.GetCursorScreenPos().Y;
        float x0 = ImGui.GetCursorScreenPos().X + LabelWidth;
        foreach (var (group, members) in Groups(lanes))
        {
            bool open = !_groupOpen.TryGetValue(group, out var o) || o;
            if (ImGui.SmallButton((open ? "v " : "> ") + (group.Length == 0 ? "Other" : group) + "##tlg" + group)) _groupOpen[group] = !open;
            if (!open)
            {
                ImGui.SameLine();
                DrawRow(dl, index, members, "", width, cursor);
                continue;
            }
            foreach (var lane in members) DrawRow(dl, index, new[] { lane }, _nameOf(lane), width, cursor);
        }
        float lanesBottom = ImGui.GetCursorScreenPos().Y;

        // The unscanned part (progressive), then "now".
        if (!index.IsComplete && index.FrameTicks.Count > 0)
        {
            float xs = x0 + Math.Max(0f, _window.XOf(index.FrameTicks[^1], width));
            if (xs < x0 + width) dl.AddRectFilled(new Vector2(xs, lanesTop), new Vector2(x0 + width, lanesBottom), 0x40000000);
        }
        float xn = x0 + _window.XOf(now, width);
        if (xn >= x0 && xn <= x0 + width) dl.AddLine(new Vector2(xn, lanesTop), new Vector2(xn, lanesBottom), 0xFF4040FF, 1.5f);
    }

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

    private void DrawOverview(ImDrawListPtr dl, TimelineIndex index, string[] allLanes, float width, long now, ITimelineCursor? cursor)
    {
        ImGui.Dummy(new Vector2(LabelWidth, OverviewHeight));
        ImGui.SameLine(0, 0);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-overview", new Vector2(width, OverviewHeight));
        var all = new TimelineWindow(_extentFrom, _extentTo);
        dl.AddRectFilled(p, p + new Vector2(width, OverviewHeight), 0x30FFFFFF);
        foreach (var m in TimelineLayout.Marks(index, allLanes, all, width, _priorityOf, into: _marks))
            DrawMark(dl, m, p, OverviewHeight, labelRoom: 0);   // the overview shows marks, never counts
        var (b0, b1) = TimelineLayout.Bracket(_window, _extentFrom, _extentTo, width);
        dl.AddRect(p + new Vector2(b0, 0), p + new Vector2(Math.Max(b1, b0 + 2), OverviewHeight), 0xFFFFFFFF);
        float xn = all.XOf(now, width);
        dl.AddLine(p + new Vector2(xn, 0), p + new Vector2(xn, OverviewHeight), 0xFF4040FF);
        if (ImGui.IsItemActive())   // drag the bracket's centre to the mouse
        {
            long centre = all.TicksAt(ImGui.GetIO().MousePos.X - p.X, width);
            long half = _window.Span / 2;
            _window = TimelineLayout.Clamp(new TimelineWindow(centre - half, centre + half), _extentFrom, _extentTo);
        }
    }

    private void DrawRuler(ImDrawListPtr dl, float width)
    {
        ImGui.Dummy(new Vector2(LabelWidth, RulerHeight));
        ImGui.SameLine(0, 0);
        var p = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, RulerHeight));
        long step = TimelineLayout.RulerStep(_window, width);
        long first = _extentFrom + (long)Math.Ceiling((_window.From - _extentFrom) / (double)step) * step;
        for (long t = first; t <= _window.To; t += step)
        {
            float x = _window.XOf(t, width);
            dl.AddLine(p + new Vector2(x, RulerHeight - 5), p + new Vector2(x, RulerHeight), 0xFFA0A0A0);
            var rel = TimeSpan.FromTicks(t - _extentFrom);
            string label = step < TimeSpan.TicksPerSecond ? rel.ToString(@"m\:ss\.f") : rel.TotalHours >= 1 ? rel.ToString(@"h\:mm\:ss") : rel.ToString(@"m\:ss");
            dl.AddText(p + new Vector2(x + 2, 0), 0xFFA0A0A0, label);
        }
    }

    private void DrawRow(ImDrawListPtr dl, TimelineIndex index, string[] lanes, string label, float width, ITimelineCursor? cursor)
    {
        if (label.Length > 0)
        {
            Plain(label, ImGui.ColorConvertU32ToFloat4(ColorOf(lanes[0])));
            ImGui.SameLine(LabelWidth);
        }
        else ImGui.SameLine(LabelWidth);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tl-row" + string.Join("-", lanes), new Vector2(width, RowHeight));
        bool hovered = ImGui.IsItemHovered();
        dl.AddRectFilled(p, p + new Vector2(width, RowHeight), 0x18FFFFFF);
        TimelineLayout.Marks(index, lanes, _window, width, _priorityOf, into: _marks);
        for (int i = 0; i < _marks.Count; i++)   // a count label only where it fits before the next mark
            DrawMark(dl, _marks[i], p, RowHeight, (i + 1 < _marks.Count ? _marks[i + 1].X0 : width) - _marks[i].X0 - 6f);
        if (!hovered) return;

        var io = ImGui.GetIO();
        float mx = io.MousePos.X - p.X;
        if (io.KeyCtrl && io.MouseWheel != 0)
            _window = TimelineLayout.ZoomAbout(_window, mx, width, io.MouseWheel > 0 ? 0.8f : 1.25f, _extentFrom, _extentTo, TimeSpan.TicksPerSecond / 10);
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3f))
        {
            _window = TimelineLayout.Pan(_window, io.MouseDelta.X, width, _extentFrom, _extentTo);
            _dragging = true;
        }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (!_dragging && cursor is { CanSeek: true }) cursor.Seek(_window.TicksAt(mx, width));
            _dragging = false;
        }
        int hit = TimelineLayout.MarkAt(_marks, mx, TimelineLayout.DefaultGlyphPx);
        if (hit >= 0)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(TimelineLayout.Describe(index, lanes, _marks[hit], _extentFrom, _nameOf));
            ImGui.EndTooltip();
        }
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
