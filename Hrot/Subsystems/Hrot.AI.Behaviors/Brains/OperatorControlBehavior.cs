using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
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
                // ⭐ CE-1074 (DESIGN_Entity_Control.md §13.5) — the operator's stance, through the brain's one way to ask for it
                //   (StanceRequest: a version bump only on a change, so a frame per tick is harmless). 🔒 User, 2026-10-14: "I will
                //   need to be able to switch the possessed character to crouched as well as lying pose (cycling using a key press)".
                if (input.Stance is >= 1 and <= 3)
                    Hrot.MuscleCharacter.Animation.Stance.StanceRequest.Set(world, self, (Fdp.Toolkit.Tkb.Domain.StanceId)(input.Stance - 1), OperatorStanceBlendSeconds);
            }
            else if (world.HasComponent<ManualDrive>(self))
                drive.Yaw = world.GetComponentRO<ManualDrive>(self).Yaw;   // no frame: stop, keep facing
            ws.Seq = drive.Seq;
            if (world.HasComponent<ManualDrive>(self)) world.SetComponent(self, drive);
            else world.AddComponent(self, drive);
            OperateWeapon(world, self);
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-3172</c> S4d — seconds between an operator's rounds. ⚠ A constant: the TKB carries no rate of fire yet
        /// (<c>WeaponMountDto</c> has aim time, magazine and reload, not a cyclic rate).</summary>
        public const float OperatorCooldownSeconds = 0.25f;

        /// <summary>⭐ CE-1074 — how long the operator's stance change blends (s).</summary>
        public const float OperatorStanceBlendSeconds = 0.4f;

        /// <summary>
        /// ⭐ <c>CE-3172</c> S4d (M-8) — the operator's weapon: the unit's weapon channel runs <c>AimAndFire</c> in OPERATOR mode,
        /// aimed at the frame's point, firing while its trigger is held (<c>AimAndFireExecutor</c> keeps ammunition, magazine,
        /// cooldown and the friendly line; it skips seen-now, aim time and the ROE — C-2). Started once, then steered in place each
        /// frame. Stamped with this run's id, so <c>ChannelArbitrationSystem</c> stops it the moment the possession ends.
        /// </summary>
        private static unsafe void OperateWeapon(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<WeaponChannel>(self) || !world.IsComponentTypeRegistered<OperatorInput>()
                || !world.HasComponent<OperatorInput>(self)) return;
            var input = world.GetComponentRO<OperatorInput>(self);
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            ref var p = ref System.Runtime.CompilerServices.Unsafe.As<byte, AimAndFireParams>(ref channel.Params[0]);
            if (channel.ActiveAction != CombatConstants.ActionIdAimAndFire || p.Operator == 0 || channel.Status != NodeStatus.Running)
            {
                if (world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(self))
                    channel.BehaviorInstanceId = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(self).InstanceId;
                unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
                channel.Status = NodeStatus.Running;
            }
            p = new AimAndFireParams
            {
                Operator = 1, Trigger = input.Trigger, Mount = input.Mount, Point = input.AimPoint,
                CooldownSeconds = OperatorCooldownSeconds,
            };
        }

        /// <summary>⭐ <c>CE-2135</c> (P3, B3b-1) — the operator's behaviour takes nothing: its block is internal (the possession sends
        /// <c>"{}"</c>, <c>Possession.cs</c>), so it names <see cref="NoParams"/> and this resolver starts the block clean (the Disembark shape).</summary>
        [BehaviorResolver(BehaviorNames.OperatorControl)]
        public static void Resolve(in NoParams authored, ref OperatorControlBlackboard block, EntityRepository world, Entity self)
            => block.Control = default;

        [Behavior(BehaviorNames.OperatorControl, Params = typeof(NoParams))]   // ⭐ CE-2135 (P3) — it takes nothing (P1: NoParams)
        [BTreeDefinition(BehaviorNames.OperatorControl, Curated = true, Group = "Operator", ParamsType = typeof(OperatorControlBlackboard))]
        public static BTreeBuilder<OperatorControlBlackboard, BTreeContext> BuildOperatorControlTree()
            => new BTreeBuilder<OperatorControlBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Control, bb => bb.ControlState, ManualControl);
    }
}
