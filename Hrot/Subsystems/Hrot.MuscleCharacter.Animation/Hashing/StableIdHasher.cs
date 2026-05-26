using System;
using System.Text;

namespace Hrot.MuscleCharacter.Animation.Hashing
{
    /// <summary>
    /// Deterministic hashing utilities for animation IDs (DD-4 §3).
    /// Computes stable montage and marker hashes using FNV1a to ensure
    /// consistent IDs across runs, machines, and rebuilds.
    /// </summary>
    public static class StableIdHasher
    {
        /// <summary>
        /// FNV1a 32-bit offset basis.
        /// </summary>
        private const uint FNV1a32Prime = 16777619u;

        /// <summary>
        /// FNV1a 32-bit offset basis.
        /// </summary>
        private const uint FNV1a32Basis = 2166136261u;

        /// <summary>
        /// FNV1a 64-bit offset basis.
        /// </summary>
        private const ulong FNV1a64Prime = 1099511628211ul;

        /// <summary>
        /// FNV1a 64-bit basis constant.
        /// </summary>
        private const ulong FNV1a64Basis = 14695981039346656037ul;

        /// <summary>
        /// Compute a stable montage ID from a name using FNV1a64.
        /// Returns a signed 32-bit positive int [0, 0x7FFFFFFF).
        /// Deterministic: same name → same ID across runs / machines.
        ///
        /// Per DD-4 §3.1, collision risk is negligible at character-class scale
        /// (typical character class has under 100 montages).
        /// </summary>
        /// <param name="montageName">The montage's stable string name.</param>
        /// <returns>Signed 32-bit positive montage asset ID.</returns>
        public static int ComputeMontageAssetId(string montageName)
        {
            if (string.IsNullOrEmpty(montageName))
                throw new ArgumentException("Montage name cannot be null or empty.", nameof(montageName));

            ulong hash = Fnv1a64(montageName);
            // Mask to 31 bits to keep as positive signed int
            return (int)(hash & 0x7FFFFFFFul);
        }

        /// <summary>
        /// Compute a marker hash from a name using FNV1a32.
        /// Returns an unsigned 32-bit hash used by AnimNotifyEvent.MarkerHash.
        /// Deterministic: same name → same hash across runs / machines.
        ///
        /// Per DD-4 §3.4, if two characters use markers with the same name,
        /// they collide on the same hash (by design).
        /// </summary>
        /// <param name="markerName">The marker's stable string name.</param>
        /// <returns>Unsigned 32-bit marker hash.</returns>
        public static uint ComputeMarkerHash(string markerName)
        {
            if (string.IsNullOrEmpty(markerName))
                throw new ArgumentException("Marker name cannot be null or empty.", nameof(markerName));

            return Fnv1a32(markerName);
        }

        /// <summary>
        /// Compute FNV1a32 hash of a string.
        /// </summary>
        private static uint Fnv1a32(string input)
        {
            uint hash = FNV1a32Basis;
            byte[] bytes = Encoding.UTF8.GetBytes(input);

            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= FNV1a32Prime;
            }

            return hash;
        }

        /// <summary>
        /// Compute FNV1a64 hash of a string.
        /// </summary>
        private static ulong Fnv1a64(string input)
        {
            ulong hash = FNV1a64Basis;
            byte[] bytes = Encoding.UTF8.GetBytes(input);

            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= FNV1a64Prime;
            }

            return hash;
        }
    }
}
