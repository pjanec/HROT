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

        /// <summary>A ramp's length for a deck <paramref name="rise"/> metres above the bank: the authored slope, never shorter than 1 m.</summary>
        public static float RampLengthFor(float rise) => MathF.Max(1f, rise * RampRunPerRise);

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
