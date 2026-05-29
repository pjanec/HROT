using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// One ranked result entry produced by <see cref="UtilityScorer"/>.
    /// 16 bytes (Sequential layout). Unmanaged — safe for inline arrays and fixed buffers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UtilityResultEntry
    {
        /// <summary>Packed entity handle for the candidate; 0 for non-candidate decisions.</summary>
        public long  CandidateHandle;
        /// <summary>Final aggregated utility score in [0, 1].</summary>
        public float Score;
        /// <summary>Winning posture option ID (PostureSelect decisions only).</summary>
        public byte  WinningPostureId;
        // 3 bytes padding to preserve 8-byte alignment for CandidateHandle.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        /// <summary>Typed view of <see cref="CandidateHandle"/> as an unsigned packed entity value.</summary>
        public ulong Candidate => (ulong)CandidateHandle;
    }

    /// <summary>
    /// Fixed-size C# 12 inline array storing up to 16 ranked <see cref="UtilityResultEntry"/> values.
    /// IMPORTANT: never write through a direct index assignment — use
    /// <see cref="UtilityResultBuffer.GetSpanRW"/> to avoid the C# [InlineArray]
    /// defensive-copy trap (a write to a copy of this array is silently discarded).
    /// </summary>
    [InlineArray(UtilityConstants.TopN)]
    public struct UtilityResultArray
    {
        private UtilityResultEntry _element;
    }

    /// <summary>
    /// Output buffer written by <see cref="UtilityScorer.Evaluate"/>.
    /// Contains up to <see cref="UtilityConstants.TopN"/> ranked entries, sorted descending by score.
    /// Stack-allocated or stored as an ECS component; fully unmanaged.
    /// </summary>
    [ComponentId(UtilityApplicationComponentIds.UtilityResultBuffer)]
    [DataPolicy(DataPolicy.NoSave)]
    [StructLayout(LayoutKind.Sequential)]
    public struct UtilityResultBuffer
    {
        /// <summary>Number of valid entries in <see cref="Results"/> (0 to TopN).</summary>
        public int   Count;
        /// <summary>Score difference between the top-ranked and second-ranked options.
        /// Zero when fewer than two options are present.</summary>
        public float RunnerUpMargin;

        /// <summary>
        /// Ranked result entries (inline array, TopN slots).
        /// IMPORTANT: do not write through a direct index assignment such as
        /// <c>Results[i] = entry</c>. If this buffer is accessed through a copy
        /// (e.g. a by-value method parameter or implicit struct copy), the write
        /// goes into the copy and is silently discarded. Always use
        /// <see cref="GetSpanRW"/> for mutations.
        /// </summary>
        public UtilityResultArray Results;

        // ── Safe memory accessors (bypass the [InlineArray] ldobj defensive-copy trap) ──

        /// <summary>
        /// Returns a writable span over all TopN result slots.
        /// Uses <see cref="MemoryMarshal.CreateSpan"/> via an unsafe cast to bypass the
        /// C# compiler's defensive copy emitted for [InlineArray] index assignments.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<UtilityResultEntry> GetSpanRW()
        {
            return MemoryMarshal.CreateSpan(
                ref Unsafe.As<UtilityResultArray, UtilityResultEntry>(ref Results),
                UtilityConstants.TopN);
        }

        /// <summary>
        /// Returns a read-only span over all TopN result slots.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<UtilityResultEntry> GetSpanRO()
        {
            return MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<UtilityResultArray, UtilityResultEntry>(ref Results),
                UtilityConstants.TopN);
        }

        /// <summary>Returns the top-ranked entry, or <c>default</c> when <see cref="Count"/> is zero.</summary>
        public UtilityResultEntry Top() => Count > 0 ? GetSpanRO()[0] : default;

        /// <summary>
        /// Returns the score of the entry whose <see cref="UtilityResultEntry.CandidateHandle"/> matches
        /// <paramref name="candidateHandle"/>, or 0 if not found.
        /// </summary>
        public float ScoreOf(ulong candidateHandle)
        {
            var span = GetSpanRO();
            for (int i = 0; i < Count; i++)
                if ((ulong)span[i].CandidateHandle == candidateHandle) return span[i].Score;
            return 0f;
        }
    }

    /// <summary>
    /// Per-entity transient flags enabling Utility AI diagnostics.
    /// Attach to an entity at debug-time to activate the scoring trace buffer.
    /// </summary>
    [ComponentId(UtilityApplicationComponentIds.UtilityDebugFlags)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct UtilityDebugFlags
    {
        /// <summary>Non-zero = trace buffer recording is active for this entity.</summary>
        public byte TraceEnabled;
    }
}
