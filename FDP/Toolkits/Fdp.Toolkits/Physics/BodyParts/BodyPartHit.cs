using System;
using System.Numerics;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐⭐ <c>CE-3209</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-2, HL-5) — tiers ② and ③ of a round's test
    /// against one body: its bounding sphere first, then its oriented boxes. The first box the segment ENTERS wins; the face is the side it
    /// entered, read in the BODY frame (front / side / rear / top — <see cref="Facing"/>, the armour model's faces).
    /// </summary>
    public static class BodyPartHit
    {
        /// <summary>A face normal more upward than this (cos 45°) is the TOP — the armour model's descent rule (D-A) for a part.</summary>
        public const float TopNormalZ = 0.7071f;

        /// <summary>Does the segment <paramref name="a"/>→<paramref name="b"/> pass within <paramref name="radius"/> of <paramref name="centre"/>?</summary>
        public static bool SegmentTouchesSphere(Vector3 a, Vector3 b, Vector3 centre, float radius)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-12f ? System.Math.Clamp(Vector3.Dot(centre - a, ab) / len2, 0f, 1f) : 0f;
            return Vector3.DistanceSquared(a + ab * t, centre) <= radius * radius;
        }

        /// <summary>
        /// The first of <paramref name="boxes"/> (a body at <paramref name="position"/> turned by <paramref name="rotation"/>, its boxes scaled
        /// by <paramref name="scale"/>) that the segment <paramref name="a"/>→<paramref name="b"/> enters. False when it meets none.
        /// </summary>
        public static bool Segment(ReadOnlySpan<BodyBox> boxes, Vector3 position, Quaternion rotation, float scale, Vector3 a, Vector3 b,
                                   out BodyPartHitResult hit)
        {
            hit = default;
            if (scale <= 0f) return false;
            var toBody = Quaternion.Inverse(rotation);
            var la = Vector3.Transform(a - position, toBody) / scale;
            var lb = Vector3.Transform(b - position, toBody) / scale;
            float best = float.MaxValue;
            bool any = false;
            foreach (ref readonly var box in boxes)
            {
                var toBox = Quaternion.Inverse(box.Rotation);
                var pa = Vector3.Transform(la - box.Centre, toBox);
                var pb = Vector3.Transform(lb - box.Centre, toBox);
                if (!Slab(pa, pb, box.Half, out float t, out int axis, out float sign)) continue;
                if (t >= best) continue;
                best = t;
                any = true;
                var normalLocal = axis switch { 0 => new Vector3(sign, 0, 0), 1 => new Vector3(0, sign, 0), _ => new Vector3(0, 0, sign) };
                var normalBody = Vector3.Transform(normalLocal, box.Rotation);
                var at = pa + (pb - pa) * t;
                var unit = new Vector3(Div(at.X, box.Half.X), Div(at.Y, box.Half.Y), Div(at.Z, box.Half.Z)) * 0.5f;   // −0.5 … 0.5
                hit = new BodyPartHitResult(t, box.Part, FaceOf(normalBody), unit);
            }
            return any;
        }

        /// <summary>The armour face a body-frame outward normal belongs to: TOP when it points up ≥ 45°, else by its bearing from the
        /// body's forward (&lt; 60° front, &gt; 120° rear, else side — <see cref="ArmorModel"/>'s arcs). A downward face reads as a side.</summary>
        public static Facing FaceOf(Vector3 normalBody)
        {
            if (normalBody.Z >= TopNormalZ) return Facing.Top;
            var h = new Vector2(normalBody.X, normalBody.Y);
            if (h.LengthSquared() < 1e-6f) return Facing.Side;
            float cos = Vector2.Normalize(h).X;
            return cos > 0.5f ? Facing.Front : cos < -0.5f ? Facing.Rear : Facing.Side;
        }

        /// <summary>The slab test in the box's own frame: the entry t (0..1) and the axis / sign of the face entered. A segment that STARTS
        /// inside enters at 0 through the face nearest its start.</summary>
        private static bool Slab(Vector3 a, Vector3 b, Vector3 half, out float tEnter, out int axis, out float sign)
        {
            tEnter = 0f; axis = 0; sign = 1f;
            float tMin = 0f, tMax = 1f;
            var d = b - a;
            for (int i = 0; i < 3; i++)
            {
                float o = i == 0 ? a.X : i == 1 ? a.Y : a.Z;
                float v = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
                float h = i == 0 ? half.X : i == 1 ? half.Y : half.Z;
                if (MathF.Abs(v) < 1e-9f)
                {
                    if (o < -h || o > h) return false;
                    continue;
                }
                float t1 = (-h - o) / v, t2 = (h - o) / v;
                float near = MathF.Min(t1, t2), far = MathF.Max(t1, t2);
                if (near > tMin) { tMin = near; axis = i; sign = v > 0f ? -1f : 1f; }
                if (far < tMax) tMax = far;
                if (tMin > tMax) return false;
            }
            if (tMin <= 0f)
            {
                // starts inside: the face nearest the start
                float rx = MathF.Abs(a.X) / MathF.Max(half.X, 1e-6f), ry = MathF.Abs(a.Y) / MathF.Max(half.Y, 1e-6f), rz = MathF.Abs(a.Z) / MathF.Max(half.Z, 1e-6f);
                if (rx >= ry && rx >= rz) { axis = 0; sign = MathF.Sign(a.X) is var s0 && s0 != 0 ? s0 : 1f; }
                else if (ry >= rz) { axis = 1; sign = MathF.Sign(a.Y) is var s1 && s1 != 0 ? s1 : 1f; }
                else { axis = 2; sign = MathF.Sign(a.Z) is var s2 && s2 != 0 ? s2 : 1f; }
                tEnter = 0f;
                return true;
            }
            tEnter = tMin;
            return true;
        }

        private static float Div(float v, float h) => h > 1e-6f ? v / h : 0f;
    }
}
