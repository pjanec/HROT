using System;
using System.Runtime.CompilerServices;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Deterministic RNG wrapper for HSM guards/actions.
    /// Uses XorShift32 algorithm.
    /// </summary>
    public unsafe ref struct HsmRng
    {
        private uint* _seedPtr;
        
        #if DEBUG
        private int* _debugAccessCount;
        #endif
        
        /// <summary>
        /// Create RNG wrapper from seed pointer.
        /// </summary>
        public HsmRng(uint* seedPtr)
        {
            _seedPtr = seedPtr;
            
            #if DEBUG
            _debugAccessCount = null;
            #endif
        }
        
        /// <summary>
        /// Create RNG with debug tracking.
        /// </summary>
        #if DEBUG
        public HsmRng(uint* seedPtr, int* debugAccessCount)
        {
            _seedPtr = seedPtr;
            _debugAccessCount = debugAccessCount;
        }
        #endif
        
        /// <summary>
        /// Generate random float [0, 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat()
        {
            uint x = *_seedPtr;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            *_seedPtr = x;
            
            #if DEBUG
            if (_debugAccessCount != null)
                (*_debugAccessCount)++;
            #endif
            
            // Convert to [0, 1) range
            return (x >> 8) * (1.0f / 16777216.0f);
        }
        
        /// <summary>
        /// Generate random int in range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int min, int max)
        {
            if (max <= min) return min;
            
            float f = NextFloat();
            return min + (int)(f * (max - min));
        }
        
        /// <summary>
        /// Generate random bool.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NextBool()
        {
            return NextFloat() >= 0.5f;
        }
        
        /// <summary>
        /// Get current seed value (for serialization/debugging).
        /// </summary>
        public uint CurrentSeed => *_seedPtr;
    }
}
