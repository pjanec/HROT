using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior.Devices;

namespace Hrot.AI.Behaviors.Devices.DroneStation
{
    /// <summary>⭐ SD-S2c (§3.13c D-110) — what the ground station shows the operator: its own mode, from its command and the drone's telemetry.</summary>
    public enum DroneStationMode : byte
    {
        Off = 0,
        /// <summary>On, but the drone does not answer (out of range, jammed, none paired).</summary>
        NoLink = 1,
        /// <summary>On, the drone answers, not armed.</summary>
        Linked = 2,
        Armed = 3,
        /// <summary>The drone climbs to its hover height.</summary>
        TakeOff = 4,
        /// <summary>The sticks fly it.</summary>
        Flying = 5,
        /// <summary>Manual, the sticks centred: it holds its hover.</summary>
        Hover = 6,
        Landing = 7,
        Returning = 8,
        /// <summary>It is down after a landing (armed: TAKEOFF flies again).</summary>
        Landed = 9,
        /// <summary>⭐ D-107 — the link is lost in flight (or the drone flies its failsafe).</summary>
        Failsafe = 10,
    }

    /// <summary>
    /// ⭐⭐ SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-110) — the GROUND STATION's state, a <c>[UnitMemory]</c> struct on the DEVICE:
    /// recorded, read by the scenario's <c>memory: DroneStationState</c> cue and (SD-S2d) the OSD. Written ONLY by <see cref="DroneStationBehavior.Tick"/>.
    /// </summary>
    [Fdp.Core.UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public struct DroneStationState
    {
        /// <summary>What the station shows.</summary>
        public DroneStationMode Mode;
        /// <summary>1 while powered.</summary>
        public byte Power;
        /// <summary>The command it sends (<c>RemoteControl.Mode</c>).</summary>
        public RemoteMode Command;
        /// <summary>⭐ R-4300 — the MODE presses (ARM · TAKEOFF · LAND · RTH, <see cref="DroneStationChannels"/> bits) made in a pause, run on the first live frame.</summary>
        public byte Queued;
        /// <summary>The command number (<c>RemoteControl.Seq</c>), bumped per accepted mode press.</summary>
        public uint Seq;
        /// <summary>The gimbal's pitch (radians, 0 … −π/2) — CAM UP / DOWN.</summary>
        public float CameraPitch;
        /// <summary>The drone it flies (its <c>ControlledEntities</c> slot 0) — this node's handle.</summary>
        public Fdp.Core.Entity Drone;
        /// <summary>The press counters last seen (the one tracker, <c>DevicePressReader</c>).</summary>
        public DevicePressTracker Presses;
    }

    /// <summary>
    /// The ground station's elements (the BUTTON and AXIS indexes of TKB type 642, D-142; pinned by <c>DroneStationTests</c>): the numbers are the wire, the
    /// names are the type's data.
    /// </summary>
    public static class DroneStationChannels
    {
        public const int Count = 7;
        public const int Power = 0, Arm = 1, TakeOff = 2, Land = 3, Rth = 4, CamUp = 5, CamDown = 6;
        /// <summary>The Mode 2 axes: the left stick X / up, the right stick X / up.</summary>
        public const int AxisYaw = 0, AxisThrottle = 1, AxisRoll = 2, AxisPitch = 3;
        /// <summary>The <see cref="DroneStationState.Queued"/> bit of a mode button.</summary>
        public static byte Bit(int button) => (byte)(1 << (button - Arm));
    }
}
