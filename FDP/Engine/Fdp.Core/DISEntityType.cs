using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Fdp.Core
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct DISEntityType
    {
        // --- The 64-bit "Single Integer" View ---
        [FieldOffset(0)] public ulong Value;

        // --- The Human-Readable View (Standard DIS) ---
        // Note: Field offsets allow overlapping overlay on the 'Value' field.
        // Kind is at offset 7 (MSB in Little Endian when reading Value)
        [FieldOffset(7)] public byte Kind;          // 1 = Platform, 2 = Munition, etc.
        [FieldOffset(6)] public byte Domain;        // 1 = Land, 2 = Air, etc.
        [FieldOffset(4)] public ushort Country;     // 16-bit Country Code
        [FieldOffset(3)] public byte Category;      // e.g., Tank vs Truck, Fixed vs Rotary
        [FieldOffset(2)] public byte Subcategory;   // e.g., M1A1 vs T-72
        [FieldOffset(1)] public byte Specific;      // Specific variation
        [FieldOffset(0)] public byte Extra;         // Extra

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — the SISO-REF-010 dotted form, <c>Kind.Domain.Country.Category.Subcategory.Specific.Extra</c>
        /// (e.g. <c>1.1.225.1.1.0.0</c>) — the text <c>TkbMasterDto.DisType</c> carries.
        /// </summary>
        public readonly override string ToString()
            => string.Create(CultureInfo.InvariantCulture,
                $"{Kind}.{Domain}.{Country}.{Category}.{Subcategory}.{Specific}.{Extra}");

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — parses the dotted form. Fewer than seven fields leave the rest 0 (<c>1.1.225</c> is a
        /// country-level type). False on an empty string, a non-number, more than seven fields or a value out of range.
        /// </summary>
        public static bool TryParse(string? text, out DISEntityType type)
        {
            type = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Split('.');
            if (parts.Length > 7) return false;
            Span<int> f = stackalloc int[7];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out f[i])) return false;
                if (f[i] > (i == 2 ? ushort.MaxValue : byte.MaxValue)) return false;
            }
            type = new DISEntityType
            {
                Kind = (byte)f[0], Domain = (byte)f[1], Country = (ushort)f[2], Category = (byte)f[3],
                Subcategory = (byte)f[4], Specific = (byte)f[5], Extra = (byte)f[6],
            };
            return true;
        }
    }
}
