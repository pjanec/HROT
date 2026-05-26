using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.MuscleCharacter.Animation.Components
{
    /// <summary>
    /// Animation slot execution state for a single montage playback slot.
    /// Tracks blending, playback progress, and whether the slot is currently active.
    /// Part of the [InlineArray] buffer inside AnimationExecutorState.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationSlotState
    {
        /// <summary>Montage ID currently playing in this slot (0 = empty/inactive).</summary>
        public int CurrentMontageId;

        /// <summary>Elapsed playback time in seconds.</summary>
        public float ElapsedSeconds;

        /// <summary>Blend-in weight (0.0 = silent, 1.0 = full contribution).</summary>
        public float BlendInWeight;

        /// <summary>Blend-out weight; used during transitions to smoothly fade out.</summary>
        public float BlendOutWeight;

        /// <summary>Current section index within the montage.</summary>
        public byte CurrentSectionIndex;

        /// <summary>Flag indicating slot is marked for deactivation after next frame.</summary>
        public bool PendingDeactivation;

        private ushort _pad1;
    }

    /// <summary>
    /// Muscle-internal animation executor state.
    /// Holds the slot table and managed state for all active montage playback.
    /// Not replicated (internal to Muscle node).
    /// 
    /// Layout: 8 slots × ~28 bytes + metadata = ~230 bytes (well under reasonable limits).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimationExecutorState)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct AnimationExecutorState
    {
        /// <summary>Maximum number of concurrent playback slots.</summary>
        public const int MaxSlots = 8;

        /// <summary>Size of each slot state entry (int + float*3 + byte*2 + ushort = 28 bytes).</summary>
        public const int SlotStateSize = 28;

        /// <summary>
        /// Array of slot execution states encoded as raw bytes (8 slots × 28 bytes = 224 bytes).
        /// Cast to Span&lt;AnimationSlotState&gt; using MemoryMarshal.Cast for mutation.
        /// </summary>
        public fixed byte SlotsData[224];  // 8 * 28 bytes

        /// <summary>
        /// MontageId of the last PlayMontage or PlayMontageQueue action that was staged.
        /// Set by PlayMontageExecutor and PlayMontageQueueExecutor on OnEnter.
        /// Read by StopMontageExecutor to publish MontageEndedEvent(Interrupted).
        /// Also read by MontageQueueAdvanceSystem for the PlayMontage+enqueue transition.
        /// </summary>
        public int LastActiveMontageId;
    }

    /// <summary>
    /// Muscle-internal look-at (aim) execution state.
    /// Tracks the current aim target and blend progress.
    /// Not replicated (internal to Muscle node).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LookAtExecutorState)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct LookAtExecutorState
    {
        /// <summary>Current aim target world position (X, Y, Z).</summary>
        public float TargetPointX;
        public float TargetPointY;
        public float TargetPointZ;

        /// <summary>Blend-in weight for smoothing aim acquisition (0.0 = inactive, 1.0 = full).</summary>
        public float BlendInWeight;

        /// <summary>Blend-out weight for smoothing aim release (1.0 = active, 0.0 = inactive).</summary>
        public float BlendOutWeight;

        /// <summary>Type of current target: 0 = none, 1 = point, 2 = entity.</summary>
        public byte TargetType;
    }

    /// <summary>
    /// Character animation definition runtime handle and metadata.
    /// Holds references to baked per-character animation data (montage definitions, stance info, etc.).
    /// Retrieved from the TKB animation descriptor at entity spawn via CharacterAnimationDefRuntime baking.
    /// Not replicated (cached on each node independently).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CharacterAnimationDefRuntime)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct CharacterAnimationDefRuntime
    {
        /// <summary>Opaque handle into the baked animation definition cache (e.g. a long hash or pointer).</summary>
        public long BackendHandle;

        /// <summary>Number of supported stance modes for this character.</summary>
        public byte StanceCount;

        /// <summary>Number of available montage playback slots (max 8).</summary>
        public byte SlotCount;

        private ushort _pad1;
    }
}
