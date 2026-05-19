using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// 1024-byte unmanaged ring buffer of <see cref="BTreeTraceRecord"/>s. Per-entity
    /// trace memory for FastBTree execution; opt-in via <c>DebugState.EnableTraceBuffer</c>.
    /// Recorded by the Flight Recorder (NoSave keeps it out of scenario JSON only).
    /// </summary>
    /// <remarks>
    /// Layout: 8-byte header + 1016-byte buffer. Only the first 1008 bytes of the
    /// buffer are used (63 × 16 = 1008); trailing 8 bytes are padding to fill the
    /// 1024-byte component-size ceiling.
    /// <para>
    /// <see cref="WritePos"/> is ALWAYS pre-wrapped inside <c>[0, PayloadBytes)</c>.
    /// The naive <c>WritePos % CapacityBytes</c> pattern with deferred modulo is
    /// unsafe — <c>ushort</c> overflows at 65536, and 65536 % 1008 ≠ 0, so cursor
    /// drift would corrupt chronological ordering after the first overflow.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 1024)]
    [ComponentId(BehaviorApplicationComponentIds.BTreeTraceWorkingMemory)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct BTreeTraceWorkingMemory1024
    {
        public const int RecordStride    = 16;
        public const int CapacityRecords = 63;
        public const int PayloadBytes    = CapacityRecords * RecordStride; // 1008
        public const int BufferBytes     = 1016;

        // 8-byte header
        public ushort WritePos;        // always in [0, PayloadBytes)
        public ushort RecordCount;     // saturates at CapacityRecords
        public uint   LastInstanceId;  // stamped by tick system once per frame

        // 1016-byte payload (first 1008 bytes used; last 8 bytes are dead padding).
        public fixed byte Buffer[BufferBytes];

        // ---------- Internal helpers ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BTreeTraceRecord* NextRecord(ushort tick)
        {
            int offset = WritePos;
            WritePos = (ushort)((WritePos + RecordStride) % PayloadBytes);
            if (RecordCount < CapacityRecords) RecordCount++;

            // Buffer is a fixed-size buffer; take its address inside this struct method.
            fixed (byte* basePtr = Buffer)
            {
                var rec = (BTreeTraceRecord*)(basePtr + offset);
                Unsafe.InitBlockUnaligned(rec, 0, RecordStride);
                rec->Timestamp  = tick;
                rec->InstanceId = LastInstanceId;
                return rec;
            }
        }

        // ---------- Engine-emitted opcodes ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteNodeEvaluated(int nodeIndex, NodeStatus status, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode    = BTreeTraceOpCode.NodeEvaluated;
            rec->NodeIndex = (ushort)nodeIndex;
            rec->Status    = status;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteScopePushed(ushort stackDepth, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode     = BTreeTraceOpCode.ScopePushed;
            rec->StackDepth = stackDepth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteScopePopped(ushort stackDepth, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode     = BTreeTraceOpCode.ScopePopped;
            rec->StackDepth = stackDepth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteWaitStarted(int nodeIndex, float duration, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode    = BTreeTraceOpCode.WaitStarted;
            rec->NodeIndex = (ushort)nodeIndex;
            rec->Duration  = duration;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteWaitCompleted(int nodeIndex, float duration, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode    = BTreeTraceOpCode.WaitCompleted;
            rec->NodeIndex = (ushort)nodeIndex;
            rec->Duration  = duration;
        }

        // ---------- Domain-cooperative opcodes ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteChannelMutated(int nodeIndex, ChannelKind channel, ushort activeAction, NodeStatus status, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode        = BTreeTraceOpCode.ChannelMutated;
            rec->NodeIndex     = (ushort)nodeIndex;
            rec->Channel       = (byte)channel;
            rec->ActiveAction  = activeAction;
            rec->ChannelStatus = status;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteError(int nodeIndex, ushort errorCode, ushort tick)
        {
            var rec = NextRecord(tick);
            rec->OpCode    = BTreeTraceOpCode.Error;
            rec->NodeIndex = (ushort)nodeIndex;
            rec->ErrorCode = errorCode;
        }
    }
}
