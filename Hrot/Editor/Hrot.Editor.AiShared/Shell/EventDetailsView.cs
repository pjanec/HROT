using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Replication.Components;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ CE-4263 (<c>docs/DESIGN_Director_Timeline_Editor.md</c> §2c-1) — the timeline event the user last CLICKED, held by the host next to the one selection (🔒 user, 2026-11-04: <i>"I would expect the detail panel will show
/// the detail about the clicked event"</i>). A click still selects the event's SUBJECT (R-4087 addendum 5, unchanged); this is the second fact the Details ▸ <see cref="EventDetailsView"/> reads. ⛔ Never cleared by a
/// timer: the view applies only while the focused event's subject IS the one selected unit, so a stale event cannot show.
/// </summary>
public sealed class TimelineEventFocus
{
    /// <summary>The clicked event, or null.</summary>
    public TimelineEvent? Event { get; private set; }
    /// <summary>The event's lane by its display name (the catalogue's), when the host knew it.</summary>
    public string? LaneName { get; private set; }
    /// <summary>The event's time on the timeline's axes as the timeline shows it (<c>h:mm:ss.f</c> from the extent's start; sim and wall), when the host knew them.</summary>
    public string? WallText { get; private set; }
    public string? SimText { get; private set; }

    public void Set(TimelineEvent e, string? laneName = null, string? wallText = null, string? simText = null)
    { Event = e; LaneName = laneName; WallText = wallText; SimText = simText; }

    public void Clear() { Event = null; LaneName = null; WallText = null; SimText = null; }

    /// <summary>
    /// ⭐ The predicate: exactly ONE entity is selected AND it is the focused event's subject (its network id). ⭐ Pure — a rail runs it with no ImGui.
    /// </summary>
    public static bool AppliesTo(DetailsContext context, TimelineEventFocus focus, EntityRepository? world)
    {
        if (focus.Event is not { } ev || ev.Subject <= 0 || !DetailsViewPredicates.ExactlyOneEntity(context) || world is null) return false;
        var entity = context.Entities[0];
        if (!world.IsAlive(entity) || !world.IsComponentTypeRegistered<NetworkIdentity>() || !world.HasComponent<NetworkIdentity>(entity)) return false;
        return world.GetComponentRO<NetworkIdentity>(entity).Value == ev.Subject;
    }
}

/// <summary>One row of the event view: a label, its text, and the network id a click selects (null = not a link).</summary>
public sealed record EventRow(string Label, string Value, int? LinkNetworkId = null);

/// <summary>The Event view's dump — what a frame showed, for <c>GET /panels</c> and the rails.</summary>
public sealed record EventDetailsViewPanelViewModel(
    string PanelId, string PanelKind, bool HasEvent, IReadOnlyList<EventRow> Rows) : Fdp.Diagnostics.Contracts.Panels.IPanelViewModel
{
    public System.Text.Json.Nodes.JsonNode Dump() => Fdp.Diagnostics.Contracts.Panels.PanelDump.Of(this);
}

/// <summary>
/// ⭐ CE-4263 — <b>THE EVENT VIEW</b>: what a clicked timeline event IS — its time (sim and wall), its lane (by name) and kind, a one-line text, the <b>Subject</b> and <b>Other</b> units as links (they select through the host's ONE
/// selection), and its value fields. Ordered FIRST among the entity views (the highest <see cref="EventDetailsViewDescriptor.Rank"/>), applies only to the focused event's subject (<see cref="TimelineEventFocus.AppliesTo"/>).
/// </summary>
public sealed class EventDetailsView : IDetailsViewInstance
{
    private readonly TimelineEventFocus _focus;
    private readonly Action<int> _select;

    public EventDetailsView(TimelineEventFocus focus, Action<int> selectNetworkEntity)
    {
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _select = selectNetworkEntity ?? throw new ArgumentNullException(nameof(selectNetworkEntity));
    }

    /// <summary>⭐ The rows, as pure data (numbers culture-invariant). Empty when no event is focused.</summary>
    public static IReadOnlyList<EventRow> Rows(TimelineEventFocus focus)
    {
        var rows = new List<EventRow>();
        if (focus.Event is not { } e) return rows;
        var inv = CultureInfo.InvariantCulture;
        string lane = focus.LaneName ?? e.Lane;
        if (focus.SimText is { Length: > 0 } sim) rows.Add(new EventRow("Time (sim)", sim));
        if (focus.WallText is { Length: > 0 } wall) rows.Add(new EventRow("Time (wall)", wall));
        rows.Add(new EventRow("Lane", lane));
        rows.Add(new EventRow("Kind", e.Lane));
        rows.Add(new EventRow("Event", Text(lane, e)));
        if (e.Subject > 0) rows.Add(new EventRow("Subject", "unit " + e.Subject.ToString(inv), e.Subject));
        if (e.Other > 0) rows.Add(new EventRow("Other", "unit " + e.Other.ToString(inv), e.Other <= int.MaxValue ? (int)e.Other : null));
        if (e.X != 0f || e.Y != 0f) rows.Add(new EventRow("Position", $"{e.X.ToString("F1", inv)}, {e.Y.ToString("F1", inv)}"));
        if (e.Value != 0f) rows.Add(new EventRow("Value", e.Value.ToString("0.###", inv)));
        rows.Add(new EventRow("Frame", e.Frame.ToString(inv)));
        return rows;
    }

    /// <summary>The event as one line, like the timeline's tooltip: <c>lane  unit 7 → 9</c>.</summary>
    public static string Text(string laneName, TimelineEvent e)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = laneName;
        if (e.Subject != 0) s += "  unit " + e.Subject.ToString(inv);
        if (e.Other != 0) s += " → " + e.Other.ToString(inv);
        return s;
    }

    internal EventDetailsViewPanelViewModel SimulateDraw(string idScope)
    {
        var vm = new EventDetailsViewPanelViewModel($"{idScope}/{EventDetailsViewDescriptor.ViewId}", EventDetailsViewDescriptor.ViewId, _focus.Event is not null, Rows(_focus));
        Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.DeclareInstrumented(vm.PanelId);
        if (Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.CaptureEnabled) Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.Register(vm);
        return vm;
    }

    public void Draw(DetailsContext context, string idScope)
    {
        var rows = SimulateDraw(idScope).Rows;
        if (rows.Count == 0) { ImGui.TextDisabled("No event is selected."); return; }
        ImGui.TextUnformatted("Timeline event");
        ImGui.Separator();
        foreach (var row in rows)
        {
            ImGui.TextUnformatted(row.Label);
            ImGui.SameLine(110f);
            if (row.LinkNetworkId is { } id)
            {
                if (ImGui.SmallButton($"{row.Value}##evlink_{row.Label}{idScope}")) _select(id);   // the host's ONE selection
            }
            else ImGui.TextUnformatted(row.Value);
        }
    }

    public void Dispose() { }
}

/// <summary>⭐ CE-4263 — the Event view's descriptor: the HIGHEST rank (the offer set is rank-DESCENDING and the default is the highest that applies — R-98), so it is first among the entity views; applies to the focused event's subject.</summary>
public static class EventDetailsViewDescriptor
{
    public const string ViewId = "details.event";
    public const int Rank = 100;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, TimelineEventFocus focus, Action<int> selectNetworkEntity)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(focus);
        var instance = new EventDetailsView(focus, selectNetworkEntity);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Event",
            Rank:      Rank,
            AppliesTo: ctx => TimelineEventFocus.AppliesTo(ctx, focus, world()),
            Create:    () => instance);
    }
}
