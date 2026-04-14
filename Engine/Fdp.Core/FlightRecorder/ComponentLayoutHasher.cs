using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fdp.Kernel.FlightRecorder
{
    /// <summary>
    /// Computes a deterministic FNV-1a 64-bit hash of a struct's memory layout.
    ///
    /// <para>
    /// The hash covers every instance field's name, declaring type name, and
    /// <see cref="Marshal.OffsetOf"/> byte offset.  It is deterministic within
    /// a given compilation: the hash changes if a field is added, removed,
    /// renamed, reordered, or its type changes — making it suitable as a
    /// Flight Recorder schema fingerprint.
    /// </para>
    ///
    /// <para>
    /// <b>Important:</b> <see cref="GetHashCode"/> is intentionally never used;
    /// it is not deterministic across process restarts.  Only <c>string</c> content
    /// and integer values are folded into the hash via the FNV algorithm.
    /// </para>
    /// </summary>
    public static class ComponentLayoutHasher
    {
        // FNV-1a 64-bit constants (named per §CODE-STANDARDS §1 — no magic numbers).
        private const ulong FnvPrime       = 0x00000100_000001B3UL;
        private const ulong FnvOffsetBasis = 0xCBF29CE4_84222325UL;

        /// <summary>
        /// Computes the FNV-1a 64-bit layout hash for <paramref name="type"/>.
        /// </summary>
        /// <param name="type">
        /// An unmanaged struct type whose layout you want to fingerprint.
        /// Must not be <c>null</c>.
        /// </param>
        /// <returns>
        /// A deterministic 64-bit hash that uniquely identifies the struct's field
        /// names, types, and memory offsets as seen by the current compiled binary.
        /// </returns>
        public static ulong ComputeHash(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            var hash = FnvOffsetBasis;

            // Enumerate fields in declaration order (stable within one compilation).
            var fields = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(f => f.MetadataToken)
                .ToArray();

            foreach (var field in fields)
            {
                // 1. Fold in the field name.
                hash = HashString(hash, field.Name);
                hash = FnvMix(hash, (byte)'|');

                // 2. Fold in the full type name of the field.
                hash = HashString(hash, field.FieldType.FullName ?? field.FieldType.Name);
                hash = FnvMix(hash, (byte)'|');

                // 3. Fold in the field's byte offset within the struct.
                //    This catches reordering even when names and types are unchanged.
                int offset = (int)Marshal.OffsetOf(type, field.Name);
                hash = HashInt(hash, offset);
            }

            return hash;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static ulong HashString(ulong hash, string str)
        {
            foreach (char ch in str)
            {
                // FNV mixes one byte at a time; cast char to byte (low byte only for ASCII).
                hash = FnvMix(hash, (byte)ch);

                // Handle multi-byte characters by also hashing the high byte.
                if (ch > 0xFF)
                    hash = FnvMix(hash, (byte)(ch >> 8));
            }
            return hash;
        }

        private static ulong HashInt(ulong hash, int value)
        {
            // Mix all four bytes of the integer into the hash (little-endian order).
            hash = FnvMix(hash, (byte)(value        & 0xFF));
            hash = FnvMix(hash, (byte)((value >>  8) & 0xFF));
            hash = FnvMix(hash, (byte)((value >> 16) & 0xFF));
            hash = FnvMix(hash, (byte)((value >> 24) & 0xFF));
            return hash;
        }

        /// <summary>XOR the byte into the hash then multiply by the prime (core FNV-1a step).</summary>
        private static ulong FnvMix(ulong hash, byte b)
        {
            hash ^= b;
            hash *= FnvPrime;
            return hash;
        }
    }
}
