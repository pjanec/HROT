using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// THE footprint hash for a zone-shaped entity — <c>hash(SimTransform ⊕ Points)</c>.
    ///
    /// <para><b>⭐ One helper, two callers, no cooperation required.</b> The loader computes it when it
    /// loads and stamps it into <see cref="TerrainAssetLoadState.SourceHash"/>; any reader (the zone
    /// gizmo, the zones view) recomputes it from live entity data and compares. Equal ⇒ fresh; different
    /// ⇒ stale. ⛔ Both sides MUST call this — two hash implementations would silently disagree and every
    /// zone would read stale forever.</para>
    ///
    /// <para><b>⛔ Why not a version counter.</b> <c>EditablePolyline.Version</c> was the original key and
    /// is measured broken: nothing increments it, and the vertex edit tool commits a drag by building a
    /// fresh component, which RESETS it. A counter needs every mutation path to cooperate and has already
    /// failed that test in production; a hash needs cooperation from nobody.</para>
    ///
    /// <para><b>⛔ Why the TRANSFORM must be in the hash.</b> <c>EditablePolyline.Points</c> are RELATIVE
    /// to the entity's origin, so MOVING a zone leaves every point byte-identical. A points-only hash
    /// would be blind to a move — the single most likely edit.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.7 ③c, §2.2.
    /// </summary>
    public static class ZoneFootprint
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime       = 1099511628211UL;

        /// <summary>
        /// Computes the footprint hash from an entity's world origin and its RELATIVE points.
        /// </summary>
        /// <param name="origin">The entity's <c>SimTransform.Position</c>.</param>
        /// <param name="points">The entity's <c>EditablePolyline.Points</c> (relative offsets).</param>
        /// <returns>
        /// A stable hash of the resolved world footprint. A <c>null</c> or empty point list still hashes
        /// the origin, so a zone that has lost its shape cannot silently match one that never had one.
        /// </returns>
        public static ulong Compute(Vector3 origin, IReadOnlyList<Vector2>? points)
        {
            ulong h = FnvOffsetBasis;

            h = MixFloat(h, origin.X);
            h = MixFloat(h, origin.Y);
            h = MixFloat(h, origin.Z);

            // The COUNT is mixed in so a shorter polyline cannot collide with a longer one whose extra
            // vertices happen to fold to the same value.
            int count = points?.Count ?? 0;
            h = Mix(h, (uint)count);

            for (int i = 0; i < count; i++)
            {
                h = MixFloat(h, points![i].X);
                h = MixFloat(h, points[i].Y);
            }

            return h;
        }

        /// <summary>
        /// Mixes one <see cref="float"/> by its exact bit pattern.
        /// ⚠ <c>-0f</c> is normalised to <c>0f</c> first: the two compare equal and describe the same
        /// point but have different bit patterns, so hashing raw bits would report a move that never
        /// happened.
        /// </summary>
        private static ulong MixFloat(ulong h, float value)
        {
            if (value == 0f) value = 0f;   // collapses -0f onto +0f
            return Mix(h, BitConverter.SingleToUInt32Bits(value));
        }

        private static ulong Mix(ulong h, ulong value)
        {
            // FNV-1a over the 8 bytes of the value, low byte first.
            for (int b = 0; b < 8; b++)
            {
                h ^= (value >> (b * 8)) & 0xFF;
                h *= FnvPrime;
            }
            return h;
        }
    }
}
