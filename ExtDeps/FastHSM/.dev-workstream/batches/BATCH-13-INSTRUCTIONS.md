# BATCH-13: Debug Trace System

**Batch Number:** BATCH-13  
**Tasks:** TASK-T02 (Debug Trace Buffer)  
**Phase:** Phase T - Tooling  
**Estimated Effort:** 7-10 days  
**Priority:** HIGH  
**Dependencies:** BATCH-12.1

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to **BATCH-13**! This batch implements **zero-allocation runtime tracing** for HSM diagnostics.

This enables developers to see what the state machine is doing: transitions, events, state changes, guard evaluations, etc.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `.dev-workstream/TASK-DEFINITIONS.md` - See TASK-T02
3. **Design Document:** `docs/design/HSM-design-talk.md` - Search for "Trace" (lines 657-669, 1944-1992)
4. **Design Document:** `docs/design/HSM-Implementation-Questions.md` - Q8 (Trace Filtering)
5. **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q8 decision

### Source Code Location

- **Trace System:** `src/Fhsm.Kernel/HsmTraceBuffer.cs` (NEW)
- **Trace Records:** `src/Fhsm.Kernel/Data/TraceRecord.cs` (NEW)
- **Kernel Update:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE - add trace calls)
- **Test Project:** `tests/Fhsm.Tests/Tooling/TraceTests.cs` (NEW)

### Questions File

`.dev-workstream/questions/BATCH-13-QUESTIONS.md`

---

## Context

**Core implementation complete (BATCH-12.1).** Now add **diagnostics** to see what's happening inside the state machine.

**This batch implements:**
1. **Trace Buffer** - Ring buffer (zero-allocation)
2. **Trace Records** - Binary format for efficiency
3. **Trace Filtering** - Per-instance, per-tier (Architect Q8)
4. **Kernel Integration** - Emit trace records during execution

**Related Task:**
- [TASK-T02](../TASK-DEFINITIONS.md#task-t02-debug-trace-buffer) - Debug Trace Buffer

---

## 🎯 Batch Objectives

Enable runtime diagnostics:
- See transitions as they happen
- See events being processed
- See states entered/exited
- See guard evaluations
- Filter what you want to trace

---

## ✅ Tasks

### Task 1: Trace Record Structures

**Create:** `src/Fhsm.Kernel/Data/TraceRecord.cs`

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Trace record types (opcodes).
    /// Design: HSM-design-talk.md lines 1959-1964
    /// </summary>
    public enum TraceOpCode : byte
    {
        None = 0,
        Transition = 0x01,        // State transition occurred
        EventHandled = 0x02,      // Event was processed
        StateEnter = 0x03,        // Entered a state
        StateExit = 0x04,         // Exited a state
        GuardEvaluated = 0x05,    // Guard was evaluated (result)
        ActionExecuted = 0x06,    // Action was executed
        ActivityStarted = 0x07,   // Activity started
        ActivityEnded = 0x08,     // Activity ended
        TimerSet = 0x09,          // Timer was set
        TimerFired = 0x0A,        // Timer fired
    }
    
    /// <summary>
    /// Trace filtering levels (Architect Q8: All modes).
    /// </summary>
    [Flags]
    public enum TraceLevel : uint
    {
        None = 0,
        
        // Tier 1 (default debug)
        Transitions = 1 << 0,      // Transitions between states
        Events = 1 << 1,           // Event handling
        StateChanges = 1 << 2,     // State enter/exit
        
        // Tier 2 (verbose)
        Actions = 1 << 3,          // Action execution
        Timers = 1 << 4,           // Timer events
        
        // Tier 3 (heavy)
        Guards = 1 << 5,           // Guard evaluations
        Activities = 1 << 6,       // Activity lifecycle
        
        // Presets
        Tier1 = Transitions | Events | StateChanges,
        Tier2 = Tier1 | Actions | Timers,
        Tier3 = Tier2 | Guards | Activities,
        All = 0xFFFFFFFF,
    }
    
    /// <summary>
    /// Common trace record header (8 bytes).
    /// All records start with this.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct TraceRecordHeader
    {
        [FieldOffset(0)] public TraceOpCode OpCode;
        [FieldOffset(1)] public byte Reserved;
        [FieldOffset(2)] public ushort Timestamp;    // Tick offset (wraps)
        [FieldOffset(4)] public uint InstanceId;     // For multi-instance filtering
    }
    
    /// <summary>
    /// Transition trace record (16 bytes).
    /// OpCode: Transition (0x01)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct TraceTransition
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort FromState;
        [FieldOffset(10)] public ushort ToState;
        [FieldOffset(12)] public ushort TriggerEventId;
        [FieldOffset(14)] public ushort Reserved;
    }
    
    /// <summary>
    /// Event handled trace record (12 bytes).
    /// OpCode: EventHandled (0x02)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    public struct TraceEventHandled
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort EventId;
        [FieldOffset(10)] public byte Result;  // 0=consumed, 1=deferred, 2=rejected
        [FieldOffset(11)] public byte Reserved;
    }
    
    /// <summary>
    /// State change trace record (12 bytes).
    /// OpCode: StateEnter (0x03) or StateExit (0x04)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    public struct TraceStateChange
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort StateIndex;
        [FieldOffset(10)] public ushort Reserved;
    }
    
    /// <summary>
    /// Guard evaluated trace record (16 bytes).
    /// OpCode: GuardEvaluated (0x05)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct TraceGuardEvaluated
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort GuardId;
        [FieldOffset(10)] public byte Result;  // 0=false, 1=true
        [FieldOffset(11)] public byte Reserved;
        [FieldOffset(12)] public ushort TransitionIndex;
        [FieldOffset(14)] public ushort Reserved2;
    }
    
    /// <summary>
    /// Action executed trace record (12 bytes).
    /// OpCode: ActionExecuted (0x06)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    public struct TraceActionExecuted
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort ActionId;
        [FieldOffset(10)] public ushort Reserved;
    }
}
```

---

### Task 2: Trace Buffer (Ring Buffer)

**Create:** `src/Fhsm.Kernel/HsmTraceBuffer.cs`

```csharp
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
```

---

### Task 3: Integrate Tracing into Kernel

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Add trace calls at key points:

```csharp
// At top of file
private static HsmTraceBuffer? _traceBuffer = null;

