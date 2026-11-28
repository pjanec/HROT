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

    /// <summary>
    /// ⭐⭐ S1d-3 — what a device keeps to tell a PRESS (a <see cref="DevicePresses"/> counter that moved): the operator epoch the counters are based on
    /// (R7-11) and the counters last seen. ⭐ ONE reader, <see cref="DevicePressReader"/>, for every device type — a field in each type's
    /// <c>[UnitMemory]</c> state.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DevicePressTracker
    {
        public uint Epoch;
        /// <summary>1 when <see cref="Seen"/> is based on the present input; 0 ⇒ the next input RE-BASES (no press fires).</summary>
        public byte Based;
        public DevicePresses Seen;
    }

    /// <summary>⭐ S1d-3 — THE press reader of every device (extracted from the S1c rangefinder): the presses since the last tick, per channel.</summary>
    public static class DevicePressReader
    {
        /// <summary>Writes into <paramref name="moved"/>[i] how often channel i was pressed since the last call; re-bases (no press) on a new operator
        /// epoch (<c>PossessedBy.Epoch</c>, R7-11) or on input appearing after none, so a re-read frame never presses twice. Reads only <paramref name="self"/>.</summary>
        public static void Read(Fdp.Core.EntityRepository w, Fdp.Core.Entity self, ref DevicePressTracker t, System.Span<int> moved)
        {
            moved.Clear();
            uint epoch = w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>() && w.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(self)
                         && w.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(self).Mode == Fdp.Toolkit.Behavior.Possession.PossessionMode.Operated
                ? w.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(self).Epoch : 0u;
            if (!w.IsComponentTypeRegistered<DeviceInput>() || !w.HasComponent<DeviceInput>(self)) { t.Based = 0; t.Epoch = epoch; return; }
            ref readonly var presses = ref w.GetComponentRO<DeviceInput>(self).Control.Presses;
            bool diff = t.Based != 0 && t.Epoch == epoch;
            int n = System.Math.Min(moved.Length, DevicePresses.Count);
            for (int i = 0; i < n; i++) if (diff) moved[i] = (byte)(presses[i] - t.Seen[i]);
            for (int i = 0; i < DevicePresses.Count; i++) t.Seen[i] = presses[i];
            t.Based = 1; t.Epoch = epoch;
        }
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
