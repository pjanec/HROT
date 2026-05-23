using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Fdp.Core
{
    /// <summary>
    /// 512-bit bitmask, exactly 64 bytes (one L1 cache line).
    /// Used as the entity component-presence and authority mask for entities.
    /// Replaces BitMask256 as the component mask type to support up to 512 component types.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct BitMask512 : IEquatable<BitMask512>
    {
        [FieldOffset( 0)] private ulong _q0;
        [FieldOffset( 8)] private ulong _q1;
        [FieldOffset(16)] private ulong _q2;
        [FieldOffset(24)] private ulong _q3;
        [FieldOffset(32)] private ulong _q4;
        [FieldOffset(40)] private ulong _q5;
        [FieldOffset(48)] private ulong _q6;
        [FieldOffset(56)] private ulong _q7;

        // ----------------------------------------------------------
        // BIT MANIPULATION (Scalar)
        // ----------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int bitIndex)
        {
#if FDP_PARANOID_MODE
            if (bitIndex < 0 || bitIndex >= 512)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));
#endif
            int quadIndex = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 0x3F);
            switch (quadIndex)
            {
                case 0: _q0 |= mask; break;
                case 1: _q1 |= mask; break;
                case 2: _q2 |= mask; break;
                case 3: _q3 |= mask; break;
                case 4: _q4 |= mask; break;
                case 5: _q5 |= mask; break;
                case 6: _q6 |= mask; break;
                case 7: _q7 |= mask; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int bitIndex)
        {
#if FDP_PARANOID_MODE
            if (bitIndex < 0 || bitIndex >= 512)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));
#endif
            int quadIndex = bitIndex >> 6;
            ulong mask = ~(1UL << (bitIndex & 0x3F));
            switch (quadIndex)
            {
                case 0: _q0 &= mask; break;
                case 1: _q1 &= mask; break;
                case 2: _q2 &= mask; break;
                case 3: _q3 &= mask; break;
                case 4: _q4 &= mask; break;
                case 5: _q5 &= mask; break;
                case 6: _q6 &= mask; break;
                case 7: _q7 &= mask; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsSet(int bitIndex)
        {
#if FDP_PARANOID_MODE
            if (bitIndex < 0 || bitIndex >= 512)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));
