using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// 256-bit layer visibility mask used by <see cref="DebugPrimitiveShape.LayerControlMask"/>.
    /// Self-contained in GizmoMap.Contracts with no external dependencies.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct LayerMask256
    {
        [FieldOffset(0)]  private ulong _q0;
        [FieldOffset(8)]  private ulong _q1;
        [FieldOffset(16)] private ulong _q2;
        [FieldOffset(24)] private ulong _q3;

        /// <summary>Sets all 256 bits to 1 (all layers visible).</summary>
        public void SetAll()
        {
            _q0 = ~0UL;
            _q1 = ~0UL;
            _q2 = ~0UL;
            _q3 = ~0UL;
        }

        /// <summary>Sets the bit at <paramref name="bitIndex"/> to 1.</summary>
        public void SetBit(int bitIndex)
        {
            int q = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 0x3F);
            switch (q)
            {
                case 0: _q0 |= mask; break;
                case 1: _q1 |= mask; break;
                case 2: _q2 |= mask; break;
                case 3: _q3 |= mask; break;
            }
        }

        /// <summary>Returns true when the bit at <paramref name="bitIndex"/> is set.</summary>
        public readonly bool IsSet(int bitIndex)
        {
            int q = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 0x3F);
            return q switch
            {
                0 => (_q0 & mask) != 0,
                1 => (_q1 & mask) != 0,
                2 => (_q2 & mask) != 0,
                3 => (_q3 & mask) != 0,
                _ => false
            };
        }
    }
}
