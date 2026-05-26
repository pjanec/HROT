using System;
using System.Runtime.InteropServices;

namespace Hrot.MuscleCharacter.Animation.Contracts
{
    /// <summary>
    /// Animation channel action ID constants following the ActiveAction ushort convention.
    /// Each ID maps to a dispatcher action in the AnimationDispatcherSystem.
    /// </summary>
    public static class AnimationActionIds
    {
        /// <summary>Play a single montage, preempting any current montage with optional crossfade.</summary>
        public const ushort PlayMontage = 1;

        /// <summary>Stop the current montage with blend-out.</summary>
        public const ushort StopMontage = 2;

        /// <summary>Start a queued chain of montages (uses AnimationMontageQueue).</summary>
        public const ushort PlayMontageQueue = 3;

        /// <summary>Enqueue a single montage to the currently-running chain (appends to AnimationMontageQueue).</summary>
        public const ushort EnqueueMontage = 4;

        /// <summary>Clear all future queue entries (Brain-side direct mutation is preferred; see DD-1 §6.4).</summary>
        public const ushort ClearMontageQueue = 5;
    }

    /// <summary>
    /// Look-at channel action ID constants following the ActiveAction ushort convention.
    /// Each ID maps to a look-at intent in the LookAtDispatcherSystem.
    /// </summary>
    public static class LookAtActionIds
    {
        /// <summary>Aim at a world-space point.</summary>
        public const ushort LookAtPoint = 10;

        /// <summary>Aim at a target entity (resolved via NetworkEntityMap on Muscle).</summary>
        public const ushort LookAtEntity = 11;

        /// <summary>Release aim, blending back to neutral.</summary>
        public const ushort ReleaseLook = 12;
    }

    /// <summary>
    /// Parameters for PlayMontage action (fits in 32-byte ActionParams blob).
    /// Defines one-shot playback configuration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayMontageParams
    {
        /// <summary>Stable ID (hash) of the montage to play.</summary>
        public int MontageId;

        /// <summary>Time in seconds to blend from neutral into this montage (0 = immediate).</summary>
        public float BlendInTime;

        /// <summary>Time in seconds to blend out back to neutral on completion/interrupt.</summary>
        public float BlendOutTime;

        /// <summary>Playback speed multiplier (1.0 = normal, 0.5 = half-speed, 2.0 = double-speed).</summary>
        public float PlayRate;

        /// <summary>Section index to begin playback (0 = first section).</summary>
        public byte StartSectionIndex;

        /// <summary>Number of times to loop (0 = play once and end).</summary>
        public byte LoopCount;

        /// <summary>Priority for conflict arbitration (higher = more important).</summary>
        public byte Priority;

        /// <summary>Bitfield flags for playback behavior (e.g. UseRootMotion).</summary>
        public byte Flags;

        // 4 + 4 + 4 + 4 + 1 + 1 + 1 + 1 = 20 bytes, padded to 32
        private uint _pad1;
        private ulong _pad2;
    }

    /// <summary>
    /// Parameters for StopMontage action (fits in 32-byte ActionParams blob).
    /// Defines blend-out behavior when aborting the current montage.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StopMontageParams
    {
        /// <summary>Time in seconds to blend out to neutral (0 = immediate, < 0 = use default).</summary>
        public float BlendOutTime;

        /// <summary>Reason for stopping (user interrupt, capability loss, etc.).</summary>
        public byte StopReason;

        // ── Padding to 32 bytes ──
        private byte _pad1;
        private ushort _pad2;
        private uint _pad3;
        private ulong _pad4;
    }

    /// <summary>
    /// Parameters for PlayMontageQueue action (fits in 32-byte ActionParams blob).
    /// Defines queue-trigger configuration; actual queue entries live in AnimationMontageQueue component.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayMontageQueueParams
    {
        /// <summary>Initial blend-in time for the first montage in the queue.</summary>
        public float InitialBlendInTime;

        /// <summary>Priority for conflict arbitration.</summary>
        public byte Priority;

        /// <summary>Bitfield flags for queue playback.</summary>
        public byte Flags;

        // ── Padding to 32 bytes ──
        private ushort _pad1;
        private uint _pad2;
        private ulong _pad3;
        private uint _pad4;
    }

    /// <summary>
    /// Parameters for EnqueueMontage action (fits in 32-byte ActionParams blob).
    /// Appends a single montage entry to the currently-running AnimationMontageQueue.
    /// No DispatchedInstanceId bump (non-preemptive append operation).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EnqueueParams
    {
        /// <summary>Stable ID (hash) of the montage to append to the queue.</summary>
        public int MontageId;

        /// <summary>Time in seconds to crossfade from the previous entry into this one.</summary>
        public float BlendIntoTime;

        /// <summary>Playback speed multiplier (1.0 = normal).</summary>
        public float PlayRate;

        /// <summary>Section index to begin playback (0 = first section).</summary>
        public byte StartSectionIndex;

        /// <summary>Bitfield flags for playback behavior.</summary>
        public byte Flags;

        // 4 + 4 + 4 + 1 + 1 = 14 bytes, padded to 32
        private ushort _pad1;
        private ulong _pad2;
        private ulong _pad3;
    }

    /// <summary>
    /// Parameters for LookAtPoint action (fits in 32-byte ActionParams blob).
    /// Targets a fixed world-space point for aim/aim layer overlay.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LookAtPointParams
    {
        /// <summary>World-space target point (X, Y, Z).</summary>
        public float WorldPointX;
        public float WorldPointY;
        public float WorldPointZ;

        /// <summary>Time in seconds to blend aim weight from 0 to full.</summary>
        public float BlendInTime;

        /// <summary>Priority for conflict arbitration (higher = more important).</summary>
        public byte Priority;

        // ── Padding to 32 bytes ──
        private byte _pad1;
        private ushort _pad2;
        private ulong _pad3;
    }

    /// <summary>
    /// Parameters for LookAtEntity action (fits in 32-byte ActionParams blob).
    /// Targets a dynamic entity for aim/aim layer overlay.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LookAtEntityParams
    {
        /// <summary>Network ID or local entity handle of the target.</summary>
        public uint TargetEntityId;

        /// <summary>Local offset from target's transform origin (X, Y, Z).</summary>
        public float LocalOffsetX;
        public float LocalOffsetY;
        public float LocalOffsetZ;

        /// <summary>Time in seconds to blend aim weight from 0 to full.</summary>
        public float BlendInTime;

        /// <summary>Priority for conflict arbitration.</summary>
        public byte Priority;

        // ── Padding to 32 bytes ──
        private byte _pad1;
        private ushort _pad2;
        private uint _pad3;
    }

    /// <summary>
    /// Parameters for ReleaseLook action (fits in 32-byte ActionParams blob).
    /// Terminates aim/look-at overlay, blending back to neutral.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReleaseLookParams
    {
        /// <summary>Time in seconds to blend aim weight back to 0.</summary>
        public float BlendOutTime;

        // ── Padding to 32 bytes ──
        private uint _pad1;
        private ulong _pad2;
        private ulong _pad3;
        private ulong _pad4;
    }
}
