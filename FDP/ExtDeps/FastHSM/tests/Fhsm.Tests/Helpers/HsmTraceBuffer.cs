using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Test-only shim mirroring the legacy production <c>HsmTraceBuffer</c> API that
    /// was deleted in behav-diag-1 (replaced by the unmanaged <c>HsmTraceContext</c>).
    /// Tests retain the same surface for asserting record formats and filter behavior,
    /// but they no longer exercise the production tick path. Use <c>HsmTraceContext</c>
    /// against a stack-allocated buffer for new integration-style trace tests.
    /// </summary>
    /// <remarks>
    /// Unlike the new production path (strict 16-byte stride), this shim preserves the
    /// legacy variable-stride byte advancement to keep <c>BytesWritten</c> assertions
    /// equal to <c>sizeof(TraceXxx)</c>, matching the pre-refactor test contracts.
    /// </remarks>
    public unsafe class HsmTraceBuffer
    {
        private readonly byte[] _buffer;
        private int _writePos;

        public TraceLevel FilterLevel { get; set; } = TraceLevel.Tier1;
        public ushort CurrentTick { get; set; } = 0;
        public int BytesWritten => _writePos;

        public HsmTraceBuffer() : this(64 * 1024) { }
        public HsmTraceBuffer(int sizeBytes) { _buffer = new byte[sizeBytes]; }

        public void Clear() { _writePos = 0; }

        public ReadOnlySpan<byte> GetTraceData() => new(_buffer, 0, _writePos);

        public void WriteTransition(uint instanceId, ushort from, ushort to, ushort evtId)
        {
            if ((FilterLevel & TraceLevel.Transitions) == 0) return;
            var rec = new TraceTransition
            {
                Header = MakeHeader(TraceOpCode.Transition, instanceId),
                FromState = from, ToState = to, TriggerEventId = evtId,
            };
            WriteRecord(&rec, sizeof(TraceTransition));
        }

        public void WriteEventHandled(uint instanceId, ushort eventId, byte result)
        {
            if ((FilterLevel & TraceLevel.Events) == 0) return;
            var rec = new TraceEventHandled
            {
                Header = MakeHeader(TraceOpCode.EventHandled, instanceId),
                EventId = eventId, Result = result,
            };
            WriteRecord(&rec, sizeof(TraceEventHandled));
        }

        public void WriteStateChange(uint instanceId, ushort stateIndex, bool isEntry)
        {
            if ((FilterLevel & TraceLevel.StateChanges) == 0) return;
            var rec = new TraceStateChange
            {
                Header = MakeHeader(isEntry ? TraceOpCode.StateEnter : TraceOpCode.StateExit, instanceId),
                StateIndex = stateIndex,
            };
            WriteRecord(&rec, sizeof(TraceStateChange));
        }

        public void WriteGuardEvaluated(uint instanceId, ushort guardId, bool result, ushort transitionIndex)
        {
            if ((FilterLevel & TraceLevel.Guards) == 0) return;
            var rec = new TraceGuardEvaluated
            {
                Header = MakeHeader(TraceOpCode.GuardEvaluated, instanceId),
                GuardId = guardId, Result = (byte)(result ? 1 : 0), TransitionIndex = transitionIndex,
            };
            WriteRecord(&rec, sizeof(TraceGuardEvaluated));
        }

        public void WriteActionExecuted(uint instanceId, ushort actionId)
        {
            if ((FilterLevel & TraceLevel.Actions) == 0) return;
            var rec = new TraceActionExecuted
            {
                Header = MakeHeader(TraceOpCode.ActionExecuted, instanceId),
                ActionId = actionId,
            };
            WriteRecord(&rec, sizeof(TraceActionExecuted));
        }

        public void WriteError(uint instanceId, ushort errorCode)
        {
            var rec = new TraceError
            {
                Header = MakeHeader(TraceOpCode.Error, instanceId),
                ErrorCode = errorCode,
            };
            WriteRecord(&rec, sizeof(TraceError));
        }

        public void WriteConflict(uint instanceId, ushort stateIndex, byte attemptedLanes, byte conflictingLanes)
        {
            var rec = new ConflictRecord
            {
                Header = MakeHeader(TraceOpCode.Conflict, instanceId),
                StateIndex = stateIndex,
                AttemptedLanes = attemptedLanes,
                ConflictingLanes = conflictingLanes,
            };
            WriteRecord(&rec, sizeof(ConflictRecord));
        }

        private TraceRecordHeader MakeHeader(TraceOpCode op, uint instanceId) => new()
        {
            OpCode = op, Timestamp = CurrentTick, InstanceId = instanceId,
        };

        private void WriteRecord(void* recordPtr, int size)
        {
            if (_writePos + size > _buffer.Length) _writePos = 0; // legacy wrap
            fixed (byte* dst = _buffer)
                Unsafe.CopyBlockUnaligned(dst + _writePos, recordPtr, (uint)size);
            _writePos += size;
        }
    }
}
