using System.Runtime.InteropServices;
using Fbt;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// 16-byte fixed-stride BTree execution-trace record. The 8-byte header is
    /// always populated; the 8-byte payload is a C-style union — readers
    /// dispatch by <see cref="OpCode"/>.
    /// </summary>
    /// <remarks>
    /// Payload field layout (offsets 8..15):
    /// <list type="bullet">
    ///   <item>NodeEvaluated:  NodeIndex (8-9), Status (10), pad (11-15)</item>
    ///   <item>ScopePushed/Popped: StackDepth (8-9), pad (10-15)</item>
    ///   <item>WaitStarted/Completed: NodeIndex (8-9), pad (10-11), Duration (12-15)</item>
    ///   <item>ChannelMutated: NodeIndex (8-9), Channel (10), pad (11), ActiveAction (12-13), ChannelStatus (14), pad (15)</item>
    ///   <item>Error: NodeIndex (8-9), ErrorCode (10-11), pad (12-15)</item>
    /// </list>
    /// NodeIndex aliases offset 8 across all opcodes that carry a node reference,
    /// so the renderer can always read it via the same offset.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct BTreeTraceRecord
    {
        // ── Header (8 bytes) ────────────────────────────────────────────────
        [FieldOffset(0)] public BTreeTraceOpCode OpCode;
        [FieldOffset(1)] public byte             Reserved;
        [FieldOffset(2)] public ushort           Timestamp;
        [FieldOffset(4)] public uint             InstanceId;

        // ── Payload union (8 bytes) ─────────────────────────────────────────

        // NodeIndex aliases offset 8 for NodeEvaluated / Wait* / ChannelMutated / Error
        [FieldOffset(8)]  public ushort     NodeIndex;
        // StackDepth aliases offset 8 for Scope*
        [FieldOffset(8)]  public ushort     StackDepth;

        // NodeEvaluated
        [FieldOffset(10)] public NodeStatus Status;

        // ChannelMutated
        [FieldOffset(10)] public byte       Channel;        // ChannelKind enum stored as byte
        [FieldOffset(12)] public ushort     ActiveAction;
        [FieldOffset(14)] public NodeStatus ChannelStatus;

        // Wait* — Duration sits at offset 12 to keep NodeIndex at offset 8 readable
        [FieldOffset(12)] public float      Duration;

        // Error
        [FieldOffset(10)] public ushort     ErrorCode;
    }
}
