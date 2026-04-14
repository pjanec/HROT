using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace Fdp.Kernel
{
    /// <summary>
    /// Fixed-size 64-byte string for zero-allocation string storage.
    /// Stores up to 63 UTF-8 bytes + 1 null terminator.
    /// Safe to use in components and network messages.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public unsafe struct FixedString64 : IEquatable<FixedString64>
    {
        private fixed byte _data[64];
        
        /// <summary>
        /// Maximum string length (63 chars + null terminator).
        /// </summary>
        public const int MaxLength = 63;
        
        /// <summary>
        /// Creates a FixedString64 from a regular string.
        /// Truncates if longer than MaxLength.
        /// </summary>
        public FixedString64(string str)
        {
            this = default;
            if (string.IsNullOrEmpty(str)) return;
            
            ref byte start = ref Unsafe.As<FixedString64, byte>(ref this);
            Span<byte> buffer = MemoryMarshal.CreateSpan(ref start, 64);
            
            var encoder = Encoding.UTF8.GetEncoder();
            encoder.Convert(str.AsSpan(), buffer.Slice(0, MaxLength), true, out _, out int bytesUsed, out _);
            buffer[bytesUsed] = 0;
        }
        
        /// <summary>
        /// Converts to a regular string.
        /// </summary>
        public override readonly string ToString()
        {
            ref byte start = ref Unsafe.As<FixedString64, byte>(ref Unsafe.AsRef(in this));
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref start, 64);
            
            int len = 0;
            while (len < MaxLength && span[len] != 0) len++;
            if (len == 0) return string.Empty;
            
            return Encoding.UTF8.GetString(span.Slice(0, len));
        }
        
        /// <summary>
        /// Gets the current length in bytes.
        /// </summary>
        public readonly int Length
        {
            get
            {
                ref byte start = ref Unsafe.As<FixedString64, byte>(ref Unsafe.AsRef(in this));
                ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref start, 64);
                int len = 0;
                while (len < MaxLength && span[len] != 0) len++;
                return len;
            }
        }
        
        /// <summary>
        /// Checks if the string is empty.
        /// </summary>
        public readonly bool IsEmpty => Length == 0;
        
        /// <summary>
        /// Clears the string.
        /// </summary>
        public void Clear()
        {
            ref byte start = ref Unsafe.As<FixedString64, byte>(ref this);
            Unsafe.InitBlock(ref start, 0, 64);
        }
        
        public readonly bool Equals(FixedString64 other)
        {
            ref byte start1 = ref Unsafe.As<FixedString64, byte>(ref Unsafe.AsRef(in this));
            ref byte start2 = ref Unsafe.As<FixedString64, byte>(ref Unsafe.AsRef(in other));
            
            var span1 = MemoryMarshal.CreateReadOnlySpan(ref start1, 64);
            var span2 = MemoryMarshal.CreateReadOnlySpan(ref start2, 64);
            
            return span1.SequenceEqual(span2);
        }
        
        public override readonly bool Equals(object? obj)
        {
            return obj is FixedString64 other && Equals(other);
        }
        
        public override readonly int GetHashCode()
        {
            ref byte start = ref Unsafe.As<FixedString64, byte>(ref Unsafe.AsRef(in this));
            var span = MemoryMarshal.CreateReadOnlySpan(ref start, 64);
            
            int hash = 17;
            for (int i = 0; i < 64; i++)
            {
                hash = hash * 31 + span[i];
            }
            return hash;
        }
        
        public static bool operator ==(FixedString64 left, FixedString64 right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(FixedString64 left, FixedString64 right)
        {
            return !left.Equals(right);
        }
        
        public static implicit operator string(FixedString64 str)
        {
            return str.ToString();
        }
        
        public static implicit operator FixedString64(string str)
        {
            return new FixedString64(str);
        }
    }
}
