using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// One assignment slot. Exactly 16 bytes:
    /// AssignedTargetHandle(8) + AssignmentScore(4) + FocusFireCount(1) + Flags(1) + _pad(2).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AssignmentSlot
    {
        /// <summary>Packed entity handle of the assigned target, or 0 if unassigned.</summary>
        public long AssignedTargetHandle;
        /// <summary>Score at the time of assignment.</summary>
        public float AssignmentScore;
        /// <summary>Number of squad members currently focusing fire on the same target.</summary>
        public byte FocusFireCount;
        /// <summary>Reserved flags for future use.</summary>
        public byte Flags;
        /// <summary>Explicit 2-byte pad to reach 16-byte total size.</summary>
        private ushort _pad;
    }

    /// <summary>
    /// Inline array of 16 <see cref="AssignmentSlot"/>s (16 * 16 = 256 bytes).
    /// Occupies the Assignment sub-region of <see cref="Fdp.Toolkit.Squad.SquadCognitiveState"/>.
    /// Always access elements through <see cref="GetSlot"/> to avoid the InlineArray defensive-copy trap.
    /// </summary>
    [InlineArray(16)]
    public struct AssignmentSlotArray
    {
#pragma warning disable CS0169 // field never used
        private AssignmentSlot _element;
#pragma warning restore CS0169

        /// <summary>
        /// Returns a ref to the assignment slot for roster index <paramref name="index"/>.
        /// Uses <see cref="MemoryMarshal.CreateSpan"/> to avoid the InlineArray defensive-copy trap.
        /// </summary>
        public ref AssignmentSlot GetSlot(int index)
            => ref MemoryMarshal.CreateSpan(
                ref Unsafe.As<AssignmentSlotArray, AssignmentSlot>(ref this), 16)[index];

        /// <summary>Returns the packed target handle assigned to roster index <paramref name="index"/>.</summary>
        public long GetAssignedTarget(int index) => GetSlot(index).AssignedTargetHandle;

        /// <summary>Assigns <paramref name="targetHandle"/> to roster index <paramref name="index"/>.</summary>
        public void SetAssignment(int index, ulong targetHandle)
            => GetSlot(index).AssignedTargetHandle = (long)targetHandle;
    }
}
