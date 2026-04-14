using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Fdp.Kernel
{
    /// <summary>
    /// 256-bit bitmask optimized for AVX2.
    /// Used for component existence, authority, and query filtering.
    /// CRITICAL: Must be 32-byte aligned when embedded in EntityHeader.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32, Pack = 32)]
    public struct BitMask256 : IEquatable<BitMask256>
    {
        // 4 x 64-bit = 256 bits
        [FieldOffset(0)] private ulong _q0;
        [FieldOffset(8)] private ulong _q1;
        [FieldOffset(16)] private ulong _q2;
        [FieldOffset(24)] private ulong _q3;
        
        // ----------------------------------------------------------
        // BIT MANIPULATION (Scalar)
        // ----------------------------------------------------------
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int bitIndex)
        {
            #if FDP_PARANOID_MODE
            if (bitIndex < 0 || bitIndex >= 256)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));
            #endif
            
            int quadIndex = bitIndex >> 6;      // Divide by 64
            int bitOffset = bitIndex & 0x3F;    // Modulo 64
            ulong mask = 1UL << bitOffset;
            
            switch (quadIndex)
            {
                case 0: _q0 |= mask; break;
                case 1: _q1 |= mask; break;
                case 2: _q2 |= mask; break;
                case 3: _q3 |= mask; break;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int bitIndex)
        {
            #if FDP_PARANOID_MODE
            if (bitIndex < 0 || bitIndex >= 256)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));
            #endif
            
            int quadIndex = bitIndex >> 6;
            int bitOffset = bitIndex & 0x3F;
            ulong mask = ~(1UL << bitOffset);
            
            switch (quadIndex)
            {
                case 0: _q0 &= mask; break;
                case 1: _q1 &= mask; break;
                case 2: _q2 &= mask; break;
                case 3: _q3 &= mask; break;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsSet(int bitIndex)
        {
            #if FDP_PARANOID_MODE
            if (bitIndex < 0 || bitIndex >= 256)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));
            #endif
            
            int quadIndex = bitIndex >> 6;
            int bitOffset = bitIndex & 0x3F;
            ulong mask = 1UL << bitOffset;
            
            return quadIndex switch
            {
                0 => (_q0 & mask) != 0,
                1 => (_q1 & mask) != 0,
                2 => (_q2 & mask) != 0,
                3 => (_q3 & mask) != 0,
                _ => false
            };
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _q0 = 0;
            _q1 = 0;
            _q2 = 0;
            _q3 = 0;
        }
        
        /// <summary>
        /// Sets all bits to 1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAll()
        {
            _q0 = ~0UL;
            _q1 = ~0UL;
            _q2 = ~0UL;
            _q3 = ~0UL;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly unsafe bool IsEmpty()
        {
            // Use scalar path for stack-allocated instances (may not be aligned)
            // When embedded in EntityHeader (heap/static), alignment is guaranteed
            return (_q0 | _q1 | _q2 | _q3) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BitwiseAnd(in BitMask256 other)
        {
            _q0 &= other._q0;
            _q1 &= other._q1;
            _q2 &= other._q2;
            _q3 &= other._q3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BitwiseOr(in BitMask256 other)
        {
            _q0 |= other._q0;
            _q1 |= other._q1;
            _q2 |= other._q2;
            _q3 |= other._q3;
        }
        
        // ----------------------------------------------------------
        // QUERY OPERATIONS (AVX2 Optimized)
        // ----------------------------------------------------------
        
        /// <summary>
        /// Checks if 'target' matches the query criteria.
        /// Required: (target & include) == include AND (target & exclude) == 0
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Matches(in BitMask256 target, in BitMask256 include, in BitMask256 exclude)
        {
            // Check if hardware supports AVX2 (Checked by JIT at startup, essentially 0 cost constant)
            if (Avx2.IsSupported)
            {
                return Avx2Matches(target, include, exclude);
            }

            // Fallback for older CPUs
            return ScalarMatches(target, include, exclude);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool Avx2Matches(in BitMask256 target, in BitMask256 include, in BitMask256 exclude)
        {
            // 1. UNSAFE LOAD: Solves the "alignment issues on stack"
            // We treat the struct references as pointers to Vector256. 
            // LoadUnsafe emits 'vmovdqu' which tolerates unaligned stack memory.
            Vector256<ulong> vTarget = Unsafe.As<BitMask256, Vector256<ulong>>(ref Unsafe.AsRef(in target));
            Vector256<ulong> vInclude = Unsafe.As<BitMask256, Vector256<ulong>>(ref Unsafe.AsRef(in include));
            Vector256<ulong> vExclude = Unsafe.As<BitMask256, Vector256<ulong>>(ref Unsafe.AsRef(in exclude));

            // 2. LOGIC: (target & include == include) AND (target & exclude == 0)
            
            // "Has All" Check: (target & include)
            Vector256<ulong> hasAllAnd = Avx2.And(vTarget, vInclude);
            // Compare equality: Result is 0xFF... if true, 0x00... if false (per element)
            Vector256<ulong> hasAllCmp = Avx2.CompareEqual(hasAllAnd, vInclude);

            // "Has None" Check: (target & exclude)
            Vector256<ulong> hasNoneAnd = Avx2.And(vTarget, vExclude);
            // Compare against Zero
            Vector256<ulong> hasNoneCmp = Avx2.CompareEqual(hasNoneAnd, Vector256<ulong>.Zero);

            // 3. COMBINE: Both conditions must be true for all bits
            Vector256<ulong> finalVec = Avx2.And(hasAllCmp, hasNoneCmp);

            // 4. MOVEMASK: Collapse the 256-bit result into a single 32-bit integer.
            // MoveMask extracts the high bit of every byte. 
            // If all checks passed, every byte is 0xFF, so MoveMask returns 0xFFFFFFFF (-1).
            int mask = Avx2.MoveMask(finalVec.AsByte());
            
            return mask == -1; 
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAll(in BitMask256 source, in BitMask256 required)
        {
            return ScalarHasAll(source, required);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAny(in BitMask256 source, in BitMask256 test)
        {
            return ScalarHasAny(source, test);
        }
        
        // ----------------------------------------------------------
        // SCALAR FALLBACKS (Never branched at runtime thanks to JIT)
        // ----------------------------------------------------------
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ScalarMatches(in BitMask256 t, in BitMask256 i, in BitMask256 e)
        {
            // Check all 4 quads
            if ((t._q0 & i._q0) != i._q0) return false;
            if ((t._q0 & e._q0) != 0) return false;
            
            if ((t._q1 & i._q1) != i._q1) return false;
            if ((t._q1 & e._q1) != 0) return false;
            
            if ((t._q2 & i._q2) != i._q2) return false;
            if ((t._q2 & e._q2) != 0) return false;
            
            if ((t._q3 & i._q3)!= i._q3) return false;
            if ((t._q3 & e._q3) != 0) return false;
            
            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ScalarHasAll(in BitMask256 s, in BitMask256 r)
        {
            return (s._q0 & r._q0) == r._q0 &&
                   (s._q1 & r._q1) == r._q1 &&
                   (s._q2 & r._q2) == r._q2 &&
                   (s._q3 & r._q3) == r._q3;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ScalarHasAny(in BitMask256 s, in BitMask256 t)
        {
            return ((s._q0 & t._q0) | (s._q1 & t._q1) | (s._q2 & t._q2) | (s._q3 & t._q3)) != 0;
        }
        
        // ----------------------------------------------------------
        // EQUALITY & UTILITIES
        // ----------------------------------------------------------
        
        public readonly bool Equals(BitMask256 other)
        {
            return _q0 == other._q0 && _q1 == other._q1 && _q2 == other._q2 && _q3 == other._q3;
        }
        
        public override readonly bool Equals(object? obj) => obj is BitMask256 other && Equals(other);
        
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(_q0, _q1, _q2, _q3);
        }
        
        public static bool operator ==(BitMask256 left, BitMask256 right) => left.Equals(right);
        public static bool operator !=(BitMask256 left, BitMask256 right) => !left.Equals(right);


    }
}
