using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Opcode discriminating consideration records from winner summary records.
    /// </summary>
    public enum UtilityTraceOpCode : byte
    {
        /// <summary>One consideration row: input read, curve applied, aggregate updated.</summary>
        Consideration = 0,
        /// <summary>Summary record written after all options are scored: winner and margin.</summary>
        Winner = 1
    }

    /// <summary>
    /// 32-byte trace record written into <see cref="UtilityTraceWorkingMemory1024"/>.
    /// Two record kinds share the same layout; field semantics depend on <see cref="OpCode"/>.
    /// </summary>
    /// <remarks>
    /// Layout (32 bytes):
    /// <code>
    /// [0]     byte   OpCode
    /// [1]     byte   OptionIndex
    /// [2..3]  ushort InputId
    /// [4..5]  ushort Tick
    /// [6..7]  ushort _flags (reserved)
    /// [8..11] float  RawValue        | Winner: winner score
    /// [12..15]float  NormalizedValue | Winner: (unused, 0)
    /// [16..19]float  CurveOutput     | Winner: (unused, 0)
    /// [20..23]float  Weight          | Winner: (unused, 0)
    /// [24..27]float  RunningAggregate| Winner: runner-up margin
    /// [28..31]float  _reserved
    /// </code>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public struct UtilityTraceRecord
    {
        /// <summary>Record kind: Consideration (0) or Winner (1).</summary>
        public UtilityTraceOpCode OpCode;
        /// <summary>Zero-based index of the option within the decision definition.</summary>
        public byte  OptionIndex;
        /// <summary>FNV-1a-16 of the input reader name. Zero for Winner records.</summary>
        public ushort InputId;
        /// <summary>Simulation tick at the time of scoring.</summary>
        public ushort Tick;
        /// <summary>Reserved for future use.</summary>
        public ushort Flags;
        /// <summary>Raw value returned by the input reader. Winner records: winning score.</summary>
        public float  RawValue;
        /// <summary>Normalized input value (Phase 1: same as RawValue). Winner records: 0.</summary>
        public float  NormalizedValue;
        /// <summary>Output of the response curve in [0,1]. Winner records: 0.</summary>
        public float  CurveOutput;
        /// <summary>Consideration weight. Winner records: 0.</summary>
        public float  Weight;
        /// <summary>Running aggregate score after this consideration. Winner records: runner-up margin.</summary>
        public float  RunningAggregate;
        /// <summary>Reserved padding to reach 32 bytes.</summary>
        public float  Reserved;
    }

    /// <summary>
    /// 1024-byte unmanaged ring buffer of <see cref="UtilityTraceRecord"/>s.
    /// Per-entity trace memory for Utility AI scoring; opt-in via
    /// <c>UtilityDebugFlags.TraceEnabled</c>. NoSave keeps it out of scenario JSON.
    /// </summary>
    /// <remarks>
    /// Layout: 8-byte header + 1016-byte buffer. First 992 bytes of buffer used
    /// (31 x 32 = 992); trailing 24 bytes are dead padding to fill 1016 bytes.
    /// <para>
    /// <see cref="WritePos"/> is ALWAYS pre-wrapped inside <c>[0, PayloadBytes)</c>.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 1024)]
    [ComponentId(UtilityApplicationComponentIds.UtilityTraceWorkingMemory)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct UtilityTraceWorkingMemory1024
    {
        public const int RecordStride    = 32;
        public const int CapacityRecords = 31;
        public const int PayloadBytes    = CapacityRecords * RecordStride; // 992
        public const int BufferBytes     = 1016;

        // 8-byte header
        /// <summary>Write cursor; always pre-wrapped into [0, PayloadBytes).</summary>
        public ushort WritePos;
        /// <summary>Number of records written; saturates at <see cref="CapacityRecords"/>.</summary>
        public ushort RecordCount;
        /// <summary>Simulation tick of the most recent scoring pass.</summary>
        public uint   LastTick;

        // 1016-byte payload (first 992 bytes used; last 24 bytes are dead padding).
        public fixed byte Buffer[BufferBytes];

        // ── Internal helpers ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private UtilityTraceRecord* NextRecord(ushort tick)
        {
            int offset = WritePos;
            WritePos = (ushort)((WritePos + RecordStride) % PayloadBytes);
            if (RecordCount < CapacityRecords) RecordCount++;

            fixed (byte* basePtr = Buffer)
            {
                var rec = (UtilityTraceRecord*)(basePtr + offset);
                Unsafe.InitBlockUnaligned(rec, 0, RecordStride);
                rec->Tick = tick;
                return rec;
            }
        }

        // ── Write methods ──

        /// <summary>
        /// Records one consideration evaluation: the raw reader value, curve output,
        /// weight, and running aggregate after this row.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteConsiderationRecord(ushort tick, byte optionIdx, ushort inputId,
            float raw, float norm, float curveOut, float weight, float runningAgg)
        {
            var rec = NextRecord(tick);
            rec->OpCode           = UtilityTraceOpCode.Consideration;
            rec->OptionIndex      = optionIdx;
            rec->InputId          = inputId;
            rec->RawValue         = raw;
            rec->NormalizedValue  = norm;
            rec->CurveOutput      = curveOut;
            rec->Weight           = weight;
            rec->RunningAggregate = runningAgg;
        }

        /// <summary>
        /// Records the scoring pass summary: the winning option, its score, and the margin
        /// over the runner-up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteWinnerRecord(ushort tick, byte winnerOptionId, byte winnerDefinitionIdx,
            float winnerScore, float runnerUpMargin)
        {
            var rec = NextRecord(tick);
            rec->OpCode           = UtilityTraceOpCode.Winner;
            rec->OptionIndex      = winnerOptionId;       // actual OptionId byte (e.g. Posture value)
            rec->Flags            = winnerDefinitionIdx;  // index in def.Options array
            rec->RawValue         = winnerScore;
            rec->RunningAggregate = runnerUpMargin;
        }

        // ── Read methods (for diagnostics and tests) ──

        /// <summary>
        /// Reads the record at the given logical index (0 = oldest in the ring, wrapping).
        /// Index must be in [0, <see cref="RecordCount"/>).
        /// </summary>
        public void ReadRecord(int index, out UtilityTraceRecord record)
        {
            // Oldest record is at WritePos when the ring is full, or at 0 when not full.
            int startOffset = (RecordCount < CapacityRecords)
                ? 0
                : WritePos;  // WritePos points to the slot that will be overwritten next (= oldest)

            int byteOffset = (startOffset + index * RecordStride) % PayloadBytes;

            fixed (byte* basePtr = Buffer)
            {
                record = *(UtilityTraceRecord*)(basePtr + byteOffset);
            }
        }

        /// <summary>
        /// Scans backward from the most recent record, finds the latest Winner record, and
        /// collects the consideration records from that same scoring pass for the winning option.
        /// Returns <c>default</c> when no Winner record exists.
        /// </summary>
        public SelectedTraceResult LatestSelected()
        {
            if (RecordCount == 0) return default;
            int count = (int)RecordCount;

            // Find the most recent Winner record (highest logical index first).
            int winnerIdx = -1;
            for (int i = count - 1; i >= 0; i--)
            {
                ReadRecord(i, out var rec);
                if (rec.OpCode == UtilityTraceOpCode.Winner)
                {
                    winnerIdx = i;
                    break;
                }
            }
            if (winnerIdx < 0) return default;

            ReadRecord(winnerIdx, out var winner);
            var result = new SelectedTraceResult
            {
                OptionId       = winner.OptionIndex,
                RunnerUpMargin = winner.RunningAggregate
            };

            // winnerDefinitionIdx is the index in def.Options[] (stored in Flags).
            byte winnerDefIdx = (byte)winner.Flags;

            // Collect consideration records for the winning option (walk backward from winnerIdx-1).
            for (int i = winnerIdx - 1; i >= 0; i--)
            {
                ReadRecord(i, out var rec);
                if (rec.OpCode == UtilityTraceOpCode.Winner) break; // crossed previous scoring pass
                if (rec.OptionIndex == winnerDefIdx)
                    result.AddConsideration(rec.InputId, rec.RawValue, rec.CurveOutput, rec.Weight);
            }
            return result;
        }
    }

    // ── Trace result read-back types ──────────────────────────────────────────────

    /// <summary>One consideration row extracted from a scored trace.</summary>
    public readonly struct SelectedTraceConsideration
    {
        /// <summary>Registered input ID.</summary>
        public readonly ushort InputId;
        /// <summary>Raw value returned by the input reader.</summary>
        public readonly float  RawValue;
        /// <summary>Curve-evaluated output fed to the aggregator.</summary>
        public readonly float  CurveOutput;
        /// <summary>Configured weight for this consideration.</summary>
        public readonly float  Weight;

        internal SelectedTraceConsideration(ushort inputId, float rawValue, float curveOutput, float weight)
        {
            InputId     = inputId;
            RawValue    = rawValue;
            CurveOutput = curveOutput;
            Weight      = weight;
        }
    }

    /// <summary>
    /// Up to 8 consideration slots stored inline without heap allocation.
    /// </summary>
    [System.Runtime.CompilerServices.InlineArray(8)]
    internal struct SelectedTraceConsiderationArray
    {
        private SelectedTraceConsideration _element;
    }

    /// <summary>
    /// Result of <see cref="UtilityTraceWorkingMemory1024.LatestSelected"/>:
    /// the winning option and the considerations scored for it.
    /// </summary>
    public struct SelectedTraceResult
    {
        /// <summary>The raw OptionId byte of the winner (e.g. the <c>Posture</c> value cast to byte).</summary>
        public byte  OptionId;
        /// <summary>Score gap between winner and runner-up.</summary>
        public float RunnerUpMargin;
        /// <summary>Number of consideration entries collected.</summary>
        public int   ConsiderationCount;
        private SelectedTraceConsiderationArray _considerations;

        /// <summary>
        /// Returns the consideration entry whose <see cref="SelectedTraceConsideration.InputId"/>
        /// matches <paramref name="inputId"/>, or <c>default</c> if not found.
        /// </summary>
        public SelectedTraceConsideration ConsiderationByInput(ushort inputId)
        {
            var span = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SelectedTraceConsiderationArray, SelectedTraceConsideration>(
                    ref _considerations), 8);
            for (int i = 0; i < ConsiderationCount; i++)
                if (span[i].InputId == inputId) return span[i];
            return default;
        }

        internal void AddConsideration(ushort inputId, float rawValue, float curveOutput, float weight)
        {
            if (ConsiderationCount >= 8) return;
            var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SelectedTraceConsiderationArray, SelectedTraceConsideration>(
                    ref _considerations), 8);
            span[ConsiderationCount++] = new SelectedTraceConsideration(inputId, rawValue, curveOutput, weight);
        }
    }
}
