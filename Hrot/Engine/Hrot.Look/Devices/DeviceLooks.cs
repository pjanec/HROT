using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Look.Devices;

/// <summary>⭐ SD-S5d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10a D-163) — what a device looks like, one block shape per kind.</summary>
public enum DeviceKind : byte { Box = 0, Binoculars = 1, Rangefinder = 2, Designator = 3, DroneController = 4, Console = 5 }

/// <summary>
/// ⭐⭐ SD-S5d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10a D-163 / D-164; mockup <c>designs/devices/mockups/device-looks-visual.svg</c>) — THE ONE
/// device-look rule, in the shared look library so the 3-D map and the external IG draw the same device (R-4296, the weapon kinds' pattern, CE-4671): the
/// KIND of a device type (TKB data, <see cref="DeviceDefDto.Look"/>, defaulted by its behaviour — ⛔ never from a name), its size and colour, whether it is
/// raised to the eyes, and which device a man has RAISED. The place is never decided here: <see cref="DevicePoses.TryWorld"/> (D-166).
/// <para>0 B per steady frame: a type's kind is resolved once (a per-thread cache); the raised-device walk reads a fixed buffer.</para>
/// </summary>
public static class DeviceLooks
{
    /// <summary>The kind <paramref name="device"/> says (<see cref="DeviceDefDto.Look"/>), else its behaviour's default, else <see cref="DeviceKind.Box"/>.</summary>
    public static DeviceKind KindOf(DeviceDefDto? device)
    {
        if (device == null) return DeviceKind.Box;
        if (!string.IsNullOrWhiteSpace(device.Look) && Enum.TryParse<DeviceKind>(device.Look.Trim(), ignoreCase: true, out var stated) && Enum.IsDefined(stated)) return stated;
        return device.Behavior switch
        {
            Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceRangefinder => DeviceKind.Rangefinder,
            Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceBinoculars => DeviceKind.Binoculars,
            _ => DeviceKind.Box,
        };
    }

    /// <summary>The kind of <paramref name="t"/> (a device type: its <c>Gen.Device</c>); <see cref="DeviceKind.Box"/> for anything else.</summary>
    public static DeviceKind KindOf(TkbTemplate? t) => KindOf(t?.GetDescriptor<DeviceDefDto>());

    [ThreadStatic] private static Dictionary<long, DeviceKind>? t_kinds;
    [ThreadStatic] private static ITkbDatabase? t_kindsOf;

    /// <summary>The kind of device type <paramref name="type"/> in <paramref name="tkb"/>, resolved once per type (0 B after).</summary>
    public static DeviceKind KindOf(ITkbDatabase? tkb, long type)
    {
        if (tkb == null) return DeviceKind.Box;
        if (!ReferenceEquals(tkb, t_kindsOf) || t_kinds == null) { t_kinds = new Dictionary<long, DeviceKind>(); t_kindsOf = tkb; }
        if (t_kinds.TryGetValue(type, out var k)) return k;
        k = KindOf(tkb.GetByType(type));
        t_kinds[type] = k;
        return k;
    }

    /// <summary>Is <paramref name="t"/> a device type?</summary>
    public static bool IsDevice(TkbTemplate? t) => t?.GetDescriptor<DeviceDefDto>() != null;

    /// <summary>The kind's block size, metres: length along its view (x), width (y), height (z).</summary>
    public static Vector3 SizeOf(DeviceKind kind) => kind switch
    {
        DeviceKind.Binoculars => new(0.14f, 0.17f, 0.07f),
        DeviceKind.Rangefinder => new(0.20f, 0.18f, 0.10f),
        DeviceKind.Designator => new(0.40f, 0.16f, 0.18f),
        DeviceKind.DroneController => new(0.05f, 0.30f, 0.20f),
        DeviceKind.Console => new(0.12f, 0.35f, 0.25f),
        _ => new(0.30f, 0.20f, 0.15f),
    };

