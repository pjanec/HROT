using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// Inline array of 8 <see cref="DangerAreaDescriptor"/>s (8 * 68 = 544 bytes).
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
    /// Total size: 4 (Count) + 4 (_pad) + 8*68 (Slots) = 552 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DangerAreaCognitiveBuffer)]
    public struct DangerAreaCognitiveBuffer
    {
        /// <summary>Number of valid descriptors in <see cref="Slots"/> (0..8).</summary>
        public int Count;

        // 4 bytes padding to align Slots to 8 bytes (DangerAreaDescriptor starts with uint, aligned to 4)
        private int _pad;

        /// <summary>Cached danger-area descriptors from the last refresh.</summary>
        public DangerAreaDescriptorArray Slots;

        /// <summary>True after the first successful refresh.</summary>
        public bool IsReady => Count > 0;

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
