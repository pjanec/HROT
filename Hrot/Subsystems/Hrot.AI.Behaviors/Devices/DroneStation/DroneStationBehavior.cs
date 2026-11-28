using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Motion;
using Hrot.AI.Behaviors.Brains;   // the StatefulAction builder extension
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Devices.DroneStation
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-104 / D-109 / D-110) — THE DRONE GROUND STATION's logic: ONE
    /// stateful action on the DEVICE entity, run every frame (paused or not) by the device tick. Its state is <see cref="DroneStationState"/> (unit memory).
    /// <para>Each tick: ① the presses (<see cref="DevicePressReader"/>); ② POWER and the gimbal (CAM UP / DOWN) act at once — the device's own state, in pause
    /// too; ③ the MODE presses (ARM · TAKEOFF · LAND · RTH) act on a live frame and are QUEUED in a pause for the first live frame (R-4300); ④ the drone it
    /// flies is its own <see cref="ControlledEntities"/> slot 0 (D-147) and its answer is the drone's <see cref="DroneLink"/> (a declared tick-start read): the
    /// station's mode follows OFF → LINKED → ARMED → TAKEOFF → FLYING ⇄ HOVER, LANDING / RETURNING, LANDED, FAILSAFE from its command and that telemetry;
    /// ⑤ on a live frame it writes its command — <see cref="RemoteControl"/> on ITSELF: the mode, the sticks in Mode 2 (left: yaw + throttle, right: roll +
    /// pitch), the gimbal, the seq — which the drone's own brain reads (<c>RemotePilotBehavior</c>, D-104 — never possessed); ⑥ its <see cref="DeviceView"/>:
    /// a powered station films through its drone at the gimbal's pitch (D-109; the station camera rides it).</para>
    /// <para>⭐ The parallel rule: it writes ONLY its own entity; it reads its drone's link (declared, <c>BrainReads</c>). ⭐ 0 B per steady tick.</para>
    /// </summary>
    public static class DroneStationBehavior
    {
        /// <summary>One CAM UP / DOWN press tilts the gimbal by this (15°).</summary>
        public static readonly float PitchStep = 15f * MathF.PI / 180f;
        /// <summary>The gimbal's lowest pitch (straight down).</summary>
        public static readonly float PitchMin = -MathF.PI / 2f;
        /// <summary>A stick inside this reads as centred (the station shows HOVER).</summary>
        public const float StickCentred = 0.05f;

        [StructLayout(LayoutKind.Sequential)]
        public struct DroneStationParams { public byte Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DroneStationWork
        {
            /// <summary>⛔ Reserved, always 0 — a device counts nothing per frame (S1e-3).</summary>
            public uint Ticks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DroneStationBlackboard
        {
            public DroneStationParams Params;
            public DroneStationWork Work;
        }

        [BehaviorResolver(BehaviorNames.DeviceDroneStation)]
        public static void Resolve(in NoParams authored, ref DroneStationBlackboard block, EntityRepository world, Entity self) => block = default;

        // ⛔ Offered to nothing but a device: Requires DeviceInfo (BehaviorFit's AND), so no unit's task picker lists it.
        [Behavior(BehaviorNames.DeviceDroneStation, Params = typeof(NoParams), Requires = new[] { typeof(DeviceInfo) },
                  Description = "A device's logic: the drone ground station (run by the device tick, never assigned).")]
        [BTreeDefinition(BehaviorNames.DeviceDroneStation, Curated = true, Group = "Library/Devices", ParamsType = typeof(DroneStationBlackboard))]
        public static BTreeBuilder<DroneStationBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DroneStationBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.Work, Tick);

        /// <summary>One tick of the ground station (see the class summary). Never ends: Running.</summary>
        [SharedAiAction]
        public static NodeStatus Tick(ref DroneStationParams p, ref DroneStationWork ws, Entity self, EntityRepository world)
        {
            ref var st = ref UnitMemory.Ref<DroneStationState>(world, self);
            Span<int> moved = stackalloc int[DroneStationChannels.Count];
            DevicePressReader.Read(world, self, ref st.Presses, moved);   // ① (re-based on a new operator epoch, R7-11)
            bool live = Fdp.Toolkit.Replication.Attributes.FormAttributes.TimeAdvances(world);

            // ② the device's own state, in pause too
            if ((moved[DroneStationChannels.Power] & 1) != 0)
            {
                st.Power = st.Power == 0 ? (byte)1 : (byte)0;
                st.Command = st.Power != 0 ? RemoteMode.Idle : RemoteMode.Off;
                st.Queued = 0;
            }
            int tilt = moved[DroneStationChannels.CamUp] - moved[DroneStationChannels.CamDown];
            if (tilt != 0) st.CameraPitch = Math.Clamp(st.CameraPitch + tilt * PitchStep, PitchMin, 0f);

            // ④ its drone and the drone's answer
            var drone = DroneOf(world, self);
            st.Drone = drone;
            var link = RemoteLinks.Read(world, drone);
            bool linked = st.Power != 0 && !drone.IsNull && link.Lost == 0;

            // ③ the mode presses: live ⇒ now (the queue first, in press order); paused ⇒ queued (R-4300)
            byte pressed = 0;
            for (int b = DroneStationChannels.Arm; b <= DroneStationChannels.Rth; b++) if (moved[b] > 0) pressed |= DroneStationChannels.Bit(b);
            if (st.Power == 0) st.Queued = 0;
            else if (!live) st.Queued |= pressed;
            else
            {
                byte all = (byte)(st.Queued | pressed);
                st.Queued = 0;
                for (int b = DroneStationChannels.Arm; b <= DroneStationChannels.Rth; b++)
                    if ((all & DroneStationChannels.Bit(b)) != 0) Apply(ref st, b, linked, link.Phase);
            }

            // the sticks, Mode 2, in the drone's frame (Lateral + = left, Yaw + = counter-clockwise)
            float yaw = 0f, vertical = 0f, roll = 0f, pitch = 0f;
            if (world.IsComponentTypeRegistered<DeviceInput>() && world.HasComponent<DeviceInput>(self))
            {
                ref readonly var axes = ref world.GetComponentRO<DeviceInput>(self).Control.Axes;
                yaw = axes[DroneStationChannels.AxisYaw]; vertical = axes[DroneStationChannels.AxisThrottle];
                roll = axes[DroneStationChannels.AxisRoll]; pitch = axes[DroneStationChannels.AxisPitch];
            }
            bool centred = MathF.Abs(yaw) < StickCentred && MathF.Abs(vertical) < StickCentred && MathF.Abs(roll) < StickCentred && MathF.Abs(pitch) < StickCentred;
            st.Mode = ModeOf(in st, linked, in link, centred);

            // ⑤ the command, on live frames only (a pause stops the world; the queued press goes out on the first live frame)
            if (live)
            {
                bool sticks = st.Power != 0;
                WriteCommand(world, self, new RemoteControl
                {
                    Mode = st.Command, Seq = st.Seq, CameraPitch = st.CameraPitch,
                    Forward = sticks ? pitch : 0f, Lateral = sticks ? -roll : 0f, Yaw = sticks ? -yaw : 0f, Vertical = sticks ? vertical : 0f,
                });
            }

            // ⑥ the view: a powered station films through its drone at the gimbal (in pause too — CAM DOWN tilts the paused picture, panel 15)
            DeviceViews.Write(world, self, new DeviceView
            {
                Entity = st.Power != 0 && !drone.IsNull && world.IsAlive(drone) ? drone : Entity.Null, Mode = DeviceViewMode.Eyes, Pitch = st.CameraPitch,
            });
            DeviceLooks.Follow(world, self, 0f);   // where it points (no optic)
            return NodeStatus.Running;
        }

        /// <summary>The drone this station flies: its own <see cref="ControlledEntities"/> slot 0 (D-147), or null.</summary>
        public static Entity DroneOf(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<ControlledEntities>() && world.HasComponent<ControlledEntities>(self)
                ? world.GetComponentRO<ControlledEntities>(self).Resolve(world, 0) : Entity.Null;

        /// <summary>One accepted mode press (D-110): ARM toggles on the ground, TAKEOFF flies an armed drone, LAND and RTH any time in flight. A new command bumps the seq.</summary>
        private static void Apply(ref DroneStationState st, int button, bool linked, PilotPhase phase)
        {
            var before = st.Command;
            switch (button)
            {
                case DroneStationChannels.Arm:
                    if (st.Command == RemoteMode.Idle && linked && phase == PilotPhase.Grounded) st.Command = RemoteMode.Armed;
                    else if (st.Command == RemoteMode.Armed) st.Command = RemoteMode.Idle;
                    break;
                case DroneStationChannels.TakeOff:
                    if (st.Command is RemoteMode.Armed or RemoteMode.Land or RemoteMode.ReturnHome) st.Command = RemoteMode.Fly;
                    break;
                case DroneStationChannels.Land:
                    if (st.Command is RemoteMode.Fly or RemoteMode.ReturnHome) st.Command = RemoteMode.Land;
                    break;
                case DroneStationChannels.Rth:
                    if (st.Command is RemoteMode.Fly or RemoteMode.Land) st.Command = RemoteMode.ReturnHome;
                    break;
            }
            if (st.Command != before) unchecked { st.Seq++; }
        }

        /// <summary>What the station shows: its command joined to what the drone reports it is doing (the telemetry, D-106 as built).</summary>
        private static DroneStationMode ModeOf(in DroneStationState st, bool linked, in DroneLink link, bool centred)
        {
            if (st.Power == 0) return DroneStationMode.Off;
            bool flying = st.Command is RemoteMode.Fly or RemoteMode.Land or RemoteMode.ReturnHome;
            if (!linked) return flying && link.Phase != PilotPhase.Grounded ? DroneStationMode.Failsafe : DroneStationMode.NoLink;
            switch (link.Phase)
            {
                case PilotPhase.FailsafeHover: case PilotPhase.FailsafeReturn: case PilotPhase.FailsafeLand: return DroneStationMode.Failsafe;
                case PilotPhase.TakeOff: return DroneStationMode.TakeOff;
                case PilotPhase.Manual: return centred ? DroneStationMode.Hover : DroneStationMode.Flying;
                case PilotPhase.Land: case PilotPhase.BatteryDown: return DroneStationMode.Landing;
                case PilotPhase.ReturnHome: return DroneStationMode.Returning;
            }
            return st.Command switch   // on the ground
            {
                RemoteMode.Idle or RemoteMode.Off => DroneStationMode.Linked,
                RemoteMode.Armed => DroneStationMode.Armed,
                RemoteMode.Fly when link.AckSeq != st.Seq => DroneStationMode.TakeOff,   // sent, not yet flown
                _ => DroneStationMode.Landed,
            };
        }

        private static void WriteCommand(EntityRepository world, Entity self, in RemoteControl next)
        {
            if (!world.IsComponentTypeRegistered<RemoteControl>()) return;
            if (world.HasComponent<RemoteControl>(self))
            {
                ref readonly var now = ref world.GetComponentRO<RemoteControl>(self);
                if (now.Mode == next.Mode && now.Seq == next.Seq && now.CameraPitch == next.CameraPitch && now.Forward == next.Forward
                    && now.Lateral == next.Lateral && now.Yaw == next.Yaw && now.Vertical == next.Vertical) return;
                world.SetComponent(self, next);
            }
            else world.AddComponent(self, next);   // added WITH its value (the parallel rule)
        }
    }
}
