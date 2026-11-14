using System;
using System.Numerics;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// The ONE encoding of an orientation on a position wire (<c>WorldPos.OrientationXyz</c> on the NED stack) — <c>docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md</c> D / F.
    ///
    /// <para><b>The form.</b> The quaternion's vector part <c>(x, y, z)</c> travels; <c>w</c> is implied:
    /// <c>w = +sqrt(max(0, 1 − |v|²))</c>. The sender first makes <c>w ≥ 0</c> (a quaternion and its negation are the same rotation,
    /// so negating when <c>w &lt; 0</c> loses nothing). Two consequences are the point of the shape:</para>
    /// <list type="bullet">
    ///   <item>an <b>all-zero</b> struct decodes to the identity rotation, never to NaN or to a zero (invalid) quaternion — a wire struct
    ///   nobody filled in is a sane pose;</item>
    ///   <item>there is no Euler convention to get wrong and no gimbal case — a nose straight up or down (CE-4068) round-trips like any
    ///   other attitude.</item>
    /// </list>
    /// <para>⛔ Every wire calls THIS class; a second copy of the arithmetic is how two wires come to disagree. The field's TYPE is
    /// <c>OrientationQuat</c> — the bytes of a Vec3f, a type of its own so <c>ddsmonitor</c> shows it
    /// as heading/pitch/roll + quaternion via <see cref="OrientationDisplay"/>.</para>
    /// </summary>
    public static class QuaternionWireCodec
    {
        /// <summary>
        /// The vector part to put on the wire for <paramref name="rotation"/>: normalised, canonicalised to <c>w ≥ 0</c>.
        /// A degenerate (zero / non-finite) input encodes as the identity.
        /// </summary>
        public static void Encode(Quaternion rotation, out float x, out float y, out float z)
        {
            float lengthSq = rotation.X * rotation.X + rotation.Y * rotation.Y + rotation.Z * rotation.Z + rotation.W * rotation.W;
            if (!(lengthSq > 1e-12f) || float.IsInfinity(lengthSq))
            {
                x = y = z = 0f;   // identity: the all-zero struct
                return;
            }

            float inv = 1f / MathF.Sqrt(lengthSq);
            float sign = rotation.W < 0f ? -inv : inv;   // canonicalise: the sent form always has w >= 0
            x = rotation.X * sign;
            y = rotation.Y * sign;
            z = rotation.Z * sign;
        }

        /// <summary>
        /// The rotation a wire vector part stands for: <c>w = +sqrt(max(0, 1 − |v|²))</c>. A vector part longer than 1 (float rounding at
        /// a half-turn, or a corrupt sample) is scaled back onto the unit sphere (<c>w = 0</c>), so the result is always a unit quaternion.
        /// </summary>
        public static Quaternion Decode(float x, float y, float z)
        {
            float vSq = x * x + y * y + z * z;
            if (!float.IsFinite(vSq))
                return Quaternion.Identity;   // NaN / Infinity on the wire: a defect, but never propagate it into the pose

            if (vSq >= 1f)
            {
                float scale = 1f / MathF.Sqrt(vSq);
                return new Quaternion(x * scale, y * scale, z * scale, 0f);
            }

            return new Quaternion(x, y, z, MathF.Sqrt(1f - vSq));
        }
    }
}
