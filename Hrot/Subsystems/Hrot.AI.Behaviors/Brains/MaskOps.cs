namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated bit-mask helpers for the Hill-attack slot bitmasks (<c>ushort</c>, 16 slots). Bitwise
    /// set/shift has no visual-node form (architect Q#6-A keeps boolean composition to logical
    /// <c>&amp;&amp;</c>/<c>||</c>, not bitwise), so the accumulation stays in this reviewable helper.
    /// </summary>
    public static class MaskOps
    {
        /// <summary>
        /// Returns <paramref name="mask"/> with the <paramref name="index"/>-th bit set, or the mask
        /// unchanged when <paramref name="index"/> is outside the 16-slot range. Reproduces the C# oracle
        /// <c>HillAttackCommanderNodes.Action_DispatchAllToBaseline</c>'s
        /// <c>if (i &lt; 16) s.BaselineReservedMask |= (ushort)(1 &lt;&lt; i)</c>.
        /// </summary>
        public static ushort WithBitSet(ushort mask, int index)
            => index >= 0 && index < 16 ? (ushort)(mask | (1 << index)) : mask;
    }
}
