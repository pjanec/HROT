using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// One assignment slot within <see cref="ThreatMatrixAssignmentState"/>.
    /// Fixed at 64 bytes so that 16 slots fill exactly one <see cref="Blackboard1024"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
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
        // Remaining 50 bytes are implicit padding via LayoutKind.Sequential Size = 64.
    }

    /// <summary>
    /// Helper wrapper so <see cref="ThreatMatrixAssignmentState"/> can use an inline array of
    /// exactly 16 <see cref="AssignmentSlot"/>s without per-element defensive copies.
    /// </summary>
    [InlineArray(16)]
    public struct AssignmentSlotArray
    {
#pragma warning disable CS0169 // field never used
        private AssignmentSlot _element;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Squad assignment state projected onto a squad leader's <see cref="Blackboard1024"/>.
    /// Tracks which target each subordinate (by roster index) is assigned to.
    /// </summary>
    /// <remarks>
    /// Layout: 16 x <see cref="AssignmentSlot"/> (64 bytes each) = 1024 bytes.
    /// Use <see cref="Project"/> to obtain a ref into the leader's blackboard memory.
    /// Always access slots through <see cref="GetSlot"/> to avoid InlineArray defensive-copy issues.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct ThreatMatrixAssignmentState
    {
        /// <summary>Per-subordinate assignment slots, indexed by roster position.</summary>
        public AssignmentSlotArray Slots;

        /// <summary>
        /// Projects this struct as an overlay onto the first 1024 bytes of <paramref name="bb"/>.
        /// </summary>
        public static ref ThreatMatrixAssignmentState Project(ref Blackboard1024 bb)
            => ref Blackboard1024.Project<ThreatMatrixAssignmentState>(ref bb);

        /// <summary>
        /// Returns a ref to the assignment slot for roster index <paramref name="memberIndex"/>.
        /// Uses <see cref="MemoryMarshal.CreateSpan"/> to avoid the InlineArray defensive-copy trap.
        /// </summary>
        public ref AssignmentSlot GetSlot(int memberIndex)
            => ref MemoryMarshal.CreateSpan(
                ref Unsafe.As<AssignmentSlotArray, AssignmentSlot>(ref Slots), 16)[memberIndex];

        /// <summary>Returns the packed target handle assigned to roster index <paramref name="memberIndex"/>.</summary>
        public long GetAssignedTarget(int memberIndex) => GetSlot(memberIndex).AssignedTargetHandle;

        /// <summary>Assigns <paramref name="targetHandle"/> to roster index <paramref name="slot"/>.</summary>
        public void SetAssignment(int slot, ulong targetHandle)
            => GetSlot(slot).AssignedTargetHandle = (long)targetHandle;
    }
}
