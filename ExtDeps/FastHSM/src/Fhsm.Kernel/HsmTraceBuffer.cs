using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Zero-allocation ring buffer for trace records.
    /// Thread-local, fixed size (64KB default).
    /// Design: HSM-design-talk.md lines 1951-1953
    /// </summary>
    public unsafe class HsmTraceBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private int _writePos;
        private ushort _currentTick;
        private TraceLevel _filterLevel;
        
        public HsmTraceBuffer(int capacityBytes = 65536)  // 64KB
        {
            _capacity = capacityBytes;
            _buffer = new byte[capacityBytes];
            _writePos = 0;
            _currentTick = 0;
            _filterLevel = TraceLevel.Tier1;  // Default
        }
        
        /// <summary>
        /// Current filter level (what to trace).
        /// </summary>
        public TraceLevel FilterLevel
        {
            get => _filterLevel;
            set => _filterLevel = value;
        }
        
        /// <summary>
        /// Current tick (wraps at ushort.MaxValue).
        /// </summary>
        public ushort CurrentTick
        {
            get => _currentTick;
            set => _currentTick = value;
        }
        
        /// <summary>
        /// Clear the trace buffer.
        /// </summary>
        public void Clear()
        {
            _writePos = 0;
        }
        
        /// <summary>
        /// Write a transition trace record.
        /// </summary>
        public void WriteTransition(uint instanceId, ushort from, ushort to, ushort eventId)
        {
            if ((_filterLevel & TraceLevel.Transitions) == 0) return;
            
            var record = new TraceTransition
            {
                Header = new TraceRecordHeader
                {
                    OpCode = TraceOpCode.Transition,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                FromState = from,
                ToState = to,
                TriggerEventId = eventId
            };
            
            WriteRecord(ref record, sizeof(TraceTransition));
        }
        
        /// <summary>
        /// Write an event handled trace record.
        /// </summary>
        public void WriteEventHandled(uint instanceId, ushort eventId, byte result)
        {
            if ((_filterLevel & TraceLevel.Events) == 0) return;
            
            var record = new TraceEventHandled
            {
                Header = new TraceRecordHeader
                {
                    OpCode = TraceOpCode.EventHandled,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                EventId = eventId,
                Result = result
            };
            
            WriteRecord(ref record, sizeof(TraceEventHandled));
        }
        
        /// <summary>
        /// Write a state change trace record.
        /// </summary>
        public void WriteStateChange(uint instanceId, ushort stateIndex, bool isEntry)
        {
            if ((_filterLevel & TraceLevel.StateChanges) == 0) return;
            
            var record = new TraceStateChange
            {
                Header = new TraceRecordHeader
                {
                    OpCode = isEntry ? TraceOpCode.StateEnter : TraceOpCode.StateExit,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                StateIndex = stateIndex
            };
            
            WriteRecord(ref record, sizeof(TraceStateChange));
        }
        
        /// <summary>
        /// Write a guard evaluated trace record.
        /// </summary>
        public void WriteGuardEvaluated(uint instanceId, ushort guardId, bool result, ushort transitionIndex)
        {
            if ((_filterLevel & TraceLevel.Guards) == 0) return;
            
            var record = new TraceGuardEvaluated
            {
                Header = new TraceRecordHeader
                {
                    OpCode = TraceOpCode.GuardEvaluated,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                GuardId = guardId,
                Result = (byte)(result ? 1 : 0),
                TransitionIndex = transitionIndex
            };
            
            WriteRecord(ref record, sizeof(TraceGuardEvaluated));
        }
        
        /// <summary>
        /// Write an action executed trace record.
        /// </summary>
        public void WriteActionExecuted(uint instanceId, ushort actionId)
        {
            if ((_filterLevel & TraceLevel.Actions) == 0) return;
            
            var record = new TraceActionExecuted
            {
                Header = new TraceRecordHeader
                {
                    OpCode = TraceOpCode.ActionExecuted,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                ActionId = actionId
            };
            
            WriteRecord(ref record, sizeof(TraceActionExecuted));
        }
        
        /// <summary>
        /// Write a conflict trace record.
        /// </summary>
        public void WriteConflict(uint instanceId, ushort stateIndex, byte attemptedLanes, byte conflictingLanes)
        {
            // Conflicts are important, logging at Tier1 or Tier2?
            // Instructions say: "For v1.0: Log conflict (if tracing enabled)"
            // Assuming it aligns with Transitions/Events level (Tier 1) or Error?
            // Let's assume Tier1 since it affects behavior.
            if (_filterLevel == TraceLevel.None) return;

            var record = new ConflictRecord
            {
                Header = new TraceRecordHeader
                {
                    OpCode = TraceOpCode.Conflict,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                StateIndex = stateIndex,
                AttemptedLanes = attemptedLanes,
                ConflictingLanes = conflictingLanes
            };

            WriteRecord(ref record, sizeof(ConflictRecord));
        }

        /// <summary>
        /// Write an error trace record.
        /// </summary>
        public void WriteError(uint instanceId, ushort errorCode)
        {
            // Errors are always logged, ignore filter? Or maybe separate flag?
            // Assuming errors are critical and always logged if buffer exists.
            
            var record = new TraceError
            {
                Header = new TraceRecordHeader
                {
                    OpCode = TraceOpCode.Error,
                    Timestamp = _currentTick,
                    InstanceId = instanceId
                },
                ErrorCode = errorCode
            };
            
            WriteRecord(ref record, sizeof(TraceError));
        }

        /// <summary>
        /// Read all trace records from the buffer.
        /// Returns a span view (zero-copy).
        /// </summary>
        public ReadOnlySpan<byte> GetTraceData()
        {
            return new ReadOnlySpan<byte>(_buffer, 0, _writePos);
        }
        
        /// <summary>
        /// Get current write position (bytes written).
        /// </summary>
        public int BytesWritten => _writePos;
        
        private void WriteRecord<T>(ref T record, int size) where T : unmanaged
        {
            // Ring buffer: wrap if needed
            if (_writePos + size > _capacity)
            {
                _writePos = 0;  // Wrap around (overwrite old data)
            }
            
            fixed (byte* bufferPtr = _buffer)
            fixed (T* recordPtr = &record)
            {
                byte* src = (byte*)recordPtr;
                byte* dst = bufferPtr + _writePos;
                
                // Copy record to buffer
                Unsafe.CopyBlock(dst, src, (uint)size);
            }
            
            _writePos += size;
        }
    }
}
