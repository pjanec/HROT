using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ PH-2 (docs/DESIGN_Player_HUD_And_Debrief.md) — who "I" am: the possessed unit, its network id, its side (<c>EntityInfo.ForceId</c>) and its team
/// (the network ids of its commander chain, <c>UnitSubordinate.Commander</c> upward). Nothing possessed ⇒ <see cref="IsNone"/> ⇒ no player HUD.
/// </summary>
public readonly record struct PlayerView(Entity Unit, int UnitNetId, ForceId Side, IReadOnlyList<int> Team)
{
    public bool IsNone => UnitNetId == 0;

    /// <summary>The view of <paramref name="unit"/> in <paramref name="w"/> (none when it is null, dead or has no network id).</summary>
    public static PlayerView Of(EntityRepository w, Entity unit)
    {
        if (unit.IsNull || !w.IsAlive(unit) || !w.IsComponentTypeRegistered<NetworkIdentity>()) return default;
        int net = NetworkIdResolver.RuntimeNetworkIdOf(w, unit);
        if (net == 0) return default;
        var side = w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(unit) ? w.GetComponentRO<EntityInfo>(unit).ForceId : ForceId.Neutral;
        var team = new List<int>();
        if (w.IsComponentTypeRegistered<UnitSubordinate>())
            for (var e = unit; w.HasComponent<UnitSubordinate>(e) && team.Count < 8;)
            {
                var boss = w.GetComponentRO<UnitSubordinate>(e).Commander;
                if (boss.IsNull || !w.IsAlive(boss) || boss == unit) break;
                int b = NetworkIdResolver.RuntimeNetworkIdOf(w, boss);
                if (b != 0 && !team.Contains(b)) team.Add(b);
                e = boss;
            }
        return new PlayerView(unit, net, side, team);
    }
}

/// <summary>A mark on an objective line: the draw turns it into a shape (the HUD font has no ✓ ● ○ glyphs).</summary>
public enum HudMark { Done, Active, Waiting, Failed }

/// <summary>One objective on the training HUD. <see cref="Fraction"/> −1 = no bar; <see cref="Detail"/> the words beside the bar.</summary>
public sealed record ObjectiveLine(int NetworkId, string Title, HudMark Mark, ObjectiveState State, bool Required, int Depth, float Fraction, string Detail);

/// <summary>One of "my standards": a measure with its value against target, coloured (0xRRGGBB) by its score against the pass mark.</summary>
public sealed record StandardLine(int NetworkId, string Title, string Text, float Score, float Pass, uint Rgb);

/// <summary>
/// ⭐ CE-4246 (⑨b, PH-3 <c>hud.coaching</c>) — the coaching hint, PURE: the first LEAF that does not hold yet of a live rule ABOUT ME (a trigger whose <c>Subject</c> is my unit, lowest network id first), as
/// <i>label (measured)</i>. <paramref name="leavesOf"/> reads the owner's published <c>RuleLeaves</c> (<see cref="LiveLeafOverlay.Read"/>); the owner publishes them only while the node feature <c>ui.rules-live-values</c> is on, so with it off there is no hint.
/// </summary>
public static class CoachingHint
{
    public static string? Build(IReadOnlyList<RulesReader.Entry> entries, PlayerView view, Func<RulesReader.Entry, IReadOnlyList<LiveLeaf>> leavesOf)
    {
        if (view.IsNone) return null;
        foreach (var e in entries.Where(e => !e.Authored && e.Status.Role == ObjectiveRole.Trigger && e.SubjectNetworkId == view.UnitNetId && e.Status.Closed == 0 && !e.Status.IsFinal).OrderBy(e => e.NetworkId))
            foreach (var l in leavesOf(e))
                if (!l.Holds && l.Label.Length > 0)
                {
                    // ⚠ an OnEntity leaf's label starts with the authored ref (a GUID, cut to 32 chars): no words for a person ⇒ name the RULE instead
                    string words = System.Text.RegularExpressions.Regex.IsMatch(l.Label, @"^[0-9a-fA-F]{8}-") ? $"{e.Status.Title}: not yet" : l.Label;
                    return l.Measured != 0f && l.Measured != 1f ? $"{words} ({l.Measured:0.##})" : words;
                }
        return null;
    }
}

