using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace Fdp.Kernel
{
    /// <summary>
    /// Fixed-size 32-byte string for zero-allocation string storage.
    /// Stores up to 31 UTF-8 bytes + 1 null terminator.
    /// Safe to use in components and network messages.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public unsafe struct FixedString32 : IEquatable<FixedString32>
    {
        private fixed byte _data[32];
        
        /// <summary>
        /// Maximum string length (31 chars + null terminator).
        /// </summary>
        public const int MaxLength = 31;
        
        /// <summary>
        /// Creates a FixedString32 from a regular string.
        /// Truncates if longer than MaxLength.
        /// </summary>
        public FixedString32(string str)
        {
            this = default;
            if (string.IsNullOrEmpty(str)) return;
            
            ref byte start = ref Unsafe.As<FixedString32, byte>(ref this);
            Span<byte> buffer = MemoryMarshal.CreateSpan(ref start, 32);
            
            var encoder = Encoding.UTF8.GetEncoder();
            encoder.Convert(str.AsSpan(), buffer.Slice(0, MaxLength), true, out _, out int bytesUsed, out _);
            buffer[bytesUsed] = 0;
        }
        
        /// <summary>
        /// Converts to a regular string.
        /// </summary>
        public override readonly string ToString()
        {
            ref byte start = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in this));
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref start, 32);
            
            int len = 0;
            while (len < MaxLength && span[len] != 0) len++;
            if (len == 0) return string.Empty;
            
            return Encoding.UTF8.GetString(span.Slice(0, len));
        }
        
        /// <summary>
        /// Gets the current length in bytes (not characters).
        /// </summary>
        public readonly int Length
        {
            get
            {
                ref byte start = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in this));
                ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref start, 32);
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
            ref byte start = ref Unsafe.As<FixedString32, byte>(ref this);
            Unsafe.InitBlock(ref start, 0, 32);
        }
        
        /// <summary>
        /// Copies from another FixedString32.
        /// </summary>
        public void CopyFrom(in FixedString32 other)
        {
            ref byte dest = ref Unsafe.As<FixedString32, byte>(ref this);
            ref byte src = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in other));
            Unsafe.CopyBlock(ref dest, ref src, 32);
        }
        
        public readonly bool Equals(FixedString32 other)
        {
            ref byte start1 = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in this));
            ref byte start2 = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in other));
            
            var span1 = MemoryMarshal.CreateReadOnlySpan(ref start1, 32);
            var span2 = MemoryMarshal.CreateReadOnlySpan(ref start2, 32);
            
            return span1.SequenceEqual(span2);
        }
        
        public override readonly bool Equals(object? obj)
        {
            return obj is FixedString32 other && Equals(other);
        }
        
        public override readonly int GetHashCode()
        {
            ref byte start = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in this));
            var span = MemoryMarshal.CreateReadOnlySpan(ref start, 32);
            
            int hash = 17;
            for (int i = 0; i < 32; i++)
            {
                hash = hash * 31 + span[i];
            }
            return hash;
        }
        
        public static bool operator ==(FixedString32 left, FixedString32 right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(FixedString32 left, FixedString32 right)
        {
            return !left.Equals(right);
        }
        
        public static implicit operator string(FixedString32 str)
        {
            return str.ToString();
        }
        
        public static implicit operator FixedString32(string str)
        {
            return new FixedString32(str);
        }
    }
}
