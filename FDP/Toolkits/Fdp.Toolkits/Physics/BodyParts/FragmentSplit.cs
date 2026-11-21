using System;
using System.Numerics;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐ <c>CE-3212</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §10 S3-1) — how a burst's fragments split over a
    /// body's parts: each part's share is its boxes' area PROJECTED toward the burst, over the whole body's. The same boxes the rounds meet
    /// (<see cref="PersonBody"/>), so a prone man shows a burst at his feet his head and shoulders, a standing one his whole side.
    /// </summary>
    public static class FragmentSplit
    {
        /// <summary>
        /// Fills <paramref name="shares"/> (indexed by part id) with each part's share of the area <paramref name="boxes"/> present along
        /// <paramref name="towardBurst"/> (a unit vector in the BODY frame); returns the total projected area (in the boxes' units²), 0 when
        /// the direction is degenerate. Shares sum to 1.
        /// </summary>
        public static float Shares(ReadOnlySpan<BodyBox> boxes, Vector3 towardBurst, Span<float> shares)
        {
            shares.Clear();
            if (towardBurst.LengthSquared() < 1e-12f) return 0f;
            var d = Vector3.Normalize(towardBurst);
            float total = 0f;
            foreach (ref readonly var b in boxes)
            {
                if (b.Part >= shares.Length) continue;
                var x = Vector3.Transform(Vector3.UnitX, b.Rotation);
                var y = Vector3.Transform(Vector3.UnitY, b.Rotation);
                var z = Vector3.Transform(Vector3.UnitZ, b.Rotation);
                // an oriented box's silhouette along d: the three face pairs, each weighted by how squarely it faces d
                float area = 4f * (b.Half.Y * b.Half.Z * MathF.Abs(Vector3.Dot(d, x))
                                 + b.Half.X * b.Half.Z * MathF.Abs(Vector3.Dot(d, y))
                                 + b.Half.X * b.Half.Y * MathF.Abs(Vector3.Dot(d, z)));
                shares[b.Part] += area;
                total += area;
            }
            if (total <= 0f) return 0f;
            for (int i = 0; i < shares.Length; i++) shares[i] /= total;
            return total;
        }
    }
}