public static void SetTraceBuffer(HsmTraceBuffer? buffer)
{
    _traceBuffer = buffer;
}

// In ExecuteTransition (after computing LCA)
if (_traceBuffer != null)
{
    _traceBuffer.WriteTransition(
        header->MachineId,  // Use MachineId as instance ID
        transition.SourceStateIndex,
        transition.TargetStateIndex,
        transition.EventId);
}

// In exit actions loop
if (_traceBuffer != null)
{
    _traceBuffer.WriteStateChange(header->MachineId, stateId, false);  // Exit
}

// In entry actions loop
if (_traceBuffer != null)
{
    _traceBuffer.WriteStateChange(header->MachineId, stateId, true);  // Entry
}

// In ExecuteAction
if (_traceBuffer != null)
{
    _traceBuffer.WriteActionExecuted(header->MachineId, actionId);
}

// In EvaluateGuard (return statement)
bool result = HsmActionDispatcher.EvaluateGuard(guardId, instancePtr, contextPtr, eventId);

if (_traceBuffer != null)
{
    _traceBuffer.WriteGuardEvaluated(
        ((InstanceHeader*)instancePtr)->MachineId,
        guardId,
        result,
        0);  // transitionIndex if available
}

return result;

// In ProcessEventPhase (after dequeue)
if (_traceBuffer != null)
{
    byte result = 0;  // 0=consumed
    if ((evt.Flags & EventFlags.IsDeferred) != 0)
        result = 1;  // 1=deferred
    
    _traceBuffer.WriteEventHandled(header->MachineId, evt.EventId, result);
}
```

---

### Task 4: Per-Instance Trace Filtering

**Update:** `src/Fhsm.Kernel/Data/Enums.cs`

Add flag to `InstanceFlags`:

```csharp
[Flags]
public enum InstanceFlags : byte
{
    None = 0,
    Error = 1 << 0,
    Paused = 1 << 1,
    DebugTrace = 1 << 2,    // NEW: Enable tracing for this instance
    Reserved3 = 1 << 3,
    Reserved4 = 1 << 4,
    Reserved5 = 1 << 5,
    Reserved6 = 1 << 6,
    Reserved7 = 1 << 7,
}
```

**Update tracing checks in kernel:**

```csharp
// Only trace if instance has DebugTrace flag AND trace buffer exists
if (_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
{
    _traceBuffer.WriteTransition(...);
}
```

---

## 🧪 Testing Requirements

**File:** `tests/Fhsm.Tests/Tooling/TraceTests.cs` (NEW)

**Minimum 20 tests:**

### Trace Buffer Tests (8)
1. Trace buffer creation
2. Clear resets write position
3. Ring buffer wraps around when full
4. GetTraceData returns correct span
5. BytesWritten tracks position
6. FilterLevel filters records
7. CurrentTick increments
8. Multiple records written in sequence

### Trace Record Tests (6)
9. WriteTransition writes correct format
10. WriteEventHandled writes correct format
11. WriteStateChange (entry) writes correct format
12. WriteStateChange (exit) writes correct format
13. WriteGuardEvaluated writes correct format
14. WriteActionExecuted writes correct format

### Filtering Tests (3)
15. Tier1 filter includes transitions, events, state changes
16. Tier2 filter includes actions, timers
17. Tier3 filter includes guards, activities

### Integration Tests (3)
18. Trace records emitted during transition
19. Per-instance filtering (DebugTrace flag)
20. Trace buffer can be enabled/disabled

---

## 📊 Success Criteria

- [ ] TASK-T02 completed (Debug Trace Buffer)
- [ ] Trace records defined (10 opcodes)
- [ ] Ring buffer implemented (64KB, zero-allocation)
- [ ] Kernel integrated (trace calls at key points)
- [ ] Per-instance filtering (InstanceFlags.DebugTrace)
- [ ] Per-tier filtering (Tier 1/2/3)
- [ ] 20+ tests, all passing
- [ ] 182+ total tests passing
- [ ] No allocation in trace path

---

## ⚠️ Common Pitfalls

1. **Allocations:** Trace buffer must be zero-allocation at runtime
2. **Ring Buffer:** Handle wraparound correctly (overwrite old data)
3. **Filtering:** Check both instance flag AND buffer level
4. **Thread Safety:** Trace buffer is thread-local (not shared)
5. **Size:** Fixed-size records for fast parsing
6. **Timestamp:** Use ushort (wraps, but that's OK for short traces)

---

## 📚 Reference

- **Task:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) - TASK-T02
- **Design:** `docs/design/HSM-design-talk.md` - Lines 657-669, 1944-1992
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q8 (All modes)

---

**This enables powerful diagnostics without runtime cost when disabled!** 🔍