/// <summary>The training HUD this frame: the objectives my side may see and the measures about me (or, when none is mine, my team).</summary>
public sealed record TrainingHudModel(IReadOnlyList<ObjectiveLine> Objectives, IReadOnlyList<StandardLine> Standards, bool StandardsAreTeams);

/// <summary>
/// ⭐ H1 / PH-3 (CE-4226) — the training HUD as DATA, from the ONE reader (<see cref="RulesReader"/>, PH-1): <c>hud.objectives</c> = the objectives visible to my side
/// (<c>VisibleSides</c>, else the rule's own side), required first, then tree order; <c>hud.own-evaluation</c> = the measures whose
/// <c>Subject</c> is me. Pure: the overlay draws it, the rails drive it.
/// </summary>
public static class TrainingHud
{
    public const uint Good = 0x4FBF85, Fair = 0xD9A83F, Poor = 0xE5645A, Neutral = 0xD9DDE2;

    /// <summary>Measure kinds that are aggregates of a score tree, not a standard of one man.</summary>
    public static readonly IReadOnlySet<string> AggregateKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Points", "Best", "SelectionMean" };

    /// <summary>Kinds that belong to the GAME hud (tickets, bounty) — never a mission objective.</summary>
    public static readonly IReadOnlySet<string> GameKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Counter", "Bounty" };

    /// <summary>⭐ W-H3 — a capture point is the status that SAYS so: <c>Capture</c> = 1 (backend G-8, CE-4230). Was inferred from a HoldArea whose reason contained <i>owner</i>.</summary>
    public static bool IsCapturePoint(in ObjectiveStatus s) => s.Capture != 0;
    /// <summary>Kinds that are a conduct standard of the exercise (a negative objective), not a mission objective.</summary>
    public static readonly IReadOnlySet<string> ConductKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NoHarm" };

