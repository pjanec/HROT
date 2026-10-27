using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CR-6</c> (docs/DESIGN_Director_Timeline_Editor.md §11e, "CR-6 detail") — an index EVENT as a take anchor: the <see cref="Name"/> a key's
/// <see cref="AtExpr"/> names (<c>"shot:Tank#2"</c>), the <see cref="AnchorSpec"/> the take's own <see cref="AnchorBook"/> resolves live
/// (<c>{on: shot, by: Tank, n: 2}</c>), and the sim time the event has in the index the key was made against (<see cref="EventSim"/> — what the
/// take bakes for the name).
/// </summary>
public sealed record EventAnchor(string Name, AnchorSpec Spec, double EventSim);

/// <summary>
/// ⭐ <c>CR-6</c> — finds the event a new key should hang on: the NEAREST event at or before the playhead, in the lanes the take's anchors can
/// express. ⛔ No second resolver: it only NAMES an event in the take's existing anchor vocabulary (<see cref="AnchorSpec"/> cues
/// <c>shot</c> / <c>blast</c> / <c>hit</c>); <see cref="AnchorBook"/> + <c>Take.Baked</c> + <see cref="AtExpr"/> resolve it, as for any anchor.
/// <para><b>Which events are expressible</b> — only those <see cref="AnchorBook"/> can find again on a live run:
/// <c>combat.shot</c> → the n-th round BY that unit (<c>shot:Unit#n</c> = <c>{on: shot, by: Unit, n}</c>, the ordinal counted per shooter, so another
/// unit's extra round does not renumber it) · <c>combat.blast</c> → the n-th burst of the run (<c>blast#n</c>, global as <c>AnchorBook</c> counts it) ·
/// <c>combat.hit</c> → a unit's FIRST hit only (<c>hit:Unit</c> — <c>AnchorBook</c> ignores <c>n</c> for a hit). <c>life.*</c> has no
/// <see cref="AnchorSpec"/> cue, so it is not offered (CR-5 grows the cues).</para>
/// </summary>
public static class EventAnchors
{
    /// <summary>The lanes K looks in, nearest event wins, a tie goes to the earlier lane here.</summary>
    public static readonly IReadOnlyList<string> DefaultLanes = new[] { "combat.shot", "combat.blast", "combat.hit" };

    /// <summary>The index frame whose sim time is the last one at or before <paramref name="sim"/> (−1 when none, or the index has no sim clock).</summary>
    public static int FrameAt(TimelineIndex index, double sim)
    {
        var fs = index.FrameSim;
        for (int f = Math.Min(fs.Count, index.FrameTicks.Count) - 1; f >= 0; f--)
            if (!double.IsNaN(fs[f]) && fs[f] <= sim + 1e-9) return f;
        return -1;
    }

