using System.Runtime.CompilerServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Unmanaged trace-emission context. Pointers are owned by the caller
    /// (typically a 1024-byte ECS component); this struct only performs
    /// O(1) ring-buffer arithmetic and zero-allocation record writes.
    /// </summary>
    /// <remarks>
    /// Stride invariant: every record consumes exactly 16 bytes regardless of
    /// payload struct size (12-byte structs like TraceStateChange are zero-padded
    /// to the 16-byte slot). This guarantees that <c>*WritePos</c> stays aligned
    /// inside <c>[0, CapacityBytes)</c> and that no write can overrun the buffer
    /// near the wrap boundary.
    /// </remarks>
    public unsafe struct HsmTraceContext
    {
        public const int RecordStride = 16;

        public byte*   Buffer;
        public ushort* WritePos;
        public ushort* RecordCount;
        public ushort  CapacityBytes;  // == HsmTraceWorkingMemory1024.PayloadBytes (1008)
        public ushort  MaxRecords;     // == HsmTraceWorkingMemory1024.CapacityRecords (63)
        public TraceLevel FilterLevel;
        public ushort  CurrentTick;
        public uint    InstanceId;

        // ---------- Filter helpers ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLevelEnabled(TraceLevel level) => (FilterLevel & level) != 0;

        // ---------- Write API ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTransition(uint instanceId, ushort fromState, ushort toState, ushort triggerEventId)
        {
            if (!IsLevelEnabled(TraceLevel.Transitions)) return;
            var rec = new TraceTransition
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = TraceOpCode.Transition,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                FromState       = fromState,
                ToState         = toState,
                TriggerEventId  = triggerEventId,
            };
            WriteRecord(&rec, sizeof(TraceTransition));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStateChange(uint instanceId, ushort stateIndex, bool isEntry)
        {
            if (!IsLevelEnabled(TraceLevel.StateChanges)) return;
            var rec = new TraceStateChange
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = isEntry ? TraceOpCode.StateEnter : TraceOpCode.StateExit,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                StateIndex = stateIndex,
            };
            WriteRecord(&rec, sizeof(TraceStateChange));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteEventHandled(uint instanceId, ushort eventId, byte result)
        {
            if (!IsLevelEnabled(TraceLevel.Events)) return;
            var rec = new TraceEventHandled
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = TraceOpCode.EventHandled,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                EventId = eventId,
                Result  = result,
            };
            WriteRecord(&rec, sizeof(TraceEventHandled));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteGuardEvaluated(uint instanceId, ushort guardId, bool result, ushort transitionIndex = 0)
        {
            if (!IsLevelEnabled(TraceLevel.Guards)) return;
            var rec = new TraceGuardEvaluated
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = TraceOpCode.GuardEvaluated,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                GuardId         = guardId,
                Result          = (byte)(result ? 1 : 0),
                TransitionIndex = transitionIndex,
            };
            WriteRecord(&rec, sizeof(TraceGuardEvaluated));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteActionExecuted(uint instanceId, ushort actionId)
        {
            if (!IsLevelEnabled(TraceLevel.Actions)) return;
            var rec = new TraceActionExecuted
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = TraceOpCode.ActionExecuted,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                ActionId = actionId,
            };
            WriteRecord(&rec, sizeof(TraceActionExecuted));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteError(uint instanceId, ushort errorCode)
        {
            // Errors bypass FilterLevel: they are always recorded.
            var rec = new TraceError
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = TraceOpCode.Error,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                ErrorCode = errorCode,
            };
            WriteRecord(&rec, sizeof(TraceError));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteConflict(uint instanceId, ushort stateIndex, byte attemptedLanes, byte conflictingLanes)
        {
            var rec = new ConflictRecord
            {
                Header = new TraceRecordHeader
                {
                    OpCode     = TraceOpCode.Conflict,
                    Timestamp  = CurrentTick,
                    InstanceId = instanceId,
                },
                StateIndex       = stateIndex,
                AttemptedLanes   = attemptedLanes,
                ConflictingLanes = conflictingLanes,
            };
            WriteRecord(&rec, sizeof(ConflictRecord));
        }

        // ---------- Strict 16-byte stride writer ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteRecord(void* payload, int payloadSize)
        {
            // WritePos is always pre-wrapped in [0, CapacityBytes).
            int offset = *WritePos;
            *WritePos = (ushort)((*WritePos + RecordStride) % CapacityBytes);
            if (*RecordCount < MaxRecords) (*RecordCount)++;

            byte* dst = Buffer + offset;
            // Zero the entire 16-byte slot first to keep trailing pad bytes deterministic.
            Unsafe.InitBlockUnaligned(dst, 0, RecordStride);
            // Then copy the actual payload (12 or 16 bytes).
            Unsafe.CopyBlockUnaligned(dst, payload, (uint)payloadSize);
        }
    }
}
