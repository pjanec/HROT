using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    [Flags]
    public enum ActorCapabilities : byte
    {
        None = 0,
        CanMove = 1,
        CanShoot = 2,
        CanInteract = 4,
        CanPlayAnimations = 8,
        CanChangeStance = 16,
        CanAim = 32
    }

    /// <summary>
    /// Shadow component that records the capability bitmask from the previous frame.
    /// Used by <c>HsmDamageBridgeSystem</c> to detect transitions (e.g. CanMove → cleared).
    /// Must be initialised to the entity's initial capabilities at spawn.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PreviousCapabilities)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct PreviousCapabilities
    {
        public ActorCapabilities Capabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ActorCapabilityState)]
    public struct ActorCapabilityState
    {
        public ActorCapabilities Capabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BehaviorState)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct BehaviorState
    {
        public int ActiveBehaviorHash;
        public uint InstanceId; // Preemption token
        public byte BrainTier;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SimTier)]
    public struct SimTier
    {
        public byte Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = BehaviorConstants.BrainBlackboardByteSize)]
    [ComponentId(GlobalComponentIds.BrainBlackboard)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct BrainBlackboard
    {
        /// <summary>
        /// Polymorphic behavior parameter payload at the start of the blackboard.
        /// AI developers project their specific DTO (e.g. <c>FireAtTargetParams</c>) onto
        /// this region using <c>Unsafe.As</c>.  Must not exceed
        /// <see cref="BehaviorConstants.MaxBehaviorParamByteSize"/> bytes.
        /// </summary>
        [FieldOffset(0)]
        public fixed byte BehaviorParameters[BehaviorConstants.MaxBehaviorParamByteSize];

        /// <summary>
        /// Per-waypoint threat/danger level written by <c>RouteContextSystem</c>.
        /// A value of 0 means unknown/default; higher values indicate increasing danger.
        /// </summary>
        [FieldOffset(BehaviorConstants.BrainBlackboardByteSize-8)]
        public byte ExpectedThreatLevel;

        /// <summary>
        /// MobilityLost edge-triggered interrupt.
        /// Set to 1 by <c>CognitiveInterruptSystem</c> on the tick <c>CanMove</c> transitions
        /// from set to cleared.  Cleared back to 0 by <c>CognitiveCleanupSystem</c> at end of frame.
        /// </summary>
        [FieldOffset(BehaviorConstants.BrainBlackboardByteSize-2)]
        public byte Interrupt_MobilityLost;

        /// <summary>Reserved for future hardware-level interrupt.</summary>
        [FieldOffset(BehaviorConstants.BrainBlackboardByteSize-1)]
        public byte Interrupt_Reserved;
    }

    /// <summary>
    /// Generic 1024-byte heavy blackboard component, reusable across different behaviors.
    /// Avoids exhausting the 256 component-type limit by sharing one component type for
    /// large behavior-specific payloads.  Project <see cref="Memory"/> into a concrete
    /// unmanaged DTO via <c>Unsafe.As</c> (generated automatically when using
    /// <c>[SharedAiHeavyAction]</c>).  Because this holds transient execution state,
    /// it is excluded from scenario serialisation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Blackboard1024)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct Blackboard1024
    {
        public const int ByteSize = 1024;
        public fixed byte Memory[ByteSize];

        /// <summary>
        /// Projects the 1024-byte memory block as a reference to an unmanaged struct <typeparamref name="T"/>.
        /// <typeparamref name="T"/> must fit within <see cref="ByteSize"/> bytes (assert at call site, not here).
        /// Convention: each subsystem projects at a disjoint byte offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref T Project<T>(ref Blackboard1024 bb) where T : unmanaged
            => ref Unsafe.As<Blackboard1024, T>(ref bb);
    }
}
