using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4692</c> BL-4 (📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §2b) — THE one bridge deck: a deck slab between two deck ends and a ramp
    /// beyond each end down to the ground, every corner carrying its height ABOVE THE GROUND under it (ground-relative). Lifted from the authored bridge
    /// (<c>FeatureKinds.Bridge</c>, docs/DESIGN_Terrain_Authoring.md §8) BELOW its JSON, so the authoring compile (which writes the parts as features) and the
    /// obstacle bake (which turns a laid bridge into walkables, <see cref="TerrainObstacles.DeckOf"/>) share it — ruling 9. Deck height and ramp length are
    /// parameters: a floating bridge has a low deck.
    /// </summary>
    public static class DeckGeometry
    {
        /// <summary>The authored bridge's deck height above the bank (m) and its ramp length (m): a 1 : 4 ramp (14°, under the vehicle layer's 20°).</summary>
        public const float AuthoredDeckHeight = 1.5f, AuthoredRampLength = 6.0f;

        /// <summary>The ramp's run per metre of rise — the authored bridge's 1 : 4 (14°).</summary>
        public const float RampRunPerRise = AuthoredRampLength / AuthoredDeckHeight;

        /// <summary>
        /// ⭐ <c>CE-4696</c> (backend, cross-lane — user "fix yourself") — the SHORTEST ramp a laid deck gets (m). 📐 Measured (<c>StaticObstacleBakeTests.CE4696_*</c>): a
        /// deck 0.6 m up (the floating span) at 1 : 4 had a 2.4 m ramp and NO vehicle path (the vehicle layer's 1.8 m agent radius leaves a ramp that short unusable);
        /// 3.6 m (a 0.9 m deck) and longer carry one. The assault span's own 4.8 m is the floor; a real ribbon bridge's ramp bay is ~6.9 m (DESIGN_Bridge_Laying §12d).
        /// </summary>
        public const float MinRampLength = 4.8f;

        /// <summary>A LAID deck's ramp length for a deck <paramref name="rise"/> metres above the bank: the authored slope, never shorter than <see cref="MinRampLength"/>
        /// (a lower deck gets a gentler ramp). ⚠ The authored bridge feature keeps its own <see cref="AuthoredRampLength"/>.</summary>
        public static float RampLengthFor(float rise) => MathF.Max(MinRampLength, rise * RampRunPerRise);

        /// <summary>One quad of the deck: <see cref="Kind"/>, and its four corners counter-clockwise along the bridge (p0 − n, p1 − n, p1 + n, p0 + n), each with
        /// its height above the ground. A ramp's foot corners are at height 0 (the parser / <see cref="TerrainObstacles.DeckOf"/> bury them, TH-K).</summary>
        public readonly record struct Part(TerrainWalkableKind Kind, (Vector2 At, float Above) C0, (Vector2 At, float Above) C1, (Vector2 At, float Above) C2, (Vector2 At, float Above) C3)
        {
            public (Vector2 At, float Above) this[int i] => i switch { 0 => C0, 1 => C1, 2 => C2, _ => C3 };
        }

        /// <summary>
        /// The deck from <paramref name="a"/> to <paramref name="b"/> (the deck ends, on the banks), <paramref name="width"/> wide, <paramref name="deckHeight"/> above the ground
        /// at each end, with a ramp <paramref name="rampLength"/> long beyond each end: [deck, ramp at A, ramp at B]. ⚠ The order and the corners are the authored bridge's
        /// exactly (its compiled world is byte-identical through this — rail <c>FeatureKindsTests.CE4692_*</c>).
        /// </summary>
        public static Part[] Build(Vector2 a, Vector2 b, float width, float deckHeight, float rampLength)
        {
            var d = b - a; float len = d.Length();
            if (len < 1e-3f) throw new ArgumentException("[Terrain] a bridge deck's two ends are the same place.");
            var u = d / len; var n = new Vector2(-u.Y, u.X); float hw = width / 2f;
            Part Quad(TerrainWalkableKind kind, Vector2 p0, float z0, Vector2 p1, float z1)
                => new(kind, (p0 - n * hw, z0), (p1 - n * hw, z1), (p1 + n * hw, z1), (p0 + n * hw, z0));
            return new[]
            {
                Quad(TerrainWalkableKind.Slab, a, deckHeight, b, deckHeight),
                Quad(TerrainWalkableKind.Ramp, a - u * rampLength, 0f, a, deckHeight),
                Quad(TerrainWalkableKind.Ramp, b, deckHeight, b + u * rampLength, 0f),
            };
        }

        /// <summary>A deck end standing higher than this above the ground under it (m) gets a ramp down to it in <see cref="BuildAlong"/> — one step (the vehicle navmesh's).</summary>
        public const float RampThreshold = 0.3f;

        /// <summary>
        /// ⭐⭐ <c>CE-5057</c> / <c>CE-11063</c> (docs/DESIGN_Terrain_OSM_Bridges.md D-5) — a deck ALONG a polyline (an imported bridge: from bank to bank, possibly curved),
        /// <paramref name="width"/> wide, its top at <paramref name="zAt"/>(arc length) — ABSOLUTE heights, so a deck can run straight over a valley — written as this
        /// geometry's own parts: one SLAB per segment, MITRED (an interior vertex's two corners on the bisector, so neighbouring slabs share their edge), each corner's
        /// height ABOVE THE GROUND under it (<paramref name="groundAt"/>; may be negative where the bank rises above the deck — the walkable top is the higher of the two);
        /// and a RAMP beyond an end only where that end stands more than <see cref="RampThreshold"/> above the ground ([slabs…, ramp at the start?, ramp at the end?]).
        /// The same quads the authored and the laid bridge write (<see cref="Build"/>) — ruling 9.
        /// </summary>
        public static Part[] BuildAlong(IReadOnlyList<Vector2> line, float width, Func<float, float> zAt, Func<Vector2, float> groundAt)
        {
            if (line.Count < 2) throw new ArgumentException("[Terrain] a deck along a line needs two points.");
            float hw = width / 2f;
            int n = line.Count;
            var s = new float[n];
            for (int i = 1; i < n; i++) s[i] = s[i - 1] + Vector2.Distance(line[i - 1], line[i]);
            if (s[n - 1] < 1e-3f) throw new ArgumentException("[Terrain] a deck's two ends are the same place.");
            Vector2 Dir(int i) { var d = line[i + 1] - line[i]; float l = d.Length(); return l < 1e-6f ? Vector2.UnitX : d / l; }
            static Vector2 Left(Vector2 u) => new(-u.Y, u.X);
            // the offset of each vertex's two corners: the segment's normal at the ends, the mitre (bisector, length hw / cos(half the turn), capped at 2 hw) inside
            var off = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                if (i == 0) { off[i] = Left(Dir(0)) * hw; continue; }
                if (i == n - 1) { off[i] = Left(Dir(n - 2)) * hw; continue; }
                var na = Left(Dir(i - 1)); var nb = Left(Dir(i));
                var m = na + nb; float ml = m.Length();
                if (ml < 1e-4f) { off[i] = nb * hw; continue; }   // a U-turn: no mitre
                m /= ml;
                float cos = MathF.Max(0.5f, Vector2.Dot(m, nb));
                off[i] = m * (hw / cos);
            }
            (Vector2 At, float Above) C(Vector2 at, float z) => (at, z - groundAt(at));
            var parts = new List<Part>(n + 1);
            for (int i = 0; i + 1 < n; i++)
            {
                float z0 = zAt(s[i]), z1 = zAt(s[i + 1]);
                parts.Add(new Part(TerrainWalkableKind.Slab, C(line[i] - off[i], z0), C(line[i + 1] - off[i + 1], z1), C(line[i + 1] + off[i + 1], z1), C(line[i] + off[i], z0)));
            }
            // a ramp where an END stands above the ground (an embankment end, DESIGN_Terrain_OSM_Bridges Q1): from the end down to the ground, at the authored slope
            void Ramp(Vector2 end, Vector2 outward, Vector2 o, float z, bool atStart)
            {
                float rise = z - groundAt(end);
                if (rise <= RampThreshold) return;
                var foot = end + outward * RampLengthFor(rise);
                parts.Add(atStart
                    ? new Part(TerrainWalkableKind.Ramp, (foot - o, 0f), C(end - o, z), C(end + o, z), (foot + o, 0f))
                    : new Part(TerrainWalkableKind.Ramp, C(end - o, z), (foot - o, 0f), (foot + o, 0f), C(end + o, z)));
            }
            Ramp(line[0], -Dir(0), off[0], zAt(0f), atStart: true);
            Ramp(line[n - 1], Dir(n - 2), off[n - 1], zAt(s[n - 1]), atStart: false);
            return parts.ToArray();
        }

        /// <summary>The deck ends of an obstacle centred at <paramref name="centre"/>, heading <paramref name="yaw"/>, <paramref name="span"/> long (the span between the ends).</summary>
        public static (Vector2 A, Vector2 B) Ends(Vector2 centre, float yaw, float span)
        {
            var u = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw)) * (span * 0.5f);
            return (centre - u, centre + u);
        }

        /// <summary>The plan footprint of the whole deck (slab + both ramps) as a box: the reach of a blast (BL-8), a dirty rectangle (BL-5).</summary>
        public static (Vector2 Min, Vector2 Max) Bounds(IReadOnlyList<Part> parts)
        {
            var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
            foreach (var p in parts)
                for (int i = 0; i < 4; i++) { lo = Vector2.Min(lo, p[i].At); hi = Vector2.Max(hi, p[i].At); }
            return (lo, hi);
        }
    }
}
