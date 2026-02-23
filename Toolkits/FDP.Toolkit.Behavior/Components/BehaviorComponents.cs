using System;
using System.Runtime.InteropServices;

namespace FDP.Toolkit.Behavior.Components
{
    [Flags]
    public enum ActorCapabilities : byte
    {
        None = 0,
        CanMove = 1,
        CanShoot = 2,
        CanInteract = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ActorCapabilityState
    {
        public ActorCapabilities Capabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DoctrineState
    {
        public int ActiveDoctrineHash;
        public uint InstanceId; // Preemption token
        public byte BrainTier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimTier
    {
        public byte Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BrainBlackboard
    {
        public fixed byte Memory[128];
    }
}
