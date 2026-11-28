using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐⭐ SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-130 / D-131) — THE PANEL IO SEAM: one input source of a device at a station — the
    /// station's virtual panel (the HUD's clickable chips, the mouse, the keys, the pad), a physical replica, a tracker, a scenario's scripted trainee. Every
    /// one writes the device through its OWN <see cref="Encoder"/> (sparse events of its <see cref="Source"/> kind, merged by the device — D-138) and nothing
    /// else: ⛔ no HUD code, no device logic and no device state is ever called from an input (D-130), so a replica replaces the virtual adapter by a binding
    /// change only (<see cref="DevicePanelIoBinding"/>).
    /// </summary>
    public interface IDevicePanelIo
    {
        /// <summary>The source kind its events carry (its priority, D-145).</summary>
        DeviceInputSource Source { get; }

        /// <summary>Its encoder — what it stages; the station flushes it each frame.</summary>
        DeviceInputEncoder Encoder { get; }

        /// <summary>⭐ D-126 — it owns the device's orientation (a tracker): the virtual panel's RMB-look and arrows then do nothing for that device.</summary>
        bool OwnsLook => false;

        /// <summary>⭐ D-135 — it owns the device's LATCHING controls (a replica's physical switches): the virtual panel's chip of a switch is then not clickable.</summary>
        bool OwnsLatching => false;
    }

    /// <summary>
    /// ⭐⭐ SD-S5v (D-131 / D-141) — WHICH sources are live for each device at a station: the PRIMARY adapter (an attached replica / tracker / test adapter, else
    /// the station's virtual panel) and the virtual panel's presses (its HUD clicks and hotkeys) always. ⭐ A POLICY, never a composer: the device merges every
    /// source's events itself (D-138). Station-local, main thread; 0 B per frame (a fixed table).
    /// </summary>
    public sealed class DevicePanelIoBinding
    {
        private readonly Entity[] _devices;
        private readonly IDevicePanelIo?[] _adapters;

        public DevicePanelIoBinding(int capacity = 8)
        {
            _devices = new Entity[Math.Max(1, capacity)];
            _adapters = new IDevicePanelIo?[_devices.Length];
        }

        /// <summary>Attaches <paramref name="adapter"/> as <paramref name="device"/>'s PRIMARY source (a replica, a tracker — the virtual panel steps back to presses).</summary>
        public void Attach(Entity device, IDevicePanelIo adapter)
        {
            int free = -1;
            for (int i = 0; i < _devices.Length; i++)
            {
                if (_devices[i] == device) { _adapters[i] = adapter; return; }
                if (free < 0 && _adapters[i] == null) free = i;
            }
            if (free < 0) throw new InvalidOperationException("the station's panel IO binding is full");
            _devices[free] = device;
            _adapters[free] = adapter;
        }

        /// <summary>Detaches the device's attached adapter (the virtual panel is primary again).</summary>
        public void Detach(Entity device)
        {
            for (int i = 0; i < _devices.Length; i++)
                if (_devices[i] == device) { _devices[i] = Entity.Null; _adapters[i] = null; }
        }

        /// <summary>The attached primary adapter of <paramref name="device"/>, or null (the virtual panel is primary).</summary>
        public IDevicePanelIo? Attached(Entity device)
        {
            for (int i = 0; i < _devices.Length; i++) if (_devices[i] == device && _adapters[i] != null) return _adapters[i];
            return null;
        }

        /// <summary>Is the virtual panel the device's primary source (its look and axes are the station's)?</summary>
        public bool VirtualIsPrimary(Entity device) => Attached(device) is null;

        /// <summary>⭐ D-126 — another source owns the device's look.</summary>
        public bool LookOwnedElsewhere(Entity device) => Attached(device) is { OwnsLook: true };

        /// <summary>⭐ D-135 — another source owns the device's latching controls.</summary>
        public bool LatchingOwnedElsewhere(Entity device) => Attached(device) is { OwnsLatching: true };

        /// <summary>Flushes every attached adapter's encoder for the devices the station operates.</summary>
        public void Flush(EntityRepository w, StationId station, ReadOnlySpan<Entity> devices)
        {
            for (int i = 0; i < _adapters.Length; i++)
            {
                var a = _adapters[i];
                if (a == null) continue;
                bool seen = false;
                for (int k = 0; k < i; k++) if (ReferenceEquals(_adapters[k], a)) seen = true;   // one adapter on two devices flushes once
                if (!seen) a.Encoder.Flush(w, station, devices);
            }
        }
    }

    /// <summary>⭐ SD-S5v — a device's TKB type: the ONE lookup of its descriptor (<see cref="DeviceInfo.DeviceType"/> in the world's <see cref="ITkbDatabase"/>).</summary>
    public static class DeviceTypes
    {
        /// <summary>The device's <see cref="DeviceDefDto"/>, or null (no type, no database).</summary>
        public static DeviceDefDto? Of(EntityRepository w, Entity device) => TemplateOf(w, device)?.GetDescriptor<DeviceDefDto>();

        /// <summary>The device's TKB template, or null.</summary>
        public static TkbTemplate? TemplateOf(EntityRepository w, Entity device)
        {
            if (device.IsNull || !w.IsAlive(device) || !w.IsComponentTypeRegistered<DeviceInfo>() || !w.HasComponent<DeviceInfo>(device) || !w.HasSingletonManaged<ITkbDatabase>()) return null;
            var db = w.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(w.GetComponentRO<DeviceInfo>(device).DeviceType, out var t) ? t : null;
        }
    }

    /// <summary>
    /// ⭐⭐⭐ SD-S5v (D-122 / D-123 / D-125 / D-135) — THE CLICK of a HUD button, shared by every adapter that clicks (the station's virtual panel on LMB, the
    /// scripted trainee's <c>do: click</c>): the point is hit-tested against the view's kept HUD run (<see cref="PanelButtons.Hit"/>, canvas coordinates) and the
    /// button's ELEMENT is pressed through the adapter's encoder — a button pressed, a switch stepped to its next position — exactly the bytes a hotkey, a pad
    /// button, a scenario's <c>press</c> or a replica's button make (one counter, D-125). ⛔ Nothing else: the HUD is never a path into the device (D-130).
    /// </summary>
    public static class DevicePanelClick
    {
        /// <summary>Clicks <paramref name="canvas"/> on <paramref name="view"/>: the index of the PanelButton hit (−1: none — LMB on no button does nothing). A
        /// latching chip while another source owns the latching controls is hit but REFUSED (<paramref name="refused"/>, D-135).</summary>
        public static int Click(EntityRepository w, DeviceInputEncoder encoder, Entity device, DeviceDefDto? type, ReadOnlySpan<DebugPrimitive> view, Vector2 canvas,
                                bool latchingOwnedElsewhere, out bool refused)
        {
            refused = false;
            int hit = PanelButtons.Hit(view, canvas);
            if (hit < 0) return -1;
            var kind = (DeviceElementKind)view[hit].PanelKind;
            if (kind == DeviceElementKind.Switch && latchingOwnedElsewhere) { refused = true; return hit; }
            Press(w, encoder, device, type, kind, view[hit].PanelElement);
            return hit;
        }

        /// <summary>Presses element (<paramref name="kind"/>, <paramref name="index"/>) through <paramref name="encoder"/>: a button once, a switch one step on from
        /// the position the DEVICE holds now (its merged input — another source may have moved it), wrapping at the type's positions.</summary>
        public static void Press(EntityRepository w, DeviceInputEncoder encoder, Entity device, DeviceDefDto? type, DeviceElementKind kind, int index)
        {
            switch (kind)
            {
                case DeviceElementKind.Button: encoder.Press(device, index); break;
                case DeviceElementKind.Switch:
                    int now = w.IsComponentTypeRegistered<DeviceInput>() && w.HasComponent<DeviceInput>(device) && (uint)index < DeviceSwitches.Count
                        ? w.GetComponentRO<DeviceInput>(device).Control.Switches[index] : 0;
                    int positions = Math.Max(1, type?.ElementAt(kind, index)?.Positions ?? 2);
                    encoder.SetSwitch(device, index, (now + 1) % positions);
                    break;
            }
        }
    }
}
