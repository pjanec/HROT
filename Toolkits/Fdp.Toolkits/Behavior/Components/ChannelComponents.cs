using System.Runtime.InteropServices;
using Fbt;
using Fdp.Toolkit.Behavior;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LocomotionChannel)]
    public unsafe struct LocomotionChannel
    {
        public ushort ActiveAction;
        public uint DoctrineInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;

        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
        public fixed byte State[BehaviorConstants.ActionStateByteSIze];
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponChannel)]
    public unsafe struct WeaponChannel
    {
        public ushort ActiveAction;
        public uint DoctrineInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;

        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
        public fixed byte State[BehaviorConstants.ActionStateByteSIze];
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.InteractionChannel)]
    public unsafe struct InteractionChannel
    {
        public ushort ActiveAction;
        public uint DoctrineInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;

        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
        public fixed byte State[BehaviorConstants.ActionStateByteSIze];
    }
}
