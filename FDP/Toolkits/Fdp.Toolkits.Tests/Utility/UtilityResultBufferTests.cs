using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UtilityResultBuffer"/>, <see cref="UtilityResultArray"/>,
    /// and <see cref="UtilityDebugFlags"/> (TASK-UAI-P1-04 success criteria).
    /// </summary>
    public class UtilityResultBufferTests
    {
        // ── SC-P1-04-1: Span write persists; direct-index write on a copy does not ──

        [Fact]
        public void GetSpanRW_WriteIsPersisted_AndCopyTrapIsDocumented()
        {
            var buffer = default(UtilityResultBuffer);

            // ── Path A: span-based write (persists on the original) ──
            buffer.GetSpanRW()[0] = new UtilityResultEntry { Score = 0.9f };
            Assert.Equal(0.9f, buffer.GetSpanRO()[0].Score);

            // ── Path B: demonstrate the defensive-copy trap ──
            // Writing through a direct index on a COPY of the struct mutates only the copy.
            // The original buffer is unaffected. This mirrors the EqsCognitiveBuffer pattern.
            var copy = buffer;                                              // explicit copy
            copy.Results[0] = new UtilityResultEntry { Score = 0.1f };    // writes into copy's inline array

            // Original buffer: unchanged at 0.9f (copy mutation discarded for the original).
            Assert.Equal(0.9f, buffer.GetSpanRO()[0].Score);
            // Copy: updated to 0.1f (confirms [InlineArray] direct indexer works on a local copy).
            Assert.Equal(0.1f, copy.GetSpanRO()[0].Score);
        }

        // ── SC-P1-04-2: UtilityDebugFlags - struct layout and defaults ──

        [Fact]
        public void UtilityDebugFlags_DefaultTraceEnabled_IsZero()
        {
            var flags = default(UtilityDebugFlags);
            Assert.Equal(0, flags.TraceEnabled);
        }

        [Fact]
        public void UtilityDebugFlags_CanBeSetToNonZero()
        {
            var flags = new UtilityDebugFlags { TraceEnabled = 1 };
            Assert.NotEqual(0, flags.TraceEnabled);
        }

        // ── SC-P1-04-3: UtilityTraceWorkingMemory1024 basic record-count test ──

        [Fact]
        public unsafe void TraceBuffer_DisabledPath_RecordCountStaysZero()
        {
            var traceMem = default(UtilityTraceWorkingMemory1024);
            // No writes — record count must remain zero.
            Assert.Equal(0, traceMem.RecordCount);
        }

        [Fact]
        public unsafe void TraceBuffer_WriteConsiderationAndWinner_RecordCountIsCorrect()
        {
            var traceMem = default(UtilityTraceWorkingMemory1024);

            // 2 options x 3 considerations each = 6 consideration records
            for (byte opt = 0; opt < 2; opt++)
            {
                for (ushort inp = 0; inp < 3; inp++)
                {
                    traceMem.WriteConsiderationRecord(tick: 1, optionIdx: opt, inputId: inp,
                        raw: 0.5f, norm: 0.5f, curveOut: 0.5f, weight: 1f, runningAgg: 0.5f);
                }
            }

            // 1 winner record
            traceMem.WriteWinnerRecord(tick: 1, winnerOptionId: 0, winnerDefinitionIdx: 0, winnerScore: 0.8f, runnerUpMargin: 0.2f);

            Assert.Equal(7, traceMem.RecordCount);
        }

        [Fact]
        public unsafe void TraceBuffer_WinnerRecord_ContainsCorrectValues()
        {
            var traceMem = default(UtilityTraceWorkingMemory1024);
            traceMem.WriteWinnerRecord(tick: 5, winnerOptionId: 2, winnerDefinitionIdx: 0, winnerScore: 0.75f, runnerUpMargin: 0.15f);

            traceMem.ReadRecord(0, out var rec);
            Assert.Equal(UtilityTraceOpCode.Winner, rec.OpCode);
            Assert.Equal(2, rec.OptionIndex);
            Assert.Equal(5, rec.Tick);
            Assert.Equal(0.75f, rec.RawValue, precision: 5);
            Assert.Equal(0.15f, rec.RunningAggregate, precision: 5);
        }

        [Fact]
        public unsafe void TraceBuffer_ConsiderationRecord_ContainsCorrectValues()
        {
            var traceMem = default(UtilityTraceWorkingMemory1024);
            traceMem.WriteConsiderationRecord(tick: 3, optionIdx: 1, inputId: 42,
                raw: 0.6f, norm: 0.6f, curveOut: 0.4f, weight: 2f, runningAgg: 0.8f);

            traceMem.ReadRecord(0, out var rec);
            Assert.Equal(UtilityTraceOpCode.Consideration, rec.OpCode);
            Assert.Equal(1, rec.OptionIndex);
            Assert.Equal(42, rec.InputId);
            Assert.Equal(3, rec.Tick);
            Assert.Equal(0.6f, rec.RawValue, precision: 5);
            Assert.Equal(0.6f, rec.NormalizedValue, precision: 5);
            Assert.Equal(0.4f, rec.CurveOutput, precision: 5);
            Assert.Equal(2f, rec.Weight, precision: 5);
            Assert.Equal(0.8f, rec.RunningAggregate, precision: 5);
        }

        // ── SC-P1-04-4: RecordCount saturates at CapacityRecords ──

        [Fact]
        public unsafe void TraceBuffer_RecordCount_SaturatesAtCapacity()
        {
            var traceMem = default(UtilityTraceWorkingMemory1024);
            int writeCount = UtilityTraceWorkingMemory1024.CapacityRecords + 10;

            for (int i = 0; i < writeCount; i++)
                traceMem.WriteWinnerRecord(tick: (ushort)i, winnerOptionId: 0, winnerDefinitionIdx: 0, winnerScore: 0f, runnerUpMargin: 0f);

            Assert.Equal(UtilityTraceWorkingMemory1024.CapacityRecords, traceMem.RecordCount);
        }

        // ── Layout and constant sanity checks ──

        [Fact]
        public void UtilityResultEntry_SizeIs16Bytes()
        {
            Assert.Equal(16, Unsafe.SizeOf<UtilityResultEntry>());
        }

        [Fact]
        public void UtilityResultBuffer_HasExpectedFieldLayout()
        {
            // Count (4) + RunnerUpMargin (4) + Results (TopN * 16 = 256) = 264 bytes minimum.
            Assert.True(Unsafe.SizeOf<UtilityResultBuffer>() >= 264);
        }

        [Fact]
        public void UtilityTraceWorkingMemory1024_SizeIs1024Bytes()
        {
            Assert.Equal(1024, Unsafe.SizeOf<UtilityTraceWorkingMemory1024>());
        }

        [Fact]
        public void UtilityTraceRecord_SizeIs32Bytes()
        {
            Assert.Equal(32, Unsafe.SizeOf<UtilityTraceRecord>());
        }

        [Fact]
        public void UtilityApplicationComponentIds_AreUnique()
        {
            int debugId = UtilityApplicationComponentIds.UtilityDebugFlags;
            int traceId = UtilityApplicationComponentIds.UtilityTraceWorkingMemory;
            Assert.NotEqual(debugId, traceId);
        }
    }
}
