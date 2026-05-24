using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Single ranked EQS candidate. 24 bytes (StructLayout.Sequential).
    /// Handles both entity-shaped queries (EntityId != 0) and positional queries (EntityId == 0).
    /// Rejection sentinel: EntityId == -1L.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsResult
    {
        /// <summary>Packed entity value for entity-shaped queries. 0 = positional candidate. -1 = rejected.</summary>
        public long EntityId;
        /// <summary>World-space X coordinate (positional queries).</summary>
        public float PositionX;
        /// <summary>World-space Y coordinate (positional queries).</summary>
        public float PositionY;
        /// <summary>Final computed score used for Top-K ranking.</summary>
        public float Score;
        /// <summary>Bitfield of result flags (e.g., HasLOSToContext).</summary>
        public short Flags;
        /// <summary>Padding to 24 bytes.</summary>
        public short _pad;
    }

    /// <summary>
    /// Fixed-size C# 12 inline array storing up to 16 ranked <see cref="EqsResult"/> entries.
    /// IMPORTANT: never write through a direct index assignment — use
    /// <see cref="EqsCognitiveBuffer.GetSpanRW"/> to avoid the ldobj defensive-copy trap.
    /// </summary>
    [InlineArray(16)]
    public struct EqsResultArray
    {
        private EqsResult _element;
    }

    /// <summary>
    /// Brain-tier cognitive buffer holding the most recent Top-K query result.
    /// Written by <c>EqsResultUpdateSystem</c>; read synchronously by BTree/HSM nodes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EqsCognitiveBuffer)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct EqsCognitiveBuffer
    {
        /// <summary>Number of valid entries in <see cref="Results"/> (0–16).</summary>
        public int Count;
        /// <summary>Simulation tick when the buffer was last written.</summary>
        public uint LastUpdateTick;
        /// <summary>Packed Top-K result entries (inline array, 16 slots).</summary>
        public EqsResultArray Results;

        // ── Safe memory accessors (bypass the [InlineArray] ldobj defensive-copy trap) ──

        /// <summary>
        /// Returns a writable span over the result array.
        /// Uses <see cref="MemoryMarshal.CreateSpan"/> via an unsafe cast to bypass the
        /// C# compiler's defensive copy emitted for [InlineArray] index assignments (Design §8.1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<EqsResult> GetSpanRW()
        {
            return MemoryMarshal.CreateSpan(
                ref Unsafe.As<EqsResultArray, EqsResult>(ref Results), 16);
        }

        /// <summary>
        /// Returns a read-only span over the result array for BTree node evaluation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<EqsResult> GetSpanRO()
        {
            return MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<EqsResultArray, EqsResult>(ref Results), 16);
        }

        /// <summary>True once the buffer has received its first result from the solver.</summary>
        public bool IsReady => LastUpdateTick > 0;

        /// <summary>
        /// Returns a read-only reference to the top-ranked result (index 0).
        /// The buffer must not be empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly EqsResult GetTop()
        {
            return ref GetSpanRO()[0];
        }
    }

    /// <summary>
    /// Standing query configuration attached to a Brain entity.
    /// Replicated from Brain to Muscle via DDS to trigger the background EQS solver.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EqsSensor)]
    public struct EqsSensor
    {
        /// <summary>FNV-1a 32-bit hash of the query template BlueprintId.</summary>
        public uint BlueprintId;
        /// <summary>
        /// Version counter incremented each time any sensor parameter changes.
        /// The Muscle solver resets evaluation state when this differs from its cached epoch.
        /// </summary>
        public uint Epoch;
        /// <summary>Search radius in world-space units.</summary>
        public float SearchRadius;
        /// <summary>Bitmask of target faction affiliations to include.</summary>
        public uint FactionFilter;
        /// <summary>Minimum threat score required to pass the cheap LOS filter.</summary>
        public float ThreatThreshold;
        /// <summary>Publish policy controlling when results are broadcast (e.g., TopChanged, AlwaysPush).</summary>
        public byte PublishPolicy;
        /// <summary>Solver scheduling priority band: Critical, Normal, or Low.</summary>
        public byte Priority;
    }
}
