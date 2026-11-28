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

    /// <summary>A deck's top stands at least this far above the ground (m) to be a deck at all — where the edge runs ON the ground, no deck (D-3a).</summary>
    public const float MinClearance = 0.3f;

    /// <summary>
    /// One deck: its line (terrain-local), width, its height PROFILE along the line (arc lengths and heights — the upper hull of the ground under the bridged
    /// edge, D-3a), and the net edges it carries (for the net re-elevation, D-7).
    /// </summary>
    public sealed record Deck(string Id, Vector2[] Line, float Width, float[] ProfileS, float[] ProfileZ, IReadOnlyList<string> EdgeIds, IReadOnlyList<string> InnerJunctions)
    {
        /// <summary>The deck's length along its line.</summary>
        public float Length { get; } = LengthOf(Line);

        /// <summary>The height of the deck's first and last points.</summary>
        public float ZStart => ProfileZ[0];
        public float ZEnd => ProfileZ[^1];

        /// <summary>The deck's top at arc length <paramref name="s"/> — the profile, linear between its samples.</summary>
        public float ZAt(float s)
        {
            if (s <= ProfileS[0]) return ProfileZ[0];
            for (int i = 1; i < ProfileS.Length; i++)
                if (s <= ProfileS[i])
                {
                    float span = ProfileS[i] - ProfileS[i - 1];
                    return span < 1e-6f ? ProfileZ[i] : ProfileZ[i - 1] + (ProfileZ[i] - ProfileZ[i - 1]) * (s - ProfileS[i - 1]) / span;
                }
            return ProfileZ[^1];
        }

        /// <summary>The deck's top over the point of its line nearest <paramref name="p"/>.</summary>
        public float ZNear(Vector2 p) { Nearest(p, out float at, out _); return ZAt(at); }

        /// <summary>⭐ D-7 — whether <paramref name="p"/> (a net lane point, a junction) lies ON this deck — beside its line within its half-width + 1 m, and not past
        /// either end — and the deck's height there. A bridged edge's point off every deck stays on the ground.</summary>
        public bool Covers(Vector2 p, out float z)
        {
            float d2 = Nearest(p, out float at, out bool past);
            z = ZAt(at);
            float r = Width / 2f + 1f;
            return !past && d2 <= r * r;
        }

        private float Nearest(Vector2 p, out float at, out bool past)
        {
            float best = float.MaxValue, run = 0f; at = 0f; past = false;
            for (int i = 0; i + 1 < Line.Length; i++)
            {
                var a = Line[i]; var d = Line[i + 1] - a; float l2 = d.LengthSquared(), l = MathF.Sqrt(l2);
                float raw = l2 < 1e-9f ? 0f : Vector2.Dot(p - a, d) / l2, t = Math.Clamp(raw, 0f, 1f);
                float dd = Vector2.DistanceSquared(p, a + d * t);
                if (dd < best) { best = dd; at = run + t * l; past = (i == 0 && raw < -0.02f) || (i + 2 == Line.Length && raw > 1.02f); }
                run += l;
            }
            return best;
        }
    }

    /// <summary>The decks of <paramref name="edges"/> (as <see cref="Corridors.Read"/> gives them) over the LEVELLED ground <paramref name="groundAt"/>.</summary>
    internal static List<Deck> Plan(List<Corridors.Edge> edges, Func<Vector2, float> groundAt)
    {
        bool IsBridge(Corridors.Edge e)
        {
            if (e.Tunnel || e.Lanes.Count == 0) return false;
            if (e.Type.StartsWith("railway", StringComparison.Ordinal)) return false;   // ⭐ D-1a (user, 2026-11-26: "road bridges are important, not rail bridges")
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
            var spans = Spans(arr, groundAt);
            for (int k = 0; k < spans.Count; k++)
            {
                var (sub, ps, pz) = spans[k];
                decks.Add(new Deck("osm-bridge/" + name + (spans.Count > 1 ? "#" + k.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""), sub, width, ps, pz, ids, inner));
            }
        }
        return decks;

        // the other segment at a node joining exactly two (null at a chain end)
        int? Through(string node, int from)
        {
            if (node.Length == 0 || !byNode.TryGetValue(node, out var l) || l.Count != 2) return null;
            return l[0] == from ? l[1] : l[0];
        }
    }

    /// <summary>Sampling step of the ground profile under a bridged chain (m), and how far a span reaches past its banks (m) — D-3a.</summary>
    public const float ProfileStep = 2f, SpanMargin = 2f;

    /// <summary>
    /// ⭐⭐ D-3a (📐 Stechovice, 2026-11-26: netconvert merges a bridge with its approaches into edges far LONGER than the structure — a 201 m path edge whose straight
    /// deck ran 15 m UNDER a hill) — a deck is laid only over the DIPS of the chain, two cases: ① the straight line between its ends never cuts the ground — the edge IS
    /// the structure (the Vltava crossings): that line, kept where it clears the ground by more than <see cref="MinClearance"/> (re-based on a kept stretch's own
    /// banks) — D-3; ② it cuts the ground (a hill: the edge is longer than the structure): each dip filled to its SPILL level (the lower of its rims) and decked FLAT
    /// there — never under a hill, never a deck over flat ground. Each deck reaches <see cref="SpanMargin"/> onto its banks, meeting rising ground. Returns
    /// [sub-line, profile arc lengths (from the sub-line's start), heights] per span.
    /// </summary>
    internal static List<(Vector2[] Line, float[] S, float[] Z)> Spans(Vector2[] line, Func<Vector2, float> groundAt)
    {
        float total = LengthOf(line);
        int n = Math.Max(2, (int)MathF.Ceiling(total / ProfileStep) + 1);
        var s = new float[n]; var g = new float[n];
        for (int i = 0; i < n; i++) { s[i] = total * i / (n - 1); g[i] = groundAt(PointAt(line, s[i])); }
        // ⭐ which case: does the straight line between the ends cut the ground anywhere (by more than the clearance)?
        float cut = 0f;
        for (int i = 0; i < n; i++) cut = MathF.Max(cut, g[i] - (g[0] + (g[n - 1] - g[0]) * s[i] / total));
        var dips = new List<(int A, int B, float Za, float Zb)>();
        if (cut > MinClearance)
        {
            // the edge is LONGER than its structure (it runs over a hill): fill each dip to its SPILL level — the lower of its two rims — and lay the deck there,
            //   flat across the dip (trapping-rain-water: level = min(the highest ground before, the highest ground after))
            var before = new float[n]; var after = new float[n];
            for (int i = 0; i < n; i++) before[i] = i == 0 ? g[0] : MathF.Max(before[i - 1], g[i]);
            for (int i = n - 1; i >= 0; i--) after[i] = i == n - 1 ? g[n - 1] : MathF.Max(after[i + 1], g[i]);
            int ra = -1;
            for (int i = 0; i <= n; i++)
            {
                bool wet = i < n && MathF.Min(before[i], after[i]) - g[i] > MinClearance;
                if (wet && ra < 0) ra = i;
                if (!wet && ra >= 0)
                {
                    int ea = Math.Max(0, ra - 1), eb = Math.Min(n - 1, i);
                    float level = MathF.Min(before[ra], after[i - 1]);
                    dips.Add((ea, eb, level, level));
                    ra = -1;
                }
            }
        }
        else Split(0, n - 1, 0);
        void Split(int a, int b, int depth)
        {
            // the chord from a to b; its runs clearing the ground (gaps under 2 samples merged)
            var runs = new List<(int A, int B)>();
            for (int i = a; i <= b; i++)
            {
                float chord = b == a ? g[a] : g[a] + (g[b] - g[a]) * (s[i] - s[a]) / (s[b] - s[a]);
                if (chord - g[i] <= MinClearance) continue;
                if (runs.Count > 0 && i - runs[^1].B <= 2) runs[^1] = (runs[^1].A, i);
                else runs.Add((i, i));
            }
            foreach (var (ra, rb) in runs)
            {
                int ea = Math.Max(a, ra - 1), eb = Math.Min(b, rb + 1);   // the run's banks: the samples just outside it
                if (depth < 4 && (ea != a || eb != b)) Split(ea, eb, depth + 1);
                else dips.Add((ea, eb, g[ea], g[eb]));
            }
        }
        var spans = new List<(Vector2[], float[], float[])>();
        foreach (var (a, b, za, zb) in dips)
        {
            float s0 = MathF.Max(0f, s[a] - SpanMargin), s1 = MathF.Min(total, s[b] + SpanMargin);
            // the reach onto a bank only where the bank is level or rising: past a crest (the ground falling away beyond the bank) the deck stops AT the bank — no
            //   overhang, no ramp down the far slope
            if (groundAt(PointAt(line, s0)) < za - MinClearance) s0 = s[a];
            if (groundAt(PointAt(line, s1)) < zb - MinClearance) s1 = s[b];
            if (s[b] - s[a] < 2f * ProfileStep) continue;
            var pts = new List<Vector2> { PointAt(line, s0) };
            float run = 0f;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                run += Vector2.Distance(line[i], line[i + 1]);
                if (run > s0 + 1e-3f && run < s1 - 1e-3f) pts.Add(line[i + 1]);
            }
            pts.Add(PointAt(line, s1));
            // the deck: straight between the dip's banks; over each margin it meets the ground where the bank keeps rising (never under it) and stays level
            //   where the bank falls away (an embankment end — the ramp rule, D-5)
            float z0 = MathF.Max(za, groundAt(PointAt(line, s0))), z1 = MathF.Max(zb, groundAt(PointAt(line, s1)));
            float Z(float at) => at <= s0 ? z0 : at >= s1 ? z1
                : at < s[a] ? z0 + (za - z0) * (at - s0) / MathF.Max(1e-3f, s[a] - s0)
                : at > s[b] ? zb + (z1 - zb) * (at - s[b]) / MathF.Max(1e-3f, s1 - s[b])
                : za + (zb - za) * (at - s[a]) / (s[b] - s[a]);
            var ps = new List<float>(); var pz = new List<float>();
            foreach (var at in new[] { s0, s[a], s[b], s1 }.Distinct()) { ps.Add(at - s0); pz.Add(Z(at)); }
            spans.Add((pts.ToArray(), ps.ToArray(), pz.ToArray()));
        }
        return spans;
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
