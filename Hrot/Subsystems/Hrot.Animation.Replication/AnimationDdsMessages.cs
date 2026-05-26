using System.Runtime.InteropServices;

namespace Hrot.Animation.Replication;

// =============================================================================
// DDS wire-format structs for Animation Replication topics (BATCH-13, DD-3 §5).
// These are internal to this assembly — only AnimationReplicationModule is public.
// All structs are blittable and fixed-size for zero-copy DDS serialization.
// =============================================================================

// ── Channel topics ────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DdsAnimationChannelIntent
{
    public long EntityId;
    public ushort ActiveAction;
    public ushort Pad1;
    public uint ActionInstanceId;
    public uint BehaviorInstanceId;
    // 32-byte action-parameter payload (matches AnimationChannel.Params).
    public fixed byte ActionParams[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsAnimationChannelStatus
{
    public long EntityId;
    public byte Status;           // NodeStatus cast to byte
    public byte Pad1;
    public ushort Pad2;
    public uint DispatchedInstanceId;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DdsLookAtChannelIntent
{
    public long EntityId;
    public ushort ActiveAction;
    public ushort Pad1;
    public uint ActionInstanceId;
    public uint BehaviorInstanceId;
    // 32-byte action-parameter payload (matches LookAtChannel.Params).
    public fixed byte ActionParams[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsLookAtChannelStatus
{
    public long EntityId;
    public byte Status;           // NodeStatus cast to byte
    public byte Pad1;
    public ushort Pad2;
    public uint DispatchedInstanceId;
}

// ── Descriptor topics ─────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct DdsStanceIntent
{
    public long EntityId;
    public byte TargetStance;     // StanceId cast to byte
    public byte Pad1;
    public ushort Pad2;
    public float BlendTime;
    public uint Version;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsStanceStatus
{
    public long EntityId;
    public byte CurrentStance;    // StanceId cast to byte
    public byte Phase;            // StanceTransitionPhase cast to byte
    public ushort Pad1;
    public float TransitionProgress;
    public uint AckVersion;
}

// ── Side-buffer topics ────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DdsMontageQueue
{
    public long EntityId;
    public uint QueueVersion;
    public byte Count;
    private byte _pad0;
    private ushort _pad1;
    // 128 bytes = 8 * 16 (sizeof MontageQueueEntry).
    // Only Count slots are considered valid by the receiver.
    public fixed byte EntriesData[128];
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsMontageQueueState
{
    public long EntityId;
    public byte CurrentEntryIndex;
    public byte InBlendOutWindow;  // bool as byte for blittability
    public ushort Pad;
    public float EntryElapsedSeconds;
}

// ── Event topics ──────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct DdsMontageStartedEvent
{
    public long Target;
    public int MontageId;
    public uint ActionInstanceId;
    public byte QueueIndex;
    private byte _p0;
    private ushort _p1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsMontageEndedEvent
{
    public long Target;
    public int MontageId;
    public uint ActionInstanceId;
    public byte QueueIndex;
    public byte EndReason;        // MontageEndReason cast to byte
    private ushort _p;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsMontageSectionAdvancedEvent
{
    public long Target;
    public int MontageId;
    public byte FromSectionIndex;
    public byte ToSectionIndex;
    private ushort _p;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsStanceChangedEvent
{
    public long Target;
    public byte PreviousStance;   // StanceId cast to byte
    public byte NewStance;        // StanceId cast to byte
    private ushort _p;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsHitWindowOpenedEvent
{
    public long Target;
    public int MontageId;
    public byte WindowId;
    private byte _p0;
    private ushort _p1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsHitWindowClosedEvent
{
    public long Target;
    public int MontageId;
    public byte WindowId;
    private byte _p0;
    private ushort _p1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DdsAnimNotifyEvent
{
    public long Target;
    public int MontageId;
    public uint MarkerHash;
    public float PayloadFloat;
    private int _pad;
}