    /// <param name="sideOf">The side a rule is about (<c>MissionSummaryModel.SideOfRule</c>: its subject's <c>EntityInfo.ForceId</c>, else its Owner). Null ⇒ the status's Owner. ⚠ A running scenario's statuses often carry Owner <i>Neutral</i> (measured on <c>showcase-bridgehead</c>), so the live caller MUST pass the real one.</param>
    public static TrainingHudModel Build(IReadOnlyList<RulesReader.Entry> entries, PlayerView view, double now, Func<RulesReader.Entry, ForceId>? sideOf = null)
    {
        sideOf ??= e => e.Status.Owner;
        if (view.IsNone) return new TrainingHudModel(Array.Empty<ObjectiveLine>(), Array.Empty<StandardLine>(), false);
        var byNet = entries.ToDictionary(e => e.NetworkId);
        var order = TreeOrder(entries);

        var hasChildren = entries.Where(e => e.ParentNetworkId != 0).Select(e => e.ParentNetworkId).ToHashSet();
        bool UnderMembers(RulesReader.Entry e)
        {
            for (int p = e.ParentNetworkId, g = 0; p != 0 && g < 64 && byNet.TryGetValue(p, out var par); p = par.ParentNetworkId, g++)
                if (par.Status.Kind.ToString().Equals("MemberEvaluation", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        // a MISSION objective = a LEAF objective of the exercise itself (the summary's rule): not a composite, not a per-man evaluation, not the game's score tree
        bool Visible(RulesReader.Entry e)
            => e.Status.VisibleSides != 0 ? (e.Status.VisibleSides & (1 << (int)view.Side)) != 0 : sideOf(e) == view.Side || sideOf(e) == ForceId.Neutral;
        var objectives = entries
            .Where(e => !e.Authored && e.Status.Role == ObjectiveRole.Objective && !hasChildren.Contains(e.NetworkId) && !UnderMembers(e)
                        && !GameKinds.Contains(e.Status.Kind.ToString()) && !ConductKinds.Contains(e.Status.Kind.ToString()) && !AggregateKinds.Contains(e.Status.Kind.ToString())
                        && !e.Status.Kind.ToString().Equals("MemberEvaluation", StringComparison.OrdinalIgnoreCase) && !IsCapturePoint(e.Status)
                        && e.Status.Title.Length > 0 && Visible(e))
            .OrderByDescending(e => e.Status.Required != 0).ThenBy(e => order[e.NetworkId])
            .Select(e => LineOf(e, 0, now))
            .ToList();

        bool InGameScore(RulesReader.Entry e) => byNet.TryGetValue(e.ParentNetworkId, out var p) && p.Status.Kind.ToString().Equals("Points", StringComparison.OrdinalIgnoreCase);
        IEnumerable<RulesReader.Entry> Measures() => entries.Where(e => !e.Authored && e.Status.Role == ObjectiveRole.Measure && e.Status.Scored != 0
                                                                       && !AggregateKinds.Contains(e.Status.Kind.ToString()) && !InGameScore(e) && e.Status.Title.Length > 0);
        var mine = Measures().Where(e => e.SubjectNetworkId == view.UnitNetId).ToList();
        bool team = false;
        if (mine.Count == 0) { mine = Measures().Where(e => e.SubjectNetworkId != 0 && view.Team.Contains(e.SubjectNetworkId)).ToList(); team = mine.Count > 0; }
        var standards = mine.OrderBy(e => e.Status.Group.ToString(), StringComparer.OrdinalIgnoreCase).ThenBy(e => order[e.NetworkId]).Select(StandardOf).ToList();
        return new TrainingHudModel(objectives, standards, team);
    }

    /// <summary>The roots by network id, each followed by its children by network id (the order the rules view lists them in).</summary>
    public static Dictionary<int, int> TreeOrder(IReadOnlyList<RulesReader.Entry> entries)
    {
        var inSet = entries.ToDictionary(e => e.NetworkId);
        var kids = entries.Where(e => e.ParentNetworkId != 0 && e.ParentNetworkId != e.NetworkId && inSet.ContainsKey(e.ParentNetworkId))
                          .GroupBy(e => e.ParentNetworkId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.NetworkId).ToList());
        var order = new Dictionary<int, int>();
        void Walk(RulesReader.Entry e) { if (order.ContainsKey(e.NetworkId)) return; order[e.NetworkId] = order.Count; if (kids.TryGetValue(e.NetworkId, out var k)) foreach (var c in k) Walk(c); }
        foreach (var r in entries.Where(e => e.ParentNetworkId == 0 || e.ParentNetworkId == e.NetworkId || !inSet.ContainsKey(e.ParentNetworkId)).OrderBy(e => e.NetworkId)) Walk(r);
        foreach (var e in entries) if (!order.ContainsKey(e.NetworkId)) order[e.NetworkId] = order.Count;   // a cycle's members
        return order;
    }

    private static ObjectiveLine LineOf(RulesReader.Entry e, int depth, double now)
    {
        var s = e.Status;
        var mark = s.State switch { ObjectiveState.Achieved => HudMark.Done, ObjectiveState.Failed => HudMark.Failed, ObjectiveState.Active => HudMark.Active, _ => HudMark.Waiting };
        float fraction = -1f; string detail = "";
        if (s.State == ObjectiveState.Active)
        {
            bool seconds = s.Kind.ToString().Equals("HoldArea", StringComparison.OrdinalIgnoreCase);
            if (s.ProgressNeed > 0)
            {
                fraction = Math.Clamp(s.ProgressCur / (float)s.ProgressNeed, 0f, 1f);
                detail = seconds ? $"{RulesModel.Clock(s.ProgressCur)} / {RulesModel.Clock(s.ProgressNeed)}" : $"{s.ProgressCur} / {s.ProgressNeed}";
            }
            else if (s.Deadline > 0f)
            {
                double left = Math.Max(0, s.Deadline - now), span = Math.Max(1e-3, s.Deadline - s.ChangedAt);
                fraction = (float)Math.Clamp(1 - left / span, 0, 1);
                detail = RulesModel.Clock(left);
            }
        }
        return new ObjectiveLine(e.NetworkId, s.Title.ToString(), mark, s.State, s.Required != 0, depth, fraction, detail);
    }

    private static StandardLine StandardOf(RulesReader.Entry e)
    {
        var s = e.Status;
        string text = s.Target != 0f ? $"{s.Value:0.##} · target {s.Target:0.##}" : $"{s.Value:0.##}";
        uint rgb = s.Pass <= 0f ? Neutral : s.Score >= s.Pass ? Good : s.Score >= s.Pass * 0.6f ? Fair : Poor;
        return new StandardLine(e.NetworkId, s.Title.ToString(), text, s.Score, s.Pass, rgb);
    }
}

/// <summary>A side's tickets: its <c>Counter</c> value and where it started.</summary>
public sealed record TicketLine(ForceId Side, float Value, float Start);

/// <summary>
/// A capture point on the top bar: its letter, who holds it, and — while it changes hands — who is taking it and how far (0..1).
/// </summary>
public sealed record PointLine(int NetworkId, string Letter, ForceId Owner, ForceId Challenger, float Fraction, bool Contested, float AreaX, float AreaY, float AreaRadius)
{
    /// <summary>Is the ground point (<paramref name="x"/>, <paramref name="y"/>) inside the point's area (its bounding circle, from the status)?</summary>
    public bool Contains(float x, float y) => AreaRadius > 0f && (x - AreaX) * (x - AreaX) + (y - AreaY) * (y - AreaY) <= AreaRadius * AreaRadius;
}

/// <summary>The "Capturing B · 60 %" strip: the text and the colour side (the side taking the point).</summary>
public sealed record CaptureStrip(string Text, ForceId Side);

/// <summary>The game HUD this frame: tickets per side, the capture points, the round clock and its words, the strip, my kill streak.</summary>
public sealed record GameHudModel(IReadOnlyList<TicketLine> Tickets, IReadOnlyList<PointLine> Points, string Clock, bool ClockIsCountdown, CaptureStrip? Strip, int Streak, IReadOnlySet<int> BountyOn)
{
    public bool HasBar => Tickets.Count > 0 || Points.Count > 0 || Clock.Length > 0;
}

/// <summary>
/// ⭐ H2 / PH-4 (CE-4227) — the game HUD as DATA, from the ONE reader. Which statuses are tickets / points / the clock is read from <c>Kind</c> (⛔ no entity names):
/// a <c>Counter</c> with a start above zero = a side's tickets (a lead counter starts at 0 and is not shown); a capture <c>HoldArea</c> = a point (letter = the
/// title's last word when it is one letter, else its order); the clock = the earliest <c>Trigger</c> <c>Deadline</c> still ahead (a countdown), else the elapsed
/// time; the streak = my <c>Count</c> titled <i>Streak</i> (backend S7 P-8); a bounty = a <c>Bounty</c> status that is Active, on its <c>Subject</c>.
/// </summary>
public static class GameHud
{
    public const uint Blue = 0x2F6FD6, Red = 0xC2412F, Grey = 0x6B7280;
    public const int StreakShownFrom = 2;

    public static uint RgbOf(ForceId side) => side switch { ForceId.Friend => Blue, ForceId.Hostile => Red, _ => Grey };

    /// <param name="viewerAt">⭐ W-H3 — where the possessed man stands (ground x, y); the <i>Capturing</i> strip shows while he is inside a changing point's <c>AreaCenter</c> / <c>AreaRadius</c> (read from the status, so every node has it).</param>
    public static GameHudModel Build(IReadOnlyList<RulesReader.Entry> entries, PlayerView view, double now, System.Numerics.Vector2? viewerAt = null, Func<RulesReader.Entry, ForceId>? sideOf = null, Func<RulesReader.Entry, bool>? endsRound = null)
    {
        sideOf ??= e => e.Status.Owner;   // ⚠ a running Counter's Owner is Neutral on the wire (measured, showcase-crossfire): the live caller passes the subject's side
        var live = entries.Where(e => !e.Authored).ToList();
        string Kind(RulesReader.Entry e) => e.Status.Kind.ToString();

        var tickets = live.Where(e => Kind(e).Equals("Counter", StringComparison.OrdinalIgnoreCase) && e.Status.Target > 0f)
                          .OrderBy(e => e.NetworkId).GroupBy(e => sideOf(e)).Select(g => g.First())
                          .OrderBy(e => sideOf(e) == ForceId.Friend ? 0 : sideOf(e) == ForceId.Hostile ? 1 : 2)
                          .Select(e => new TicketLine(sideOf(e), e.Status.Value, e.Status.Target)).ToList();

        var pointRows = live.Where(e => e.Status.Role == ObjectiveRole.Objective && TrainingHud.IsCapturePoint(e.Status) && !e.Status.IsFinal).OrderBy(e => e.NetworkId).ToList();
        var points = pointRows.Select((e, i) => PointOf(e, i)).ToList();

        // the clock: a trigger's deadline still ahead ⇒ the time left; otherwise the elapsed time of the run
        // ⭐ CE-4238 — the round clock counts down to the trigger whose Do holds EndExercise (several ⇒ the soonest armed one; ExpectedAt ≥ NeverSeconds is a placeholder); no such trigger readable ⇒ a deadline, else elapsed
        double? ahead = endsRound == null ? null
            : live.Where(e => e.Status.Role == ObjectiveRole.Trigger && e.Status.Closed == 0 && !e.Status.IsFinal && e.Status.ExpectedAt > now && e.Status.ExpectedAt < RulesModel.NeverSeconds && endsRound(e))
                  .Select(e => (double?)e.Status.ExpectedAt).DefaultIfEmpty(null).Min();
        ahead ??= live.Where(e => e.Status.Role == ObjectiveRole.Trigger && e.Status.Deadline > now).Select(e => (double?)e.Status.Deadline).DefaultIfEmpty(null).Min();
        bool any = tickets.Count > 0 || points.Count > 0 || ahead != null;
        string clock = !any ? "" : RulesModel.Clock(ahead is { } d ? d - now : now);

        CaptureStrip? strip = null;
        if (!view.IsNone && viewerAt is { } at)
            foreach (var p in points)
                if (p.Contested && p.Contains(at.X, at.Y))
                {
                    string verb = p.Challenger == view.Side ? "Capturing" : "Contested";
                    strip = new CaptureStrip($"{verb} {p.Letter} · {(int)Math.Round(p.Fraction * 100)} %", p.Challenger);
                    break;
                }

        int streak = view.IsNone ? 0 : live.Where(e => e.SubjectNetworkId == view.UnitNetId && Kind(e).Equals("Count", StringComparison.OrdinalIgnoreCase)
                                                       && e.Status.Title.ToString().Equals("Streak", StringComparison.OrdinalIgnoreCase)).Select(e => (int)e.Status.Value).DefaultIfEmpty(0).Max();
        var bounty = live.Where(e => Kind(e).Equals("Bounty", StringComparison.OrdinalIgnoreCase) && e.Status.State == ObjectiveState.Active && e.SubjectNetworkId != 0)
                         .Select(e => e.SubjectNetworkId).ToHashSet();
        return new GameHudModel(tickets, points, clock, ahead != null, strip, streak, bounty);
    }

    private static PointLine PointOf(RulesReader.Entry e, int index)
    {
        var s = e.Status;
        string title = s.Title.ToString();
        string last = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        string letter = last.Length == 1 && char.IsLetterOrDigit(last[0]) ? last.ToUpperInvariant() : ((char)('A' + index % 26)).ToString();
        float frac = s.ProgressNeed > 0 ? Math.Clamp(s.ProgressCur / (float)s.ProgressNeed, 0f, 1f) : 0f;
        var challenger = s.Challenger;   // ⭐ W-H3 — a status field now (was parsed out of the reason text)
        bool contested = s.ProgressNeed > 0 && s.ProgressCur > 0 && s.ProgressCur < s.ProgressNeed && challenger != ForceId.Neutral && challenger != s.Owner;
        return new PointLine(e.NetworkId, letter, s.Owner, contested ? challenger : ForceId.Neutral, contested ? frac : 0f, contested, s.AreaCenter.X, s.AreaCenter.Y, s.AreaRadius);
    }
}
