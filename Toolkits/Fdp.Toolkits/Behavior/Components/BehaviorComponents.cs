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
    [ComponentId(GlobalComponentIds.DoctrineState)]
    public struct DoctrineState
    {
        public int ActiveDoctrineHash;
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
    public unsafe struct BrainBlackboard
    {
        public fixed byte Memory[BehaviorConstants.BrainBlackboardByteSize];
    }
}