#endif
            int quadIndex = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 0x3F);
            return quadIndex switch
            {
                0 => (_q0 & mask) != 0,
                1 => (_q1 & mask) != 0,
                2 => (_q2 & mask) != 0,
                3 => (_q3 & mask) != 0,
                4 => (_q4 & mask) != 0,
                5 => (_q5 & mask) != 0,
                6 => (_q6 & mask) != 0,
                7 => (_q7 & mask) != 0,
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _q0 = 0; _q1 = 0; _q2 = 0; _q3 = 0;
            _q4 = 0; _q5 = 0; _q6 = 0; _q7 = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAll()
        {
            _q0 = ~0UL; _q1 = ~0UL; _q2 = ~0UL; _q3 = ~0UL;
            _q4 = ~0UL; _q5 = ~0UL; _q6 = ~0UL; _q7 = ~0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsEmpty()
        {
            return (_q0 | _q1 | _q2 | _q3 | _q4 | _q5 | _q6 | _q7) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BitwiseAnd(in BitMask512 other)
        {
            _q0 &= other._q0; _q1 &= other._q1; _q2 &= other._q2; _q3 &= other._q3;
            _q4 &= other._q4; _q5 &= other._q5; _q6 &= other._q6; _q7 &= other._q7;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BitwiseOr(in BitMask512 other)
        {
            _q0 |= other._q0; _q1 |= other._q1; _q2 |= other._q2; _q3 |= other._q3;
            _q4 |= other._q4; _q5 |= other._q5; _q6 |= other._q6; _q7 |= other._q7;
        }

        // ----------------------------------------------------------
        // QUERY OPERATIONS (AVX2 optimized, two-stage lower/upper)
        // ----------------------------------------------------------

        /// <summary>
        /// Returns true if all bits in <paramref name="required"/> are set in <paramref name="source"/>.
        /// AVX2 path: lower 256 bits checked first; upper 256 bits only if lower passes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAll(in BitMask512 source, in BitMask512 required)
        {
            if (Avx.IsSupported)
                return Avx2HasAll(in source, in required);
            return ScalarHasAll(in source, in required);
        }

        /// <summary>
        /// Returns true if any bit in <paramref name="test"/> is also set in <paramref name="source"/>.
        /// AVX2 path: lower 256 bits checked first; upper 256 bits only if lower does not already match.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAny(in BitMask512 source, in BitMask512 test)
        {
            if (Avx.IsSupported)
                return Avx2HasAny(in source, in test);
            return ScalarHasAny(in source, in test);
        }

        /// <summary>
        /// Returns true when:
        ///   (target &amp; include) == include  AND  (target &amp; exclude) == 0.
        /// AVX2 path: lower 256 bits checked first (early return on mismatch); upper 256 bits second.
        /// Scalar path: interleaved include/exclude checks per quad, lower-half first.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Matches(in BitMask512 target, in BitMask512 include, in BitMask512 exclude)
        {
            if (Avx.IsSupported)
                return Avx2Matches(in target, in include, in exclude);
            return ScalarMatches(in target, in include, in exclude);
        }

        // ----------------------------------------------------------
        // PHASE-2 COMPATIBILITY: Compare BitMask256 entity masks against
        // BitMask512 query masks (EntityIndex still stores 256-bit masks).
        // ----------------------------------------------------------

        /// <summary>
        /// Returns true if all bits in <paramref name="required"/> are present in the 256-bit
        /// <paramref name="source"/>. Any required bit at position 256 or above causes an
        /// immediate false result since BitMask256 cannot hold those positions.
        /// Used during Phase 2 before EntityIndex is upgraded to BitMask512.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAll(in BitMask256 source, in BitMask512 required)
        {
            // Any upper-half requirement can never be met by a BitMask256 source.
            if ((required._q4 | required._q5 | required._q6 | required._q7) != 0)
                return false;
            if (Avx.IsSupported)
            {
                ref BitMask256 mutableSource   = ref Unsafe.AsRef(in source);
                ref BitMask512 mutableRequired = ref Unsafe.AsRef(in required);
                Vector256<ulong> vSrc = Unsafe.As<BitMask256, Vector256<ulong>>(ref mutableSource);
                Vector256<ulong> vReq = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableRequired);
                return Avx.TestC(vSrc.AsByte(), vReq.AsByte());
            }
            ref byte sb = ref Unsafe.As<BitMask256, byte>(ref Unsafe.AsRef(in source));
            ulong sq0 = Unsafe.ReadUnaligned<ulong>(ref sb);
            ulong sq1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref sb, (nint)8));
            ulong sq2 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref sb, (nint)16));
            ulong sq3 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref sb, (nint)24));
            return (sq0 & required._q0) == required._q0
                && (sq1 & required._q1) == required._q1
                && (sq2 & required._q2) == required._q2
                && (sq3 & required._q3) == required._q3;
        }

        /// <summary>
        /// Returns true if any bit in <paramref name="test"/> is also set in the 256-bit
        /// <paramref name="source"/>. Bits at positions 256 and above in <paramref name="test"/>
        /// are ignored since BitMask256 cannot hold those positions.
        /// Used during Phase 2 before EntityIndex is upgraded to BitMask512.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAny(in BitMask256 source, in BitMask512 test)
        {
            if (Avx.IsSupported)
            {
                ref BitMask256 mutableSource = ref Unsafe.AsRef(in source);
                ref BitMask512 mutableTest   = ref Unsafe.AsRef(in test);
                Vector256<ulong> vSrc  = Unsafe.As<BitMask256, Vector256<ulong>>(ref mutableSource);
                Vector256<ulong> vTest = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableTest);
                return !Avx.TestZ(vSrc.AsByte(), vTest.AsByte());
            }
            ref byte sb = ref Unsafe.As<BitMask256, byte>(ref Unsafe.AsRef(in source));
            ulong sq0 = Unsafe.ReadUnaligned<ulong>(ref sb);
            ulong sq1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref sb, (nint)8));
            ulong sq2 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref sb, (nint)16));
            ulong sq3 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref sb, (nint)24));
            return ((sq0 & test._q0) | (sq1 & test._q1) | (sq2 & test._q2) | (sq3 & test._q3)) != 0;
        }

        // ----------------------------------------------------------
        // AVX2 PATHS
        // ----------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Avx2HasAll(in BitMask512 source, in BitMask512 required)
        {
            ref BitMask512 mutableSource   = ref Unsafe.AsRef(in source);
            ref BitMask512 mutableRequired = ref Unsafe.AsRef(in required);

            // Lower 256 bits (q0-q3)
            Vector256<ulong> vSrcLo  = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableSource);
            Vector256<ulong> vReqLo  = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableRequired);
            // Avx.TestC: (vSrcLo & ~vReqLo) == 0  <=>  all bits in vReqLo are set in vSrcLo
            if (!Avx.TestC(vSrcLo.AsByte(), vReqLo.AsByte()))
                return false;

            // Upper 256 bits (q4-q7) — only reached if lower half passed
            ref byte srcBytes = ref Unsafe.As<BitMask512, byte>(ref mutableSource);
            ref byte reqBytes = ref Unsafe.As<BitMask512, byte>(ref mutableRequired);
            Vector256<ulong> vSrcHi  = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref srcBytes, (nint)32));
            Vector256<ulong> vReqHi  = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref reqBytes, (nint)32));
            return Avx.TestC(vSrcHi.AsByte(), vReqHi.AsByte());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Avx2HasAny(in BitMask512 source, in BitMask512 test)
        {
            ref BitMask512 mutableSource = ref Unsafe.AsRef(in source);
            ref BitMask512 mutableTest   = ref Unsafe.AsRef(in test);

            // Lower 256 bits (q0-q3)
            Vector256<ulong> vSrcLo  = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableSource);
            Vector256<ulong> vTestLo = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableTest);
            // !Avx.TestZ: (vSrcLo & vTestLo) != 0
            if (!Avx.TestZ(vSrcLo.AsByte(), vTestLo.AsByte()))
                return true;

            // Upper 256 bits (q4-q7)
            ref byte srcBytes  = ref Unsafe.As<BitMask512, byte>(ref mutableSource);
            ref byte testBytes = ref Unsafe.As<BitMask512, byte>(ref mutableTest);
            Vector256<ulong> vSrcHi  = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref srcBytes,  (nint)32));
            Vector256<ulong> vTestHi = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref testBytes, (nint)32));
            return !Avx.TestZ(vSrcHi.AsByte(), vTestHi.AsByte());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Avx2Matches(in BitMask512 target, in BitMask512 include, in BitMask512 exclude)
        {
            ref BitMask512 mutableTarget  = ref Unsafe.AsRef(in target);
            ref BitMask512 mutableInclude = ref Unsafe.AsRef(in include);
            ref BitMask512 mutableExclude = ref Unsafe.AsRef(in exclude);

            // Lower 256 bits (q0-q3)
            Vector256<ulong> vTgtLo = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableTarget);
            Vector256<ulong> vIncLo = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableInclude);
            Vector256<ulong> vExcLo = Unsafe.As<BitMask512, Vector256<ulong>>(ref mutableExclude);

            // HasAll lower: all required bits set
            if (!Avx.TestC(vTgtLo.AsByte(), vIncLo.AsByte()))
                return false;
            // HasNone lower: no excluded bits set
            if (!Avx.TestZ(vTgtLo.AsByte(), vExcLo.AsByte()))
                return false;

            // Upper 256 bits (q4-q7) — only reached if lower half passed
            ref byte tgtBytes = ref Unsafe.As<BitMask512, byte>(ref mutableTarget);
            ref byte incBytes = ref Unsafe.As<BitMask512, byte>(ref mutableInclude);
            ref byte excBytes = ref Unsafe.As<BitMask512, byte>(ref mutableExclude);
            Vector256<ulong> vTgtHi = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref tgtBytes, (nint)32));
            Vector256<ulong> vIncHi = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref incBytes, (nint)32));
            Vector256<ulong> vExcHi = Unsafe.As<byte, Vector256<ulong>>(ref Unsafe.AddByteOffset(ref excBytes, (nint)32));

            if (!Avx.TestC(vTgtHi.AsByte(), vIncHi.AsByte()))
                return false;
            return Avx.TestZ(vTgtHi.AsByte(), vExcHi.AsByte());
        }

        // ----------------------------------------------------------
        // SCALAR FALLBACKS
        // ----------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ScalarHasAll(in BitMask512 s, in BitMask512 r)
        {
            return (s._q0 & r._q0) == r._q0 && (s._q1 & r._q1) == r._q1 &&
                   (s._q2 & r._q2) == r._q2 && (s._q3 & r._q3) == r._q3 &&
                   (s._q4 & r._q4) == r._q4 && (s._q5 & r._q5) == r._q5 &&
                   (s._q6 & r._q6) == r._q6 && (s._q7 & r._q7) == r._q7;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ScalarHasAny(in BitMask512 s, in BitMask512 t)
        {
            return ((s._q0 & t._q0) | (s._q1 & t._q1) | (s._q2 & t._q2) | (s._q3 & t._q3) |
                    (s._q4 & t._q4) | (s._q5 & t._q5) | (s._q6 & t._q6) | (s._q7 & t._q7)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ScalarMatches(in BitMask512 t, in BitMask512 i, in BitMask512 e)
        {
            // Lower half first, interleaved include/exclude checks per quad
            if ((t._q0 & i._q0) != i._q0) return false;
            if ((t._q0 & e._q0) != 0)      return false;
            if ((t._q1 & i._q1) != i._q1) return false;
            if ((t._q1 & e._q1) != 0)      return false;
            if ((t._q2 & i._q2) != i._q2) return false;
            if ((t._q2 & e._q2) != 0)      return false;
            if ((t._q3 & i._q3) != i._q3) return false;
            if ((t._q3 & e._q3) != 0)      return false;
            // Upper half
            if ((t._q4 & i._q4) != i._q4) return false;
            if ((t._q4 & e._q4) != 0)      return false;
            if ((t._q5 & i._q5) != i._q5) return false;
            if ((t._q5 & e._q5) != 0)      return false;
            if ((t._q6 & i._q6) != i._q6) return false;
            if ((t._q6 & e._q6) != 0)      return false;
            if ((t._q7 & i._q7) != i._q7) return false;
            if ((t._q7 & e._q7) != 0)      return false;
            return true;
        }

        // ----------------------------------------------------------
        // EQUALITY & UTILITIES
        // ----------------------------------------------------------

        public readonly bool Equals(BitMask512 other)
        {
            return _q0 == other._q0 && _q1 == other._q1 && _q2 == other._q2 && _q3 == other._q3 &&
                   _q4 == other._q4 && _q5 == other._q5 && _q6 == other._q6 && _q7 == other._q7;
        }

        public override readonly bool Equals(object? obj) => obj is BitMask512 other && Equals(other);

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(_q0, _q1, _q2, _q3, _q4, _q5, _q6, _q7);
        }

        public static bool operator ==(BitMask512 left, BitMask512 right) => left.Equals(right);
        public static bool operator !=(BitMask512 left, BitMask512 right) => !left.Equals(right);
    }
}
