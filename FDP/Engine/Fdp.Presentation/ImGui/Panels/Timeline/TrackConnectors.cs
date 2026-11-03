using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>
/// ⭐ <c>CE-4221</c> (rules view R4; docs/DESIGN_Rules_View_UI.md RV-1 X1) — a link from one of MY items to an item of ANOTHER track: the dependency arrow of the rules view (<i>X → the trigger that waits on X</i>).
/// </summary>
/// <param name="FromItemId">An item of the track that carries the link.</param>
/// <param name="ToTrackId">The other track's <see cref="ITimelineTrack.Id"/>.</param>
/// <param name="ToItemId">The item of that track the arrow ends at.</param>
/// <param name="Colour">ABGR; null = the panel's connector colour.</param>
public sealed record TrackLink(string FromItemId, string ToTrackId, string ToItemId, uint? Colour = null);

/// <summary>One link resolved to screen pixels: from the right end of its source item to the start of its target item.</summary>
public readonly record struct Connector(string FromTrack, string FromItem, string ToTrack, string ToItem, Vector2 From, Vector2 To, uint? Colour);

/// <summary>
/// ⭐ CE-4221 (X1) — the connectors of the shown tracks, PURE: a link is drawn only when BOTH of its items were drawn this frame (<see cref="TimelinePanel.TrackItemHit"/> — a track collapsed away, an item scrolled out of the window
/// leave no hit, so the link is not drawn: no second layout). Geometry: an elbow with an arrowhead.
/// </summary>
public static class TrackConnectors
{
    /// <summary>The drawable connectors of <paramref name="visible"/> tracks (those <see cref="TrackLayout.Visible"/> gave), from this frame's item hits.</summary>
    public static List<Connector> Resolve(IEnumerable<ITimelineTrack> visible, IReadOnlyList<TimelinePanel.TrackItemHit> hits)
    {
        var at = new Dictionary<(string, string), TimelinePanel.TrackItemHit>();
        foreach (var h in hits) at[(h.TrackId, h.ItemId)] = h;
        var result = new List<Connector>();
        foreach (var t in visible)
            foreach (var l in t.Links)
            {
                if (!at.TryGetValue((t.Id, l.FromItemId), out var from) || !at.TryGetValue((l.ToTrackId, l.ToItemId), out var to)) continue;
                float x1 = float.IsNaN(from.XEnd) ? from.X : from.XEnd;
                result.Add(new Connector(t.Id, l.FromItemId, l.ToTrackId, l.ToItemId, new Vector2(x1, from.Y), new Vector2(to.X, to.Y), l.Colour));
            }
        return result;
    }

    /// <summary>The elbow (its vertical run kept inside <paramref name="maxX"/>, the row's right edge) from <paramref name="from"/> to <paramref name="to"/>: out to the right, across at a column between the two (or just past the source when the target is not to its right), then in to the target.</summary>
    public static Vector2[] Elbow(Vector2 from, Vector2 to, float stub = 6f, float maxX = float.PositiveInfinity)
    {
        float xm = to.X - stub >= from.X + stub ? (from.X + to.X) / 2f : from.X + stub;
        xm = Math.Min(xm, maxX - 3f);   // never past the row's right edge: a source that runs to "now" at the edge would put its vertical run out of sight
        if (Math.Abs(to.Y - from.Y) < 0.5f) return new[] { from, to };
        return new[] { from, new Vector2(xm, from.Y), new Vector2(xm, to.Y), to };
    }

    /// <summary>The arrowhead triangle at the end of <paramref name="path"/>, pointing along its last segment.</summary>
    public static Vector2[] Arrow(IReadOnlyList<Vector2> path, float size = 5f)
    {
        var tip = path[^1];
        var prev = path.Count > 1 ? path[^2] : tip - new Vector2(1, 0);
        var d = tip - prev;
        d = d.LengthSquared() < 1e-6f ? new Vector2(1, 0) : Vector2.Normalize(d);
        var n = new Vector2(-d.Y, d.X);
        return new[] { tip, tip - d * size + n * (size * 0.6f), tip - d * size - n * (size * 0.6f) };
    }
}

/// <summary>One line of the Coming up strip: what is expected or clock-pinned next, and when (sim seconds).</summary>
public sealed record ComingUpEntry(string TrackId, string TrackLabel, string ItemId, string ItemLabel, double At, TimelineTrackItemStyle Style);

/// <summary>
/// ⭐ <c>CE-4221</c> (X5) — <b>Coming up</b>, PURE: the next <see cref="TimelineTrackItemStyle.Expected"/> / <see cref="TimelineTrackItemStyle.Pinned"/> items after <c>now</c>, soonest first (ties in track order). An Expected SEGMENT happens at its end
/// (<c>To</c>: what it waits for), an Expected or Pinned KEY at its start. Nothing Solid or Hatched is ever listed.
/// </summary>
public static class ComingUp
{
    public static IReadOnlyList<ComingUpEntry> Of(IEnumerable<ITimelineTrack> tracks, double now, int n)
    {
        if (n <= 0) return Array.Empty<ComingUpEntry>();
        var all = new List<(ComingUpEntry Entry, int Order)>();
        int order = 0;
        foreach (var t in tracks)
        {
            foreach (var it in t.Items)
            {
                if (it.Style is not (TimelineTrackItemStyle.Expected or TimelineTrackItemStyle.Pinned)) continue;
                double at = it.Style == TimelineTrackItemStyle.Expected && it.Kind == TimelineTrackItemKind.Segment ? it.To ?? it.From : it.From;
                if (at > now) all.Add((new ComingUpEntry(t.Id, t.Label, it.Id, it.Label, at, it.Style), order++));
            }
        }
        // the same moment named twice on one row (a deadline is an Expected stretch AND a Pinned diamond) is one line
        all = all.Where(a => !(a.Entry.Style == TimelineTrackItemStyle.Expected && all.Any(b => b.Entry.Style == TimelineTrackItemStyle.Pinned && b.Entry.TrackId == a.Entry.TrackId && b.Entry.At == a.Entry.At))).ToList();
        return all.OrderBy(a => a.Entry.At).ThenBy(a => a.Order).Take(n).Select(a => a.Entry).ToList();
    }
}
