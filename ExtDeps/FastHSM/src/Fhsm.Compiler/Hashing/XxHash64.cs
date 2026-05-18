using System;

namespace Fhsm.Compiler.Hashing
{
    /// <summary>
    /// XxHash64 implementation for structure/parameter hashing.
    /// Faster and better avalanche than SHA256.
    /// </summary>
    public static class XxHash64
    {
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 = 1609587929392839161UL;
        private const ulong Prime4 = 9650029242287828579UL;
        private const ulong Prime5 = 2870177450012600261UL;
        
        public static ulong ComputeHash(ReadOnlySpan<byte> data, ulong seed = 0)
        {
            ulong hash;
            int remaining = data.Length;
            int offset = 0;
            
            if (remaining >= 32)
            {
                ulong v1 = seed + Prime1 + Prime2;
                ulong v2 = seed + Prime2;
                ulong v3 = seed;
                ulong v4 = seed - Prime1;
                
                do
                {
                    v1 = Round(v1, ReadUInt64(data, offset)); offset += 8;
                    v2 = Round(v2, ReadUInt64(data, offset)); offset += 8;
                    v3 = Round(v3, ReadUInt64(data, offset)); offset += 8;
                    v4 = Round(v4, ReadUInt64(data, offset)); offset += 8;
                    remaining -= 32;
                } while (remaining >= 32);
                
                hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                hash = MergeRound(hash, v1);
                hash = MergeRound(hash, v2);
                hash = MergeRound(hash, v3);
                hash = MergeRound(hash, v4);
            }
            else
            {
                hash = seed + Prime5;
            }
            
            hash += (ulong)data.Length;
            
            while (remaining >= 8)
            {
                hash ^= Round(0, ReadUInt64(data, offset));
                hash = RotateLeft(hash, 27) * Prime1 + Prime4;
                offset += 8;
                remaining -= 8;
            }
            
            if (remaining >= 4)
            {
                hash ^= ReadUInt32(data, offset) * Prime1;
                hash = RotateLeft(hash, 23) * Prime2 + Prime3;
                offset += 4;
                remaining -= 4;
            }
            
            while (remaining > 0)
            {
                hash ^= data[offset] * Prime5;
                hash = RotateLeft(hash, 11) * Prime1;
                offset++;
                remaining--;
            }
            
            // Avalanche
            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            
            return hash;
        }
        
        private static ulong Round(ulong acc, ulong input)
        {
            acc += input * Prime2;
            acc = RotateLeft(acc, 31);
            acc *= Prime1;
            return acc;
        }
        
        private static ulong MergeRound(ulong acc, ulong val)
        {
            val = Round(0, val);
            acc ^= val;
            acc = acc * Prime1 + Prime4;
            return acc;
        }
        
        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
        
        private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
        {
            return BitConverter.ToUInt64(data.Slice(offset, 8));
        }
        
        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        {
            return BitConverter.ToUInt32(data.Slice(offset, 4));
        }
    }
}
