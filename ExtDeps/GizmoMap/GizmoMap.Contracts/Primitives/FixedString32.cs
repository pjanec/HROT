using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Fixed-size 32-byte string for zero-allocation string storage.
    // Stores up to 31 UTF-8 bytes + 1 null terminator.
    // Safe to use in network messages and debug primitive payloads.
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public unsafe struct FixedString32 : IEquatable<FixedString32>
    {
        private fixed byte _data[32];

        // Maximum string length (31 chars + null terminator).
        public const int MaxLength = 31;

        // Creates a FixedString32 from a regular string.
        // Truncates if longer than MaxLength.
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

        // Converts to a regular string.
        public override readonly string ToString()
        {
            ref byte start = ref Unsafe.As<FixedString32, byte>(ref Unsafe.AsRef(in this));
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref start, 32);

            int len = 0;
            while (len < MaxLength && span[len] != 0) len++;
            if (len == 0) return string.Empty;

            return Encoding.UTF8.GetString(span.Slice(0, len));
        }

        // Gets the current length in bytes (not characters).
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

        public readonly bool IsEmpty => Length == 0;

        public bool Equals(FixedString32 other) => ToString() == other.ToString();

        public override bool Equals(object? obj) => obj is FixedString32 other && Equals(other);

        public override int GetHashCode() => ToString().GetHashCode();

        public static bool operator ==(FixedString32 left, FixedString32 right) => left.Equals(right);
        public static bool operator !=(FixedString32 left, FixedString32 right) => !left.Equals(right);
    }
}
