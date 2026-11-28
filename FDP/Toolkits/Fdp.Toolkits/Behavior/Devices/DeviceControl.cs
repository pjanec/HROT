using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1b-2 / ⭐⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.4, §3.13e D-138) — a device's MERGED input, the
    /// state its logic reads (in <see cref="DeviceInput"/>): four analog axes, sixteen PRESS COUNTERS — ⭐ the DEVICE's own (D-138: every source's
    /// <c>ButtonDown</c> bumps them, so presses from any number of sources add up and the one press reader compares them to the last it saw) — the HELD
    /// level of each button, eight latching SWITCH positions, the tracker pose (present-flags) and whether a view shows the device (<see cref="Viewed"/>).
    /// ⛔ SD-S5v retired the whole-state <c>DeviceFrameEvent</c> that carried this struct: the sources now send SPARSE <see cref="DeviceInputEvent"/>s the
    /// device merges (<see cref="DeviceInputMerge"/>). ⚠ Its own component, never inside the body's <c>OperatorInput</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceControl
    {
        public DeviceAxes Axes;
        public DevicePresses Presses;
        /// <summary>⭐ D-143 — bit i: button i is held down now (set by <c>ButtonDown</c>, cleared by <c>ButtonUp</c>, healed by <c>HeldSnapshot</c>).</summary>
        public ushort Held;
        /// <summary>⭐ D-135 — the latching switch positions (switch i of the type's descriptor).</summary>
        public DeviceSwitches Switches;
        public Quaternion Orientation;
        public Vector3 Position;
        /// <summary>Bit 0: <see cref="Orientation"/> present (a tracker, or the station's look); bit 1: <see cref="Position"/> present.</summary>
        public byte Trackers;
        /// <summary>1 while a view of the operating station shows THIS device (the body's brain raises the held binoculars while its binoculars device is viewed).</summary>
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

    /// <summary>Sixteen press counters, one per BUTTON element (the device type's descriptor names them, D-142).</summary>
    [InlineArray(Count)]
    public struct DevicePresses
    {
        public const int Count = 16;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private byte _element;
#pragma warning restore IDE0044
    }

    /// <summary>⭐ SD-S5v (D-135) — eight latching switch positions, one per SWITCH element.</summary>
    [InlineArray(Count)]
    public struct DeviceSwitches
    {
        public const int Count = 8;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private byte _element;
#pragma warning restore IDE0044
    }
}
