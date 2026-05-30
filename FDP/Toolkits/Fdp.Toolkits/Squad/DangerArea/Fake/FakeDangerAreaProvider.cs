using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea.Fake
{
    /// <summary>
    /// Test-only implementation of <see cref="IDangerAreaProvider"/>.
    /// Returns a pre-configured list of <see cref="DangerAreaDescriptor"/>s built via a
    /// fluent API; zero-alloc on each <see cref="Refresh"/> call.
    /// </summary>
    public sealed class FakeDangerAreaProvider : IDangerAreaProvider
    {
        private readonly List<DangerAreaDescriptor> _descriptors = new List<DangerAreaDescriptor>();

        /// <summary>
        /// Adds a <see cref="DangerAreaDescriptor"/> that will be returned by the next
        /// <see cref="Refresh"/> call.
        /// </summary>
        /// <param name="featureName">
        /// Human-readable navmesh polygon id. <see cref="DangerAreaDescriptor.FeatureId"/> is
        /// set to the FNV-1a-32 hash of the UTF-8 encoding of this string.
        /// </param>
        /// <param name="kind">Feature classification.</param>
        /// <param name="threatRating">Threat level in [0, 1].</param>
        /// <param name="center">OBB centre in world-space 3D.</param>
        /// <param name="extentsXY">OBB half-extents in the XY plane.</param>
        /// <param name="angleRad">OBB yaw in radians.</param>
        /// <param name="zFloor">Bottom of the height band.</param>
        /// <param name="zCeiling">Top of the height band.</param>
        /// <param name="nearSide">Near-side 3D handle.</param>
        /// <param name="farSide">Far-side 3D handle.</param>
        /// <returns>This instance for fluent chaining.</returns>
        public FakeDangerAreaProvider Add(
            string      featureName,
            DangerAreaKind kind        = DangerAreaKind.StreetCrossing,
            float       threatRating   = 0.5f,
            Vector3     center         = default,
            Vector2     extentsXY      = default,
            float       angleRad       = 0f,
            float       zFloor         = 0f,
            float       zCeiling       = 10f,
            Vector3     nearSide       = default,
            Vector3     farSide        = default)
        {
            _descriptors.Add(new DangerAreaDescriptor
            {
                FeatureId      = Fnv1a32(featureName),
                ThreatRating   = threatRating,
                Kind           = kind,
                Center         = center,
                ExtentsXY      = extentsXY,
                AngleRad       = angleRad,
                ZFloor         = zFloor,
                ZCeiling       = zCeiling,
                NearSideHandle = nearSide,
                FarSideHandle  = farSide
            });
            return this;
        }

        /// <summary>
        /// Copies the pre-configured descriptors into <paramref name="dest"/>.
        /// Zero-alloc; ignores <paramref name="repo"/> and <paramref name="squadCommander"/>.
        /// </summary>
        public void Refresh(EntityRepository repo, Entity squadCommander,
                            Span<DangerAreaDescriptor> dest, out int count)
        {
            int n = Math.Min(_descriptors.Count, dest.Length);
            for (int i = 0; i < n; i++)
                dest[i] = _descriptors[i];
            count = n;
        }

        /// <summary>
        /// Computes the FNV-1a-32 hash of the UTF-8 encoding of <paramref name="text"/>.
        /// basis = 2166136261, prime = 16777619.
        /// </summary>
        public static uint Fnv1a32(string text)
        {
            uint hash = 2166136261u;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
