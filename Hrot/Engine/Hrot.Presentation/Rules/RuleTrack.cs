using System;
using System.Collections.Generic;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>What a rules row's track may do — the window's answers: pick the row, ask the rule for something, show its subject on the map, and why asking is blocked now (a recording: null = allowed).</summary>
public sealed record RuleTrackHooks(Action<RuleRow> Select, Action<RuleRow, RuleRequest> Ask, Action<RuleRow> ShowOnMap, string? Blocked,
                                    Func<RuleRow, RuleRow, string?>? CanConnect = null, Action<RuleRow, RuleRow>? Connect = null,
                                    Action<RuleRow>? Edit = null, Action<RuleRow>? ShowHistory = null);   // ⭐ CE-2423 — the editor / the history of a rule, where the host has them

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
        // an authored row (edit mode) has no past: ONE grey key at zero anchors its connectors and its connect handle
        if (row.Authored) _items.Add(new TimelineTrackItem("a", 0d, null, "authored", TimelineTrackItemKind.Key, selected, RulesModel.Grey));
        if (row.ExpectedAt is { } at && at > now)
            _items.Add(new TimelineTrackItem("x", now, at, $"expected by {RulesModel.Clock(at)}", TimelineTrackItemKind.Segment, false, RulesModel.Grey, TimelineTrackItemStyle.Expected));
        // ⭐ CE-4221 (R4) — a deadline is EXPECTED too (a dashed stretch to it) when nothing nearer is; every clock-pinned instant (a deadline, a ScenarioTime condition) is a Pinned diamond
        if (row.Status.Deadline > now && !row.Status.IsFinal && !_items.Exists(x => x.Id == "x"))
            _items.Add(new TimelineTrackItem("x", now, row.Status.Deadline, $"deadline {RulesModel.Clock(row.Status.Deadline)}", TimelineTrackItemKind.Segment, false, RulesModel.Grey, TimelineTrackItemStyle.Expected));
        if (row.Pins is { } pins)
            for (int i = 0; i < pins.Count; i++)
                if (pins[i].At > now) _items.Add(new TimelineTrackItem("p" + i, pins[i].At, null, pins[i].Label, TimelineTrackItemKind.Key, false, RulesModel.Cooldown, TimelineTrackItemStyle.Pinned));
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

    private readonly List<TrackLink> _links = new();
    public IReadOnlyList<TrackLink> Links => _links;
    public void AddLink(TrackLink link) => _links.Add(link);

    /// <summary>The item an arrow LEAVES from: the latest real thing the row did (not a plan).</summary>
    public string? AnchorOut => _items.FindLast(i => i.Style is not (TimelineTrackItemStyle.Expected or TimelineTrackItemStyle.Pinned))?.Id ?? _items.FindLast(_ => true)?.Id;
    /// <summary>The item an arrow ENTERS: the row's first.</summary>
    public string? AnchorIn => _items.Count > 0 ? _items[0].Id : null;

    // ⭐ CE-4221 X4 — drag from the handle of one authored row onto a trigger row (RE-9); the window decides what is allowed
    public bool ConnectsFrom(string itemId) => _row != null && _hooks.Connect != null && _hooks.Blocked == null && itemId == AnchorOut;
    public bool CanConnect(string fromItemId, ITimelineTrack toTrack)
        => _row != null && toTrack is RuleTrack { Row: { } to } && _hooks.CanConnect?.Invoke(_row, to) == null && _hooks.CanConnect != null;
    public void Connect(string fromItemId, ITimelineTrack toTrack)
    {
        if (_row != null && toTrack is RuleTrack { Row: { } to } && _hooks.CanConnect?.Invoke(_row, to) == null) _hooks.Connect?.Invoke(_row, to);
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
        var surface = new RuleActionBinding.Surface(_hooks.Edit == null ? null : () => _hooks.Edit(row), _hooks.ShowHistory == null ? null : () => _hooks.ShowHistory(row));   // ⭐ CE-2423 — the rows are the catalogue's (one declaration with the Rules window's buttons)
        foreach (var r in RuleActionBinding.Rows(row, RuleActionBinding.ModeOf(row, _hooks.Blocked), req => _hooks.Ask(row, req), () => _hooks.ShowOnMap(row), _hooks.Blocked, surface))
            yield return new TimelineMenuItem(r.Shown, r.Run, r.Why == null);
    }
}
