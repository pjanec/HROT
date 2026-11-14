using System.Runtime.InteropServices;
using Fbt;
using Fdp.Toolkit.Behavior;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LocomotionChannel)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct LocomotionChannel
    {
        public ushort ActiveAction;
        /// <summary>⭐ <c>R-3039</c> S1 — the action the dispatcher last ENTERED (its OnExit is owed when the next one arrives); was the
        /// dispatcher's <c>_previousAction[entity.Index]</c>. In the pad after <see cref="ActiveAction"/>: the layout's size is unchanged.</summary>
        public ushort DispatchedAction;
        public uint BehaviorInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;

        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
        public fixed byte State[BehaviorConstants.ActionStateByteSIze];
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponChannel)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct WeaponChannel
    {
        public ushort ActiveAction;
        /// <summary>⭐ <c>R-3039</c> S1 — the action the dispatcher last ENTERED (its OnExit is owed when the next one arrives); was the
        /// dispatcher's <c>_previousAction[entity.Index]</c>. In the pad after <see cref="ActiveAction"/>: the layout's size is unchanged.</summary>
        public ushort DispatchedAction;
        public uint BehaviorInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;

        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
        public fixed byte State[BehaviorConstants.ActionStateByteSIze];
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.InteractionChannel)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct InteractionChannel
    {
        public ushort ActiveAction;
        /// <summary>⭐ <c>R-3039</c> S1 — the action the dispatcher last ENTERED (its OnExit is owed when the next one arrives); was the
        /// dispatcher's <c>_previousAction[entity.Index]</c>. In the pad after <see cref="ActiveAction"/>: the layout's size is unchanged.</summary>
        public ushort DispatchedAction;
        public uint BehaviorInstanceId;
        public uint ActionInstanceId;
        public uint DispatchedInstanceId;
        public NodeStatus Status;

        public fixed byte Params[BehaviorConstants.ActionParamsByteSize];
        public fixed byte State[BehaviorConstants.ActionStateByteSIze];
    }
}
