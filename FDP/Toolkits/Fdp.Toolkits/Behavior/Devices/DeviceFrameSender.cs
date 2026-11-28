using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1e-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.4 D-43 / R5-13, §5a <c>do: control</c>) — THE ONE writer of a
    /// station's device frames, extracted from the S1d-3 station so every ADAPTER fills <see cref="DeviceFrameEvent"/> the same way: the keyboard / mouse
    /// station (<c>PossessionStation</c>), a scenario's scripted station (<c>do: control</c>), later a gamepad or a replica (SD-S2 / SD-S5).
    /// <para>Each call sends ONE frame per device the station operates (D-43 — one frame per operated entity, targeted at it): ① its <c>Orientation</c> = the
    /// station's LOOK relative to the device's mount (R5-13, the mouse mode: the device writes its own <c>DevicePose</c> from it); ② <c>Viewed</c> while the
    /// view is bound to it (D-63); ③ to the BOUND device only, this frame's button presses (its press counters move — D-38's device scope) and its axes. The
    /// counters live here, per device, so a re-read frame never presses twice and a new device starts from 0 (its operator epoch re-bases them, R7-11).
    /// ⚠ Published on the local bus — the interaction transport carries no device frame yet (SD-C). 0 B.</para>
    /// </summary>
    public sealed class DeviceFrameSender
    {
        private struct Slot
        {
            public Entity Device;
            public uint Seq;
            public DeviceControl Control;
            public DeviceControl LastSent;
            public bool Sent;
        }

        private readonly Slot[] _slots;

        public DeviceFrameSender(int maxDevices = 8, bool onlyOnChange = false)
        {
            _slots = new Slot[Math.Max(1, maxDevices)];
            OnlyOnChange = onlyOnChange;
        }

        /// <summary>⭐ S1e-3 — send a device's frame only when its control CHANGED (and its first one always). The keyboard station sends every input frame;
        /// a scripted station sends on change, so the device's input (its <c>Seq</c>, recorded) does not depend on how many frames a sim instant had — 📌
        /// dv-replay-hud's H5 review: a frame-paced <c>DeviceInput.Seq</c> made the run after a review differ from the straight run.</summary>
        public bool OnlyOnChange { get; }

        /// <summary>Frames sent (rails).</summary>
        public long Sent { get; private set; }

        /// <summary>The look as the station holds it: yaw about +Z, then pitch (up +) — forward is +X.</summary>
        public static Quaternion Look(float yaw, float pitch)
            => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pitch);

        /// <summary>
        /// Sends one frame to each of <paramref name="devices"/> from <paramref name="station"/>. <paramref name="presses"/>[i] presses channel i of the
        /// <paramref name="bound"/> device that many times (a counter moves); <paramref name="axes"/> (when given) are the bound device's axes this frame.
        /// <paramref name="viewing"/> false ⇒ no view shows any device (the 2-D map is up). Nothing when the event is not registered.
        /// </summary>
        public void Send(EntityRepository w, StationId station, ReadOnlySpan<Entity> devices, Entity bound, bool viewing, Quaternion look,
                         ReadOnlySpan<byte> presses, ReadOnlySpan<float> axes)
        {
            if (!w.Bus.IsRegistered<DeviceFrameEvent>()) return;
            for (int i = 0; i < devices.Length; i++)
            {
                var d = devices[i];
                ref var slot = ref SlotOf(d, devices);
                bool isBound = viewing && d == bound;
                Quaternion mount = w.HasComponent<SimTransform>(d) ? (Quaternion)w.GetComponentRO<SimTransform>(d).Rotation : Quaternion.Identity;
                slot.Control.Orientation = Quaternion.Normalize(Quaternion.Inverse(mount == default ? Quaternion.Identity : mount) * look);
                slot.Control.Trackers |= DeviceControl.OrientationPresent;
                slot.Control.Viewed = isBound ? (byte)1 : (byte)0;
                if (isBound)
                {
                    int n = Math.Min(presses.Length, DevicePresses.Count);
                    for (int b = 0; b < n; b++) unchecked { slot.Control.Presses[b] += presses[b]; }
                    int a = Math.Min(axes.Length, DeviceAxes.Count);
                    for (int k = 0; k < a; k++) slot.Control.Axes[k] = Math.Clamp(axes[k], -1f, 1f);
                }
                if (OnlyOnChange && slot.Sent && Same(in slot.Control, in slot.LastSent)) continue;
                w.Bus.Publish(new DeviceFrameEvent { Device = d, StationNodeId = station, Seq = ++slot.Seq, Control = slot.Control });
                slot.LastSent = slot.Control;
                slot.Sent = true;
                Sent++;
            }
        }

        private static bool Same(in DeviceControl a, in DeviceControl b)
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<DeviceControl>(in a))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<DeviceControl>(in b)));

        private ref Slot SlotOf(Entity device, ReadOnlySpan<Entity> devices)
        {
            int free = -1;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Device == device) return ref _slots[i];
                if (free < 0 && (_slots[i].Device.IsNull || devices.IndexOf(_slots[i].Device) < 0)) free = i;
            }
            if (free < 0) free = 0;
            _slots[free] = new Slot { Device = device };   // a new device: counters from 0 (its own epoch re-bases them, R7-11)
            return ref _slots[free];
        }
    }
}
