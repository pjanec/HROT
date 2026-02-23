using System.Runtime.InteropServices;
using Fbt;

namespace FDP.Toolkit.Behavior.Components
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LocomotionChannel
    {
        public ushort ActiveAction;
        public uint DoctrineInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;
        
        public fixed byte Params[32];
        public fixed byte State[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WeaponChannel
    {
        public ushort ActiveAction;
        public uint DoctrineInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;
        
        public fixed byte Params[32];
        public fixed byte State[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct InteractionChannel
    {
        public ushort ActiveAction;
        public uint DoctrineInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;
        
        public fixed byte Params[32];
        public fixed byte State[32];
    }
}
