using System;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-4122 (<c>docs/DESIGN_Map_3D_Mode.md</c> §6ad-2) — the ground BEYOND a terrain: one pure, deterministic function that
    /// <see cref="TerrainWorld.GroundHeightAt"/> asks outside the terrain's rectangle and the apron mesh draws, so a unit out there stands on what
    /// is drawn. Three zones: at the terrain's edge it IS the edge height (seamless); inside an attached outside field's footprint (the SUMO net) it
    /// is that field (meets SUMO's own ground mesh); everywhere else the edge/field level plus gentle HILLS that fade in over
    /// <see cref="RampMetres"/>.
    /// <para>No SUMO types: the field arrives as a delegate in the terrain's LOCAL metres, its footprint as a quad (the net's bounds, which the
    /// placement may rotate). Set once on the world (<see cref="TerrainWorld.AttachBeyond"/>); without one the world uses <see cref="Default"/>
    /// (edge + hills).</para>
    /// </summary>
    public sealed class BeyondGround
    {
        /// <summary>Distance over which the hills (and the field's pull) fade in from the zone that owns the ground.</summary>
        public const float RampMetres = 300f;
        /// <summary>The hills' total amplitude (m): the three octaves sum to at most this, either way.</summary>
        public const float HillAmplitude = 25f;   // ⭐ 2026-10-30 — 10 m read as flat under the haze (the user wanted "a bit hilly"); 25 m, the waves shorter

        /// <summary>No outside field: the edge height plus hills.</summary>
        public static readonly BeyondGround Default = new(null, null);

        private readonly Vector2[]? _quad;   // counter-clockwise, convex

        /// <summary>The outside field's height at a LOCAL (x, y), or null.</summary>
        public Func<float, float, float>? Field { get; }
        public bool HasField => Field != null;
        /// <summary>The field's footprint corners (local metres, counter-clockwise), empty without a field.</summary>
        public ReadOnlySpan<Vector2> FieldQuad => _quad ?? Array.Empty<Vector2>();
        /// <summary>The footprint's axis-aligned box (a quad may be rotated).</summary>
        public Vector2 FieldMin { get; }
        public Vector2 FieldMax { get; }

        /// <param name="field">the outside field in local metres, or null</param>
        /// <param name="fieldQuad">the field's footprint: 4 corners of a convex quad (any winding); required with a field</param>
        public BeyondGround(Func<float, float, float>? field, Vector2[]? fieldQuad)
        {
            if (field == null) return;
            if (fieldQuad == null || fieldQuad.Length != 4) throw new ArgumentException("A beyond-field needs its footprint: exactly 4 corners.", nameof(fieldQuad));
            Field = field;
            float area2 = 0f;
            for (int i = 0; i < 4; i++) { var a = fieldQuad[i]; var b = fieldQuad[(i + 1) % 4]; area2 += a.X * b.Y - b.X * a.Y; }
            _quad = (Vector2[])fieldQuad.Clone();
            if (area2 < 0f) Array.Reverse(_quad);
            var lo = new Vector2(float.PositiveInfinity); var hi = new Vector2(float.NegativeInfinity);
            foreach (var p in _quad) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            FieldMin = lo; FieldMax = hi;
        }

        /// <summary>0 → 0, <paramref name="d"/> ≥ <see cref="RampMetres"/> → 1, smooth between.</summary>
        public static float Ramp(float d)
        {
            float t = Math.Clamp(d / RampMetres, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The relief: three octaves of travelling sines (wavelengths ~585, ~310, ~170 m, two directions each, amplitudes 12.5 + 7.5 + 5 m) — a fixed
        /// function of (x, y), no seed, smooth, in [−<see cref="HillAmplitude"/>, +<see cref="HillAmplitude"/>].
        /// </summary>
        public static float Hills(float x, float y)
        {
            const float tau = 2f * MathF.PI;
            float h = 0f;
            const float w = 0.65f;   // the wavelengths' scale (1 = the first fit, 900 / 480 / 260 m)
            h += 2.5f * MathF.Sin(tau * (0.8f * x + 0.6f * y) / (900f * w) + 0.7f) + 2.5f * MathF.Sin(tau * (-0.6f * x + 0.8f * y) / (760f * w) + 2.1f);
            h += 1.5f * MathF.Sin(tau * (0.28f * x + 0.96f * y) / (480f * w) + 4.0f) + 1.5f * MathF.Sin(tau * (0.96f * x - 0.28f * y) / (410f * w) + 1.3f);
            h += 1.0f * MathF.Sin(tau * (0.6f * x - 0.8f * y) / (260f * w) + 5.2f) + 1.0f * MathF.Sin(tau * (0.8f * x + 0.6f * y) / (290f * w) + 3.3f);
            return h * (HillAmplitude / 10f);
        }

        /// <summary>Distance (m) from a LOCAL point to the field's footprint: 0 inside it, +∞ without a field.</summary>
        public float DistanceToField(float x, float y)
        {
            if (_quad == null) return float.PositiveInfinity;
            var p = new Vector2(x, y);
            bool inside = true;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                var a = _quad[i]; var b = _quad[(i + 1) % 4];
                var ab = b - a;
                if (ab.X * (p.Y - a.Y) - ab.Y * (p.X - a.X) < 0f) inside = false;
                float len2 = ab.LengthSquared();
                float t = len2 > 0f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
                best = MathF.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return inside ? 0f : best;
        }

        /// <summary>True when (x, y) is inside the field's footprint.</summary>
        public bool InField(float x, float y) => _quad != null && DistanceToField(x, y) == 0f;

        /// <summary>
        /// The ground at (x, y) outside the terrain. <paramref name="edge"/> is the terrain's height at the nearest point of its own rectangle,
        /// <paramref name="dTerrain"/> the distance to that rectangle (m).
        /// <c>level = lerp(edge, field, ramp(dTerrain))</c> · <c>hills = Hills · ramp(dTerrain) · ramp(dField)</c>.
        /// </summary>
        public float HeightAt(float x, float y, float edge, float dTerrain)
        {
            float rt = Ramp(dTerrain);
            if (Field == null) return edge + Hills(x, y) * rt;
            float level = edge + (Field(x, y) - edge) * rt;
            return level + Hills(x, y) * rt * Ramp(DistanceToField(x, y));
        }
    }
}
