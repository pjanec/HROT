using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ A string hash that is the SAME in every process. ⛔ <c>string.GetHashCode()</c> is randomised per process in .NET, so an id
    /// derived from it differs between two runs of one build — a recording written by one process cannot be read by another, and
    /// a fresh process does not repeat the last one (CE-4043, CE-3302).
    /// <para>FNV-1a-32 over the chars, each XOR'd whole (basis 2166136261, prime 16777619) — the same function as
    /// <c>UtilityIdHash.Fnv1a32</c> (the analyzers' linked copy) and the toolkits' other copies, which may route here.</para>
    /// </summary>
    public static class StableHash
    {
        public static uint Fnv1a32(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        /// <summary>The FNV-1a offset basis — the start of an <see cref="Add(uint, int)"/> chain.</summary>
        public const uint Basis = 2166136261u;

        /// <summary>⭐ CE-3303 — folds an <c>int</c> (its four bytes, low first) into an FNV-1a hash: the stable replacement for
        /// <c>System.HashCode</c>, whose seed is random per process.</summary>
        public static uint Add(uint hash, int value)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (byte)(value >> (8 * i));
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        /// <summary>⭐ CE-3303 — folds a <c>float</c> by its bits.</summary>
        public static uint Add(uint hash, float value) => Add(hash, BitConverter.SingleToInt32Bits(value));

        /// <summary>⭐ CE-3303 — three ints combined, the same in every process (where <c>HashCode.Combine</c> is not).</summary>
        public static int Combine(int a, int b, int c) => unchecked((int)Add(Add(Add(Basis, a), b), c));

        /// <summary>A non-negative <c>int</c> id: <see cref="Fnv1a32"/> with the sign bit cleared.</summary>
        public static int NonNegativeId(string s) => unchecked((int)(Fnv1a32(s) & 0x7FFFFFFF));
    }
}
