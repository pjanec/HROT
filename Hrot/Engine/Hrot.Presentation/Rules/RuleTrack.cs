using System;
using System.Collections.Generic;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>What a rules row's track may do — the window's answers: pick the row, ask the rule for something, show its subject on the map, and why asking is blocked now (a recording: null = allowed).</summary>
public sealed record RuleTrackHooks(Action<RuleRow> Select, Action<RuleRow, RuleRequest> Ask, Action<RuleRow> ShowOnMap, string? Blocked);

/// <summary>
/// ⭐⭐ CE-4216 (rules view R2, RV-3 / RV-5; docs/DESIGN_Rules_View_UI.md §2) — ONE row of the rules view as an <see cref="ITimelineTrack"/> over the ONE <c>TimelinePanel</c>: a <see cref="TrackHeader"/> (depth, title, state words in the state's colour, role
/// icon, an expand arrow when it has children), the PAST as Solid / Hatched spans, the fires as Pinned diamonds, and — while it is expected — an Expected stretch to <c>ExpectedAt</c>. A group (macro / subject / side) is the same class with no row: a header only.
/// ⛔ Nothing is edited here: <see cref="Move"/> and <see cref="Key"/> do nothing; the menu ASKS (RV-5).
/// </summary>
public sealed class RuleTrack : ITimelineTrack
{
    private readonly RuleRow? _row;
    private readonly RuleTrackHooks _hooks;
    private readonly bool _selected;
    private readonly List<TimelineTrackItem> _items = new();

    /// <summary>A rule row. <paramref name="selected"/> = it is the window's selected row.</summary>
    public RuleTrack(RuleRow row, int baseDepth, bool selected, RuleTrackHooks hooks, double now)
    {
        _row = row; _hooks = hooks; _selected = selected;
        Id = "r:" + row.NetworkId;
        Label = row.Title;
        Header = new TrackHeader(baseDepth + row.Depth, row.Title, row.StateWords, row.StateColour, row.Icon, Collapsible: row.HasChildren);
        for (int i = 0; i < row.Spans.Count; i++)
        {
            var sp = row.Spans[i];
            _items.Add(new TimelineTrackItem("s" + i, sp.From, sp.To, $"{sp.State} {RulesModel.Clock(sp.From)}–{RulesModel.Clock(sp.To)}", TimelineTrackItemKind.Segment, selected,
                RulesModel.ColourOfSpan(sp), sp.Hatched ? TimelineTrackItemStyle.Hatched : TimelineTrackItemStyle.Solid));
        }
        if (row.ExpectedAt is { } at && at > now)
            _items.Add(new TimelineTrackItem("x", now, at, $"expected by {RulesModel.Clock(at)}", TimelineTrackItemKind.Segment, false, RulesModel.Grey, TimelineTrackItemStyle.Expected));
        for (int i = 0; i < row.Fires.Count; i++)
            _items.Add(new TimelineTrackItem("f" + i, row.Fires[i], null, $"fired {i + 1}× at {RulesModel.Clock(row.Fires[i])}", TimelineTrackItemKind.Key, selected, RulesModel.Acting, TimelineTrackItemStyle.Pinned));
    }

    /// <summary>A group header (a macro, a subject, a side): no items.</summary>
    public RuleTrack(string name, int count, RuleTrackHooks hooks)
    {
        _hooks = hooks;
        Id = "g:" + name;
        Label = name;
        Header = new TrackHeader(0, name, $"{count} rule{(count == 1 ? "" : "s")}", RulesModel.Grey, null, Collapsible: true);
    }

    public string Id { get; }
    public string Label { get; }
    public TrackHeader? Header { get; }
    public IReadOnlyList<TimelineTrackItem> Items => _items;

    /// <summary>The row this track draws; null on a group header.</summary>
    public RuleRow? Row => _row;
    public bool IsSelected => _selected;

    public void Select(string itemId) => SelectRow();
    public void SelectRow() { if (_row != null) _hooks.Select(_row); }
    public void Move(string itemId, double toSimSeconds, bool commit) { }   // ⛔ the past is not edited
    public bool Key(char key, double atSimSeconds) => false;

    public IEnumerable<TimelineMenuItem> MenuItems(string? itemId)
    {
        if (_row == null) yield break;
        var row = _row;
        foreach (var a in RulesModel.ActionsOf(row.Status))
        {
            string? why = _hooks.Blocked ?? a.DisabledReason;
            yield return new TimelineMenuItem(why == null ? a.Label : $"{a.Label} — {why}", () => _hooks.Ask(row, a.Request), why == null);
        }
        yield return new TimelineMenuItem(row.SubjectNetworkId != 0 ? "Show on map" : "Show on map — no subject", () => _hooks.ShowOnMap(row), row.SubjectNetworkId != 0);
    }
}
