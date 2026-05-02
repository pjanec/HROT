using System;
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
        CanInteract = 4
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

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BrainBlackboard)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct BrainBlackboard
    {
        public fixed byte Memory[BehaviorConstants.BrainBlackboardByteSize];
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
    }
}
