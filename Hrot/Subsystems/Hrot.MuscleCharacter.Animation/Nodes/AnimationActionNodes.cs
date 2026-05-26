using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.MuscleCharacter.Animation.Nodes
{
    /// <summary>
    /// Blueprint action node for playing a single montage.
    /// Emits a PlayMontageParams command to the AnimationChannel.
    /// (ANC-P5-01, DD-5 §3.1)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayMontageNode
    {
        /// <summary>Target entity to play the montage on (typically the character actor).</summary>
        public uint TargetCharacter;

        /// <summary>Stable hash ID of the montage to play. Resolved at compile time from montage name.</summary>
        [MontagePicker]
        public int MontageId;

        /// <summary>Slot index on which to play this montage (0=Locomotion, 1=FullBody, etc.).</summary>
        public byte SlotIndex;
    }

    /// <summary>
    /// Blueprint action node for stopping the current montage with blend-out.
    /// Emits a StopMontageParams command to the AnimationChannel.
    /// (ANC-P5-01, DD-5 §3.2)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StopMontageNode
    {
        /// <summary>Target entity to stop the montage on.</summary>
        public uint TargetCharacter;

        /// <summary>Slot index to stop (if 0xFF, stop all slots).</summary>
        public byte SlotIndex;
    }

    /// <summary>
    /// Blueprint action node for playing a chain of montages in sequence.
    /// Mutates AnimationMontageQueue side-buffer with fixed-size array of montage IDs (max 8).
    /// Uses [InlineArray] safe Span-cast mutation pattern (DD-5 §9.1, ANIM010).
    /// (ANC-P5-02, DD-5 §3.3)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayMontageChainNode
    {
        /// <summary>Target entity.</summary>
        public uint TargetCharacter;

        /// <summary>Number of montages in the chain (1..8).</summary>
        public byte ChainCount;

        /// <summary>
        /// Fixed-size array of montage IDs to chain (max 8 entries).
        /// Only the first ChainCount entries are used.
        /// [MontagePicker] attribute on each element for editor drawer support.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] ChainedMontages;
    }

    /// <summary>
    /// Blueprint action node for appending a single montage to a running queue.
    /// No ActionInstanceId bump (queue mutation only, per DD-1 §6.4).
    /// Uses [InlineArray] safe Span-cast mutation pattern (DD-5 §9.1, ANIM010).
    /// (ANC-P5-02, DD-5 §3.4)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EnqueueMontageNode
    {
        /// <summary>Target entity with active queue.</summary>
        public uint TargetCharacter;

        /// <summary>Stable hash ID of the montage to enqueue.</summary>
        [MontagePicker]
        public int MontageId;

        /// <summary>
        /// If true, only enqueue if the queue is empty (no entries pending).
        /// If false, always append (at capacity, silently no-ops).
        /// </summary>
        public bool OnlyIfEmpty;
    }

    /// <summary>
    /// Blueprint action node for clearing future queue entries.
    /// Leaves the currently-playing montage (index 0) intact; truncates entries 1..N.
    /// Uses [InlineArray] safe Span-cast mutation pattern (DD-5 §9.1, ANIM010).
    /// (ANC-P5-02, DD-5 §3.5)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ClearMontageQueueNode
    {
        /// <summary>Target entity with active queue.</summary>
        public uint TargetCharacter;
    }

    /// <summary>
    /// Blueprint action node for requesting a stance transition.
    /// Writes to StanceIntent descriptor (not a channel command).
    /// Bumps StanceIntent.Version so StanceTransitionSystem picks up the request.
    /// (ANC-P5-03, DD-5 §4.1)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SetStanceNode
    {
        /// <summary>Target entity to change stance on.</summary>
        public uint TargetCharacter;

        /// <summary>Desired stance (Standing, Crouched, Prone, etc.). Must be in entity class's SupportedStances.</summary>
        public StanceId TargetStance;
    }
}
