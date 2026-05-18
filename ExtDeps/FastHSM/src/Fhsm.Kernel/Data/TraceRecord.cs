using System;
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
        Conflict = 0x0D,          // NEW: Output lane conflict
        Error = 0xFF,             // Critical error
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

    /// <summary>
    /// Critical error trace record (12 bytes).
    /// OpCode: Error (0xFF)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    public struct TraceError
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort ErrorCode;
        [FieldOffset(10)] public ushort Reserved;
    }

    /// <summary>
    /// Conflict trace record (12 bytes).
    /// OpCode: Conflict (0x0D)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    public struct ConflictRecord
    {
        [FieldOffset(0)] public TraceRecordHeader Header;
        [FieldOffset(8)] public ushort StateIndex;
        [FieldOffset(10)] public byte AttemptedLanes;
        [FieldOffset(11)] public byte ConflictingLanes;
    }

    /// <summary>
    /// Unified trace record for symbolication (Union of all types).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct TraceRecord
    {
        // Header
        [FieldOffset(0)] public TraceOpCode OpCode;
        [FieldOffset(2)] public ushort Timestamp;
        [FieldOffset(4)] public uint InstanceId;

        // Fields (Overlapping)
        [FieldOffset(8)] public ushort StateIndex;      // Enter/Exit/Transition(From)
        [FieldOffset(8)] public ushort EventId;         // EventHandled
        [FieldOffset(8)] public ushort ActionId;        // ActionExecuted
        [FieldOffset(8)] public ushort GuardId;         // GuardEvaluated
        [FieldOffset(8)] public ushort ErrorCode;       // Error
        
        [FieldOffset(10)] public ushort TargetStateIndex; // Transition(To)
        [FieldOffset(10)] public byte GuardResult;        // GuardEvaluated
        
        [FieldOffset(12)] public ushort TriggerEventId; // Transition(Trigger)
    }
}
