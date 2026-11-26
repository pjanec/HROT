using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐⭐ <c>CE-5057</c> / <c>CE-11063</c> (📄 docs/DESIGN_Terrain_OSM_Bridges.md §2) — the importer's BRIDGES stage: every bridged edge chain of the road net becomes ONE deck
/// from bank to bank — the two-way pair merged (the corridors' own <see cref="Corridors.PairTwoWay"/>), consecutive bridged edges chained through a junction that
/// joins exactly two of them, the deck's ENDS at the LEVELLED ground there (the bank roads) and straight (linear along its length) in between. Pure: the importer
/// writes the decks' parts (<see cref="DeckGeometry.BuildAlong"/>) and puts the net's bridged lanes on them.
/// </summary>
public static class Bridges
{
    /// <summary>A bare <c>layer</c> &gt; 0 edge (no <c>bridge</c> tag) is a bridge only where the ground at its middle lies this far under its ends' straight line (m) — D-1.</summary>
    public const float LayerDip = 1f;

    /// <summary>One deck: its line (terrain-local), width, the heights of its two ends, and the net edges it carries (for the net re-elevation, D-7).</summary>
    public sealed record Deck(string Id, Vector2[] Line, float Width, float ZStart, float ZEnd, IReadOnlyList<string> EdgeIds, IReadOnlyList<string> InnerJunctions)
    {
        /// <summary>The deck's length along its line.</summary>
        public float Length { get; } = LengthOf(Line);

        /// <summary>The deck's top at arc length <paramref name="s"/> — straight from <see cref="ZStart"/> to <see cref="ZEnd"/>.</summary>
        public float ZAt(float s) => Length < 1e-3f ? ZStart : ZStart + (ZEnd - ZStart) * Math.Clamp(s / Length, 0f, 1f);

        /// <summary>The deck's top over the point of its line nearest <paramref name="p"/> (the net's bridged lane points, a junction on the bridge).</summary>
        public float ZNear(Vector2 p)
        {
            float best = float.MaxValue, at = 0f, run = 0f;
            for (int i = 0; i + 1 < Line.Length; i++)
            {
                var a = Line[i]; var d = Line[i + 1] - a; float l2 = d.LengthSquared(), l = MathF.Sqrt(l2);
                float t = l2 < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - a, d) / l2, 0f, 1f);
                float dd = Vector2.DistanceSquared(p, a + d * t);
                if (dd < best) { best = dd; at = run + t * l; }
                run += l;
            }
            return ZAt(at);
        }
    }

    /// <summary>The decks of <paramref name="edges"/> (as <see cref="Corridors.Read"/> gives them) over the LEVELLED ground <paramref name="groundAt"/>.</summary>
    internal static List<Deck> Plan(List<Corridors.Edge> edges, Func<Vector2, float> groundAt)
    {
        bool IsBridge(Corridors.Edge e)
        {
            if (e.Tunnel || e.Lanes.Count == 0) return false;
            if (e.Bridge) return true;
            if (e.Layer <= 0) return false;
            var line = Corridors.ReferenceLine(e);
            if (line.Length < 2) return false;
            float mid = groundAt(PointAt(line, LengthOf(line) / 2f)), straight = (groundAt(line[0]) + groundAt(line[^1])) / 2f;
            return straight - mid >= LayerDip;   // a bare layer=1 over a dip: a bridge; on level ground or an embankment: the ground carries it
        }
        var bridged = edges.Where(IsBridge).ToList();
        if (bridged.Count == 0) return new List<Deck>();

        // one SEGMENT per pair (merged midline, both carriageways) or per lone edge — the corridors' own pairing rule (CE-4694), here on the bridged edges
        var pairs = Corridors.PairTwoWay(bridged, _ => true);
        var segs = new List<Seg>();
        foreach (var e in bridged)
        {
            if (pairs.TryGetValue(e, out var other))
            {
                if (other == null) continue;
                var (mid, half) = Corridors.Merged(e, other);
                segs.Add(new Seg(e.From, e.To, mid, 2f * (half - RoadCorridors.Shoulder), new[] { e.Id, other.Id }));
            }
            else
                segs.Add(new Seg(e.From, e.To, Corridors.ReferenceLine(e), e.Lanes.Sum(l => l.Width), new[] { e.Id }));
        }

        // chain the segments through junctions that join exactly two of them (one bridge split at a junction ⇒ one deck, no staircase)
        var byNode = new Dictionary<string, List<int>>();
        for (int i = 0; i < segs.Count; i++)
            foreach (var node in new[] { segs[i].From, segs[i].To })
                if (node.Length > 0) { if (!byNode.TryGetValue(node, out var l)) byNode[node] = l = new List<int>(); l.Add(i); }
        var used = new bool[segs.Count];
        var decks = new List<Deck>();
        for (int start = 0; start < segs.Count; start++)
        {
            if (used[start]) continue;
            // walk back to an end of this chain (a node not joining exactly two segments)
            int first = start; string tail = segs[start].From; var seen = new HashSet<int> { start };
            while (Through(tail, first) is int prev && seen.Add(prev)) { first = prev; tail = segs[prev].From == tail ? segs[prev].To : segs[prev].From; }
            // walk forward, orienting each segment, joining the lines
            var line = new List<Vector2>(); var ids = new List<string>(); var inner = new List<string>();
            float width = 0f; int cur = first; string at = tail;
            while (true)
            {
                used[cur] = true;
                var sg = segs[cur];
                bool forward = sg.From == at || (sg.From.Length == 0 && line.Count == 0);
                var pts = forward ? sg.Line : sg.Line.Reverse().ToArray();
                foreach (var p in pts) if (line.Count == 0 || Vector2.DistanceSquared(line[^1], p) > 1e-4f) line.Add(p);
                ids.AddRange(sg.EdgeIds); width = MathF.Max(width, sg.Width);
                at = forward ? sg.To : sg.From;
                if (Through(at, cur) is not int next || used[next]) break;
                inner.Add(at);
                cur = next;
            }
            if (line.Count < 2) continue;
            var arr = line.ToArray();
            // named after its forward edge (SUMO's reverse edge is "-<id>"), the lowest id — stable across imports of the same net
            string name = ids.OrderBy(i => i.StartsWith('-') ? 1 : 0).ThenBy(i => i, StringComparer.Ordinal).First();
            decks.Add(new Deck("osm-bridge/" + name, arr, width, groundAt(arr[0]), groundAt(arr[^1]), ids, inner));
        }
        return decks;

        // the other segment at a node joining exactly two (null at a chain end)
        int? Through(string node, int from)
        {
            if (node.Length == 0 || !byNode.TryGetValue(node, out var l) || l.Count != 2) return null;
            return l[0] == from ? l[1] : l[0];
        }
    }

    private sealed record Seg(string From, string To, Vector2[] Line, float Width, string[] EdgeIds);

    private static float LengthOf(Vector2[] line)
    {
        float l = 0f;
        for (int i = 1; i < line.Length; i++) l += Vector2.Distance(line[i - 1], line[i]);
        return l;
    }

    private static Vector2 PointAt(Vector2[] line, float s)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            float l = Vector2.Distance(line[i], line[i + 1]);
            if (s <= l || i + 2 == line.Length) return l < 1e-6f ? line[i] : Vector2.Lerp(line[i], line[i + 1], Math.Clamp(s / l, 0f, 1f));
            s -= l;
        }
        return line[^1];
    }
}
