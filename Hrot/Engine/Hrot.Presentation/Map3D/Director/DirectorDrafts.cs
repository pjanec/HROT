using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CE-4059</c> (docs/DESIGN_Director_Timeline_Editor.md §11q D3) — what the director knows of a unit by its NETWORK id (the id the index's events
/// carry), read from the world the host shows: its name (what a take's cameras and anchors name) and its <see cref="SubjectKind"/> (the drafter's height).
/// ⭐ The ONE copy: the editor's <c>UnitNameOf</c> and the Replay Browser's <c>DirectorUnitName</c> were the same twelve lines.
/// </summary>
public static class DirectorUnits
{
    /// <summary>The unit's <see cref="EntityInfo.Name"/>; null when there is no world, no such unit (not spawned yet, gone) or it has no name.</summary>
    public static string? NameOf(EntityRepository? world, long networkId)
    {
        if (Of(world, networkId) is not { } e || !world!.HasComponent<EntityInfo>(e)) return null;
        var name = world.GetComponentRO<EntityInfo>(e).Name.ToString();
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// The unit's kind for the camera: a squad (<c>SquadStateMarker</c>) is a <see cref="SubjectKind.Group"/>; else its body's class, read the ONE way
    /// (<see cref="Locomotions.Of"/>: a descent, its <c>Locomotion</c>, else its <c>VehicleParams</c> preset) — on foot (a man on a rope or under a canopy too) a
    /// <see cref="SubjectKind.Person"/>, a rotorcraft / fixed-wing an <see cref="SubjectKind.Aircraft"/>, wheeled · tracked · amphibious · a vessel a
    /// <see cref="SubjectKind.Vehicle"/>; anything else (no world, no unit, no class) <see cref="SubjectKind.Unknown"/>.
    /// </summary>
    public static SubjectKind KindOf(EntityRepository? world, long networkId)
    {
        if (Of(world, networkId) is not { } e) return SubjectKind.Unknown;
        if (world!.IsComponentTypeRegistered<Fdp.Toolkit.Squad.SquadStateMarker>() && world.HasComponent<Fdp.Toolkit.Squad.SquadStateMarker>(e)) return SubjectKind.Group;
        return Locomotions.Of(world, e) switch
        {
            LocomotionClass.Pedestrian or LocomotionClass.Rope or LocomotionClass.Parachute => SubjectKind.Person,
            LocomotionClass.Rotorcraft or LocomotionClass.FixedWing => SubjectKind.Aircraft,
            LocomotionClass.Wheeled or LocomotionClass.Tracked or LocomotionClass.Amphibious or LocomotionClass.Surface => SubjectKind.Vehicle,
            _ => SubjectKind.Unknown,
        };
    }

    private static Entity? Of(EntityRepository? world, long networkId)
    {
        if (world is null) return null;
        var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, networkId);
        return e.IsNull || !world.IsAlive(e) ? null : e;
    }
}

/// <summary>
/// ⭐⭐ <c>CE-4059</c> P3 (docs/DESIGN_Director_Timeline_Editor.md §11q) — the drafter WIRED, once for every host: the moments of the index the host's Event
/// Timeline shows, a draft of one event opened in the take session (unsaved, a NEW take — §11q D2), and the event menu's <i>"Draft a shot sequence here"</i>.
/// Built by <see cref="DirectorPack.Attach"/>; the editor's <c>/director/moments</c> and <c>/director/draft</c> call the same members.
/// ⛔ No drafting logic here — that is <see cref="Drafter"/>'s; this class only finds the index, the names and the kinds, and opens the result.
/// </summary>
public sealed class DirectorDrafts
{
    /// <summary>The event menu's item.</summary>
    public const string MenuLabel = "Draft a shot sequence here";

    private readonly Func<TimelineIndex?> _index;
    private readonly Func<long, string?> _nameOf;
    private readonly Func<long, SubjectKind> _kindOf;
    private readonly Func<string?> _scenario;
    private readonly DirectorTakeSession _session;

    /// <param name="index">The index the host's Event Timeline shows (null: none yet).</param>
    /// <param name="nameOf">A network id to the unit's name (<see cref="DirectorUnits.NameOf"/> over the host's world).</param>
    /// <param name="kindOf">A network id to the unit's kind (<see cref="DirectorUnits.KindOf"/>).</param>
    /// <param name="scenario">The scenario the shown run belongs to (the editor's loaded one; the recording's) — the draft's take is that scenario's.</param>
    public DirectorDrafts(Func<TimelineIndex?> index, Func<long, string?> nameOf, Func<long, SubjectKind> kindOf, Func<string?> scenario, DirectorTakeSession session)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _nameOf = nameOf ?? throw new ArgumentNullException(nameof(nameOf));
        _kindOf = kindOf ?? throw new ArgumentNullException(nameof(kindOf));
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>The index the drafts read now (null: the host shows none).</summary>
    public TimelineIndex? Index => _index();

    /// <summary>The scenario a draft belongs to now.</summary>
    public string? Scenario => _scenario();

    /// <summary>The candidate moments of the shown index, scored, in time order (<see cref="Drafter.Moments"/>); empty when there is no index.</summary>
    public IReadOnlyList<DraftMoment> Moments() => _index() is { } index ? Drafter.Moments(index, _nameOf) : Array.Empty<DraftMoment>();

    /// <summary>A unit's name by network id (what a moment's actor / subject are called).</summary>
    public string? NameOf(long networkId) => _nameOf(networkId);

    /// <summary>
    /// Drafts event <paramref name="eventIndex"/> of <paramref name="lane"/> of <paramref name="index"/> (the shown index when null) and OPENS the draft in the
    /// take session — a new, unsaved take named <c>draft</c> (<c>draft-2</c>, … when the scenario has one already). The take, or the reasons it was not opened.
    /// </summary>
    public (Take? Take, IReadOnlyList<string> Problems) Draft(string lane, int eventIndex, bool replay = true, TimelineIndex? index = null)
    {
        index ??= _index();
        if (index is null) return (null, new[] { "this host shows no timeline index to draft from" });
        if (string.IsNullOrWhiteSpace(_scenario())) return (null, new[] { "no scenario is shown (load a scenario, or open a recording that names one)" });
        if (!Drafter.Lanes.Contains(lane)) return (null, new[] { $"lane '{lane}' cannot be drafted from (the lanes: {string.Join(", ", Drafter.Lanes)})" });
        if (Drafter.MomentAt(index, lane, eventIndex, _nameOf) is not { } moment)
            return (null, new[] { $"event {eventIndex} of '{lane}' is not a moment the drafter can name (no such event, or its unit has no name)" });
        string scenario = _scenario()!;
        // the title names the moment (not the caption: a take's caption is READ AS its subtitle, §11o S8 — it would be on screen for the whole film)
        var take = Drafter.Draft(index, moment, _nameOf, new DraftOptions { Scenario = scenario, Name = FreeName(scenario), Replay = replay, Kind = _kindOf })
                   with { Title = Title(moment) };
        var problems = _session.OpenDraft(take);
        return problems.Count > 0 ? (null, problems) : (take, problems);
    }

    /// <summary>
    /// ⭐ The event menu's items for the event the person right-clicked in the panel's <paramref name="index"/> (<see cref="Fdp.Presentation.Panels.Timeline.TimelinePanel.EventMenu"/>):
    /// <i>"Draft a shot sequence here"</i> on an event of a draftable lane (disabled when the drafter cannot name it); nothing on any other.
    /// </summary>
    public IReadOnlyList<TimelineMenuItem> MenuItems(TimelineIndex index, TimelineMenuContext context)
    {
        if (context.Event is not { } ev || !Drafter.Lanes.Contains(ev.Lane)) return Array.Empty<TimelineMenuItem>();
        if (IndexOf(index, ev) is not { } i) return Array.Empty<TimelineMenuItem>();
        bool can = !string.IsNullOrWhiteSpace(_scenario()) && Drafter.MomentAt(index, ev.Lane, i, _nameOf) != null;
        return new[] { new TimelineMenuItem(MenuLabel, () => Draft(ev.Lane, i, replay: true, index), can) };
    }

    /// <summary>The event's position in its lane (the lane is time-ordered; the panel hands the event itself).</summary>
    internal static int? IndexOf(TimelineIndex index, TimelineEvent ev)
    {
        var events = index.Lane(ev.Lane);
        for (int i = index.Before(ev.Lane, ev.Frame); i < events.Count && events[i].Frame == ev.Frame; i++)
            if (events[i] == ev) return i;
        return null;
    }

    private string FreeName(string scenario)
    {
        var taken = new HashSet<string>(_session.Store?.TakesOf(scenario) ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (_session.Editor is { } open && open.Take.Scenario == scenario) taken.Add(open.Take.Name);
        if (!taken.Contains("draft")) return "draft";
        int n = 2;
        while (taken.Contains("draft-" + n)) n++;
        return "draft-" + n;
    }

    private string Title(DraftMoment m)
    {
        string what = m.Lane switch { "combat.shot" => "shot", "combat.hit" => "hit", "combat.blast" => "blast", _ => m.Lane };
        string? actor = _nameOf(m.Actor), subject = m.Subject is { } s ? _nameOf(s) : null;
        return actor is null ? $"Draft: {what} at {m.Sim:0.#} s" : subject is null ? $"Draft: {actor}, {what}" : $"Draft: {actor}, {what}, {subject}";
    }
}
