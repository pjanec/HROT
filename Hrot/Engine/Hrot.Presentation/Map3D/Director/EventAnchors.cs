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
                if (Express(lane, events, i, unitName) is not { } spec) continue;
                if (e.Frame >= index.FrameSim.Count || double.IsNaN(index.FrameSim[e.Frame])) continue;
                best = new EventAnchor(spec.Name, spec.Spec, index.FrameSim[e.Frame]);
                bestFrame = e.Frame;
                break;
            }
        }
        return best;
    }

    private static (string Name, AnchorSpec Spec)? Express(string lane, IReadOnlyList<TimelineEvent> events, int i, Func<long, string?> unitName)
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
                for (int k = 0; k < i; k++) if (events[k].Subject == e.Subject) return null;   // AnchorBook finds a unit's FIRST hit only
                return ($"hit:{unit}", new AnchorSpec { On = "hit", Target = unit });
            }
            default:
                return null;
        }
    }

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
