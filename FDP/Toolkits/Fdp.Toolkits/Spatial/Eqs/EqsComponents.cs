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
        /// <summary>
        /// Parallel bitset indicating which bits in <see cref="Flags"/> were actually computed
        /// by the template's tests. A bit not set here must not be read by consumers.
        /// Same 2-byte slot as the former _pad field; struct size remains 24 bytes.
        /// </summary>
        public short FlagsMeaningful;
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
        /// <summary>
        /// Simulation time in seconds when the buffer was last written.
        /// Written by <c>EqsResultUpdateSystem</c> from <c>view.Time</c>.
        /// Distinct from <see cref="LastUpdateTick"/> which is the determinism-friendly
        /// publish-side timestamp.
        /// </summary>
        public float LastUpdateTimeSeconds;
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
    /// Discriminators controlling when <see cref="EqsSolverSystem"/> emits an
    /// <c>EqsResultEvent</c> after a successful evaluation.
    /// </summary>
    public enum EqsPublishPolicy : byte
    {
        /// <summary>Always emit a result event after each evaluation (default).</summary>
        AlwaysPush  = 0,
        /// <summary>Emit only when the top-ranked candidate identity changes.</summary>
        TopChanged  = 1,
        /// <summary>Reserved (not yet implemented).</summary>
        _Reserved2  = 2,
        /// <summary>
        /// Emit only when any top-K score changes by more than
        /// <see cref="EqsSensor.ScoreDeltaThreshold"/> since the last publish.
        /// </summary>
        ScoreDelta  = 3,
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
        /// <summary>Publish policy controlling when results are broadcast (see <see cref="EqsPublishPolicy"/>).</summary>
        public byte PublishPolicy;
        /// <summary>Solver scheduling priority band: Critical, Normal, or Low.</summary>
        public byte Priority;
        /// <summary>
        /// Score change threshold for the <see cref="EqsPublishPolicy.ScoreDelta"/> publish policy.
        /// The solver skips emitting a result event when all top-K score deltas are at or below
        /// this value since the last published result. Default 0.0f (every change triggers a publish).
        /// </summary>
        public float ScoreDeltaThreshold;
        /// <summary>Context slot 0 (by convention: Self). Position source for tests that need
        /// the observer position. Filled by the spawn/maintain helper.</summary>
        public Entity ContextSlot0;
        /// <summary>Context slot 1 (by convention: Target). Primary position source for LOS
        /// tests. Replaces TargetMemory[0] position read.</summary>
        public Entity ContextSlot1;
        /// <summary>Context slot 2 (by convention: Leader / Squad-mate). Optional secondary
        /// LOS context.</summary>
        public Entity ContextSlot2;
    }
}
