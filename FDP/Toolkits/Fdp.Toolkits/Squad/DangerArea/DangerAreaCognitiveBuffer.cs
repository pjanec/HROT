using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// Inline array of 8 <see cref="DangerAreaDescriptor"/>s (8 * 72 = 576 bytes).
    /// Always write through <see cref="DangerAreaCognitiveBuffer.GetSpanRW"/> to
    /// avoid the InlineArray defensive-copy trap.
    /// </summary>
    [InlineArray(8)]
    public struct DangerAreaDescriptorArray
    {
#pragma warning disable CS0169
        private DangerAreaDescriptor _element;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Brain-side danger-area result cache written by <c>DangerAreaRefreshSystem</c>
    /// (squad danger-area pipeline, SS5.2).
    /// Total size: 4 (Count) + 4 (LastUpdateTick) + 8*72 (Slots) = 584 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DangerAreaCognitiveBuffer)]
    public struct DangerAreaCognitiveBuffer
    {
        /// <summary>Number of valid descriptors in <see cref="Slots"/> (0..8).</summary>
        public int Count;

        /// <summary>
        /// ⭐ <c>CE-3072</c> B0 — the answer's stamp, the kind-neutral header every sensor family carries (the ranked
        /// family's is <c>EqsCognitiveBuffer.LastUpdateTick</c>): 0 = no answer yet. ⚠ Took the old 4-byte pad slot, so the
        /// layout is unchanged apart from the descriptor size.
        /// </summary>
        public uint LastUpdateTick;

        /// <summary>Cached danger-area descriptors from the last refresh.</summary>
        public DangerAreaDescriptorArray Slots;

        /// <summary>True once an answer arrived — ⭐ <c>CE-3072</c> B0: even an answer with NO areas (a clear route is an
        /// answer). It used to be <c>Count &gt; 0</c>, which read "no danger" as "not ready".</summary>
        public bool IsReady => LastUpdateTick != 0;

        /// <summary>Write-through span over Slots (defeats InlineArray defensive copy).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<DangerAreaDescriptor> GetSpanRW()
            => MemoryMarshal.CreateSpan(
                   ref Unsafe.As<DangerAreaDescriptorArray, DangerAreaDescriptor>(ref Slots), 8);

        /// <summary>Read-only span over Slots.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<DangerAreaDescriptor> GetSpanRO()
            => MemoryMarshal.CreateReadOnlySpan(
                   ref Unsafe.As<DangerAreaDescriptorArray, DangerAreaDescriptor>(ref Slots), 8);
    }
}