    /// <summary>The kind's body colour: binoculars near-black, a rangefinder / a box olive, a designator tan, a controller / a console dark grey.</summary>
    public static Rgba ColourOf(DeviceKind kind) => kind switch
    {
        DeviceKind.Binoculars => new Rgba((byte)42, (byte)44, (byte)42, (byte)255),
        DeviceKind.Designator => new Rgba((byte)183, (byte)156, (byte)112, (byte)255),
        DeviceKind.DroneController => new Rgba((byte)74, (byte)78, (byte)85, (byte)255),
        DeviceKind.Console => new Rgba((byte)42, (byte)44, (byte)46, (byte)255),
        _ => new Rgba((byte)94, (byte)102, (byte)70, (byte)255),   // Rangefinder, Box: olive
    };

    /// <summary>Is a raised device of this kind held to the EYES (an optic), or in front of the chest (a controller, a console, a box)?</summary>
    public static bool AtTheEyes(DeviceKind kind) => kind is DeviceKind.Binoculars or DeviceKind.Rangefinder or DeviceKind.Designator;

    /// <summary>
    /// D-164 ① — the device <paramref name="man"/> has RAISED: among the devices in his own carry seats (his <see cref="PassengerBuffer"/>), the one a view
    /// shows (its frame's <c>Viewed</c>), else the first he carries; false when he carries none (his own pocket binoculars, a held item with no device).
    /// ⚠ The same walk as <c>CarriedDevices.Find</c> (which answers only yes / no): the device code is fenced for this slice — a renderer needs the DEVICE, so
    /// <c>CarriedDevices</c> should grow a <c>Viewed(w, man) → Entity</c> both call (a tracker row).
    /// </summary>
    public static bool RaisedBy(EntityRepository w, Entity man, out Entity device)
    {
        device = Entity.Null;
        if (man.IsNull || !w.IsAlive(man) || !w.IsComponentTypeRegistered<DeviceInfo>() || !w.IsComponentTypeRegistered<PassengerBuffer>()
            || !w.HasComponent<PassengerBuffer>(man)) return false;
        bool inputs = w.IsComponentTypeRegistered<DeviceInput>();
        ref readonly var seats = ref w.GetComponentRO<PassengerBuffer>(man);
        for (int i = 0; i < PassengerBuffer.Capacity; i++)
        {
            var item = seats.Passengers[i];
            if (item.IsNull || !w.IsAlive(item) || !w.HasComponent<DeviceInfo>(item)) continue;
            if (inputs && w.HasComponent<DeviceInput>(item) && w.GetComponentRO<DeviceInput>(item).Control.Viewed != 0) { device = item; return true; }
            if (device.IsNull) device = item;
        }
        return !device.IsNull;
    }

    /// <summary>The kind of the device <paramref name="man"/> has raised (<see cref="RaisedBy"/>); <see cref="DeviceKind.Binoculars"/> when he carries none.</summary>
    public static DeviceKind RaisedKind(EntityRepository w, Entity man, ITkbDatabase? tkb)
    {
        if (!RaisedBy(w, man, out var device) || !w.IsComponentTypeRegistered<TkbIdentity>() || !w.HasComponent<TkbIdentity>(device)) return DeviceKind.Binoculars;
        return KindOf(tkb, (long)w.GetComponentRO<TkbIdentity>(device).TkbType);
    }

    /// <summary>D-164 / D-64 — what holds <paramref name="device"/> (a man's carry seat, a vehicle's station, a host device), or <see cref="Entity.Null"/> when it
    /// is in no station. A device a PERSON holds is drawn only in his hands while raised; the renderer's own family rule says who is a person.</summary>
    public static Entity HolderOf(EntityRepository w, Entity device)
    {
        if (!w.IsComponentTypeRegistered<IsEmbarkedTag>() || !w.HasComponent<IsEmbarkedTag>(device)) return Entity.Null;
        var holder = w.GetComponentRO<IsEmbarkedTag>(device).VehicleEntity;
        return !holder.IsNull && w.IsAlive(holder) ? holder : Entity.Null;
    }
}
