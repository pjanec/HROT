using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1b-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.4) — THE device section of a control frame, ONE definition
    /// carried by both <c>OperatorControlFrameEvent</c> (what a station sends) and <c>OperatorInput</c> (what the target keeps): the generic I/O
    /// model's inputs — four analog axes, sixteen PRESS COUNTERS (a press = its counter moved; the device compares to the last it saw, so a re-read
    /// frame never presses twice), the tracker pose (present-flags), the body offset and whether a view shows the device (<see cref="Viewed"/>).
    /// ⚠ AS BUILT: carried by its OWN event (<see cref="DeviceFrameEvent"/>) into its OWN component (<see cref="DeviceInput"/>) — ⛔ never inside the body's
    /// <c>OperatorControlFrameEvent</c> / <c>OperatorInput</c>: their recorded layout hashes are pinned so earlier recordings replay (CE-11027,
    /// <c>PossessionReplicationTests</c>), and adding a field changes them. Local until SD-C (the interaction transport carries no device frame yet).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceControl
    {
        public DeviceAxes Axes;
        public DevicePresses Presses;
        public Quaternion Orientation;
        public Vector3 Position;
        public Vector3 BodyOffset;
        /// <summary>Bit 0: <see cref="Orientation"/> present (a tracker); bit 1: <see cref="Position"/> present.</summary>
        public byte Trackers;
        /// <summary>1 while a view of the sending station shows THIS device (the body's brain raises the held binoculars while its binoculars device is viewed).</summary>
        public byte Viewed;

        public const byte OrientationPresent = 1, PositionPresent = 2;
    }

    /// <summary>Four analog axes ∈ [−1, 1] (a gamepad stick pair, a slider, a wheel).</summary>
    [InlineArray(Count)]
    public struct DeviceAxes
    {
        public const int Count = 4;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private float _element;
#pragma warning restore IDE0044
    }

    /// <summary>Sixteen press counters, one per button channel (the device type names the channels).</summary>
    [InlineArray(Count)]
    public struct DevicePresses
    {
        public const int Count = 16;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private byte _element;
#pragma warning restore IDE0044
    }
}

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>⭐ S1b-2 — a station's frame for ONE device it operates; the device tick ingests it on the device's brain owner (newest <see cref="Seq"/>).</summary>
    [Fdp.Core.EventId(Fdp.Toolkit.Behavior.Possession.PossessionEventIds.DeviceFrame)]
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceFrameEvent
    {
        public Fdp.Core.Entity Device;
        public Fdp.Core.StationId StationNodeId;
        public uint Seq;
        public DeviceControl Control;
    }

    /// <summary>⭐ S1b-2 — the newest frame a device took from its operator's station: what its logic reads (its OWN entity — the parallel rule).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [Fdp.Core.ComponentId(Fdp.Core.GlobalComponentIds.DeviceInput)]
    [Fdp.Core.DataPolicy(Fdp.Core.DataPolicy.NoScenario)]
    public struct DeviceInput
    {
        public uint Seq;
        public DeviceControl Control;
    }
}