    /// <summary>
    /// The anchor for the nearest expressible event at or before <paramref name="sim"/>; null when none precedes it (a fixed time then).
    /// <paramref name="unitName"/> maps an event's network id to the unit's name (null = unknown / gone: that event is skipped).
    /// </summary>
    public static EventAnchor? NearestBefore(TimelineIndex index, double sim, Func<long, string?> unitName, IReadOnlyList<string>? lanes = null)
    {
        int frame = FrameAt(index, sim);
        if (frame < 0) return null;
        EventAnchor? best = null;
        int bestFrame = -1;
        foreach (var lane in lanes ?? DefaultLanes)
        {
            var events = index.Lane(lane);
            for (int i = index.Before(lane, frame + 1) - 1; i >= 0; i--)
            {
                var e = events[i];
                if (e.Frame <= bestFrame) break;   // an earlier lane already has one this late; ties keep it
                if (Of(index, lane, i, unitName) is not { } found) continue;
                best = found;
                bestFrame = e.Frame;
                break;
            }
        }
        return best;
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> / <c>CR-8</c> (docs/DESIGN_Director_Timeline_Editor.md §11n) — the anchor of the SPECIFIC event <paramref name="eventIndex"/> of
    /// <paramref name="lane"/> (what <see cref="NearestBefore"/> finds by time; the Drafter names an event it already chose). Null when the lane has no such
    /// event, the event is not expressible (see the class remarks) or its frame has no sim time. ⛔ The one naming switch: <see cref="NearestBefore"/> calls this.
    /// </summary>
    public static EventAnchor? Of(TimelineIndex index, string lane, int eventIndex, Func<long, string?> unitName)
    {
        var events = index.Lane(lane);
        if ((uint)eventIndex >= (uint)events.Count) return null;
        if (Express(lane, events, eventIndex, unitName, index.Has("combat.blast") ? index.Lane("combat.blast") : null) is not { } spec) return null;
        int frame = events[eventIndex].Frame;
        if (frame < 0 || frame >= index.FrameSim.Count || double.IsNaN(index.FrameSim[frame])) return null;
        return new EventAnchor(spec.Name, spec.Spec, index.FrameSim[frame]);
    }

    private static (string Name, AnchorSpec Spec)? Express(string lane, IReadOnlyList<TimelineEvent> events, int i, Func<long, string?> unitName,
                                                           IReadOnlyList<TimelineEvent>? blasts)
    {
        var e = events[i];
        switch (lane)
        {
            case "combat.shot":
            {
                if (e.Subject <= 0 || unitName(e.Subject) is not { Length: > 0 } unit) return null;
                int n = 0;
                for (int k = 0; k <= i; k++) if (events[k].Subject == e.Subject) n++;
                return ($"shot:{unit}#{n.ToString(CultureInfo.InvariantCulture)}", new AnchorSpec { On = "shot", By = unit, N = n });
            }
            case "combat.blast":
                return ($"blast#{(i + 1).ToString(CultureInfo.InvariantCulture)}", new AnchorSpec { On = "blast", N = i + 1 });
            case "combat.hit":
            {
                if (e.Subject <= 0 || unitName(e.Subject) is not { Length: > 0 } unit) return null;
                for (int k = 0; k < i; k++) if (events[k].Subject == e.Subject) return null;   // a unit's FIRST strike only (TimeOf finds no other)
                if (blasts != null && FirstStruckBlast(blasts, e.Subject) is { } b && b.Frame < e.Frame) return null;   // ⭐ CR-5 — a blast struck it first
                return ($"hit:{unit}", new AnchorSpec { On = "hit", Target = unit });
            }
            default:
                return null;
        }
    }

    private static TimelineEvent? FirstStruckBlast(IReadOnlyList<TimelineEvent> blasts, long unit)
    {
        foreach (var b in blasts) if (b.Other == unit) return b;
        return null;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1304</c> P4 — CR-5 (docs/DESIGN_Director_Timeline_Editor.md §11s) — the INVERSE of <see cref="Of"/>: the sim time of the event an EVENT cue names, read
    /// from <paramref name="index"/> — the live index on a live run, the recording's on a recording. ⛔ The same switch as the naming above, so a name
    /// <see cref="Of"/> gave finds its event again on any run: <c>shot</c> = the <c>N</c>-th <c>combat.shot</c> by a unit <see cref="AnchorSpec.By"/> names (every
    /// shooter when none); <c>blast</c> = the <c>N</c>-th <c>combat.blast</c>; <c>hit</c> = the FIRST strike of the <see cref="AnchorSpec.Target"/> — a
    /// <c>combat.hit</c> on it or a <c>combat.blast</c> that struck it (<c>Other</c>), whichever is first. Only events at or after <paramref name="since"/> count (a
    /// live run's start). Null: not (yet) in the index, the index has no sim clock, or the cue is not an event cue.
    /// </summary>
    /// <param name="isUnit">Whether the name (a unit's name, or <c>"*part"</c>) names the unit with this network id.</param>
    public static double? TimeOf(TimelineIndex index, AnchorSpec spec, Func<string, long, bool> isUnit, double since = double.NegativeInfinity)
    {
        var sim = index.FrameSim;
        double? SimOf(int frame) => frame >= 0 && frame < sim.Count && !double.IsNaN(sim[frame]) && sim[frame] >= since - 1e-9 ? sim[frame] : null;
        switch (spec.On)
        {
            case "shot":
            case "blast":
            {
                int want = Math.Max(1, spec.N), seen = 0;
                foreach (var e in index.Lane(spec.On == "shot" ? "combat.shot" : "combat.blast"))
                {
                    if (SimOf(e.Frame) is not { } t) continue;
                    if (spec.On == "shot" && spec.By != null && !isUnit(spec.By, e.Subject)) continue;
                    if (++seen == want) return t;
                }
                return null;
            }
            case "hit":
            {
                if (spec.Target == null) return null;
                double? first = null;
                foreach (var e in index.Lane("combat.hit"))
                    if (SimOf(e.Frame) is { } t && isUnit(spec.Target, e.Subject)) { first = t; break; }
                foreach (var e in index.Lane("combat.blast"))
                    if (SimOf(e.Frame) is { } t && isUnit(spec.Target, e.Other)) { if (first is not { } f || t < f) first = t; break; }
                return first;
            }
            default:
                return null;
        }
    }

    /// <summary>The cues <see cref="TimeOf(TimelineIndex, AnchorSpec, Func{string, long, bool}, double)"/> reads from an index (the rest observe the world).</summary>
    public static bool IsEventCue(string on) => on is "shot" or "blast" or "hit";

    /// <summary>
    /// The text a key shows for its anchor: <c>shot Tank#2 +0.4s</c> (the event anchor's name with its cue apart, then the offset); a fixed-time key has none.
    /// </summary>
    public static string? Label(AtExpr at)
    {
        if (at.Anchor is not { } name) return null;
        int colon = name.IndexOf(':');
        string what = colon > 0 ? name.Substring(0, colon) + " " + name.Substring(colon + 1) : name;
        if (at.Offset == 0d) return what;
        return what + " " + (at.Offset > 0 ? "+" : "-") + Math.Abs(at.Offset).ToString("0.###", CultureInfo.InvariantCulture) + "s";
    }
}
