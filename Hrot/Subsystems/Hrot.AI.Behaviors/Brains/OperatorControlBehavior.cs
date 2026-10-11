using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-3172</c> / <c>CE-3173</c> (M-1, M-7; 📄 docs/DESIGN_Entity_Control.md §8b) — <c>OperatorControl</c>, what a POSSESSED
    /// unit runs: <see cref="PossessionSystem"/> assigns it at origin <c>Possession</c> (which pauses the unit's task), and its one node
    /// hands the station's newest frame (<see cref="OperatorInput"/>) to the body as <see cref="ManualDrive"/>, with the motion request
    /// in <see cref="NavigationMode.Manual"/>. ⛔ Not an AI-off switch (R-270): it is a behaviour like any other, and clearing it resumes
    /// the paused task.
    /// </summary>
    public static class OperatorControlBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ManualControlParams
        {
            /// <summary>Unused — the node takes its axes from <see cref="OperatorInput"/>.</summary>
            public byte Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ManualControlState
        {
            /// <summary>The frame last handed to the body.</summary>
            public uint Seq;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OperatorControlBlackboard
        {
            public ManualControlParams Control;
            public ManualControlState ControlState;
        }

        /// <summary>
        /// Every tick: the motion request is <see cref="NavigationMode.Manual"/> (entered once — and again whenever anything else
        /// cleared it, e.g. the paused task's move leaving), and the newest frame becomes <see cref="ManualDrive"/>. No frame yet ⇒ the
        /// body holds still. Runs until the possession is released; ⚠ the let-go on release is <c>PossessionSystem</c>'s (a root
        /// behaviour's deactivators do not run when it is cleared — DESIGN_Behaviour_Fault_And_Teardown, rejected alternatives).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus ManualControl(ref ManualControlParams p, ref ManualControlState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<NavigationIntent>(self)) return NodeStatus.Running;   // nothing to drive
            ref var intent = ref world.GetComponentRW<NavigationIntent>(self);
            if (intent.Mode != NavigationMode.Manual)
            {
                intent.Mode = NavigationMode.Manual;
                intent.TargetSpeed = 0f;
                unchecked { intent.IntentId++; }   // the bridge applies a mode only on a new IntentId
            }

            if (!world.IsComponentTypeRegistered<ManualDrive>()) world.RegisterComponent<ManualDrive>();
            var drive = default(ManualDrive);
            if (world.IsComponentTypeRegistered<OperatorInput>() && world.HasComponent<OperatorInput>(self))
            {
                var input = world.GetComponentRO<OperatorInput>(self);
                drive = new ManualDrive
                {
                    Forward = input.Forward, Lateral = input.Lateral, Steer = input.Steer, Yaw = input.Yaw, Gait = input.Gait,
                    Seq = input.Seq,
                };
            }
            else if (world.HasComponent<ManualDrive>(self))
                drive.Yaw = world.GetComponentRO<ManualDrive>(self).Yaw;   // no frame: stop, keep facing
            ws.Seq = drive.Seq;
            if (world.HasComponent<ManualDrive>(self)) world.SetComponent(self, drive);
            else world.AddComponent(self, drive);
            return NodeStatus.Running;
        }

        [BTreeDefinition(BehaviorNames.OperatorControl, Curated = true, Group = "Operator", ParamsType = typeof(OperatorControlBlackboard))]
        public static BTreeBuilder<OperatorControlBlackboard, BTreeContext> BuildOperatorControlTree()
            => new BTreeBuilder<OperatorControlBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Control, bb => bb.ControlState, ManualControl);
    }
}
