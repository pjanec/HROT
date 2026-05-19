using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// 1024-byte unmanaged ring buffer of FastHSM trace records. Per-entity
    /// trace memory; opt-in via <c>DebugState.EnableTraceBuffer</c>. Recorded
    /// by the Flight Recorder (NoSave keeps it out of scenario JSON only).
    /// </summary>
    /// <remarks>
    /// Identical layout to <see cref="BTreeTraceWorkingMemory1024"/>. All HSM
    /// records use a strict 16-byte slot stride (12-byte payload structs are
    /// zero-padded to 16). <see cref="WritePos"/> is always pre-wrapped inside
    /// <c>[0, PayloadBytes)</c>. The active <c>TraceLevel</c> filter is carried
    /// in the stack-local <c>HsmTraceContext</c> built each tick (not stored
    /// here) so toggling verbosity does not dirty the ECS chunk.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 1024)]
    [ComponentId(BehaviorApplicationComponentIds.HsmTraceWorkingMemory)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct HsmTraceWorkingMemory1024
    {
        public const int RecordStride    = 16;
        public const int CapacityRecords = 63;
        public const int PayloadBytes    = CapacityRecords * RecordStride; // 1008
        public const int BufferBytes     = 1016;

        public ushort WritePos;
        public ushort RecordCount;
        public uint   LastInstanceId;
        public fixed byte Buffer[BufferBytes];
    }
}
