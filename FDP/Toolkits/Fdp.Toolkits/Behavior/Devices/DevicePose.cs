using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐ Simulated devices S1b-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1, D-48) — where a device POINTS relative to where it is
    /// carried or mounted: written ONLY by the device's own logic (from the mouse look, a gamepad, a tracker — its <see cref="DeviceInput"/>);
    /// unmanaged, recorded. Identity = it points as its mount does.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DevicePose)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DevicePose
    {
        /// <summary>Relative to the mount's frame (identity = the mount's own heading).</summary>
        public Quaternion Orientation;
        /// <summary>Metres from the mount point, in the mount's frame.</summary>
        public Vector3 Offset;
        /// <summary>⭐ S1d-3 — the device optic's VERTICAL field of view (degrees) a view bound to it shows (<see cref="DeviceOptics"/>);
        /// 0 = no optic of its own (the view keeps its field of view). Written by the device from its state (a zoom step, a fixed magnification).</summary>
        public float FovDegrees;
    }

    /// <summary>
    /// ⭐⭐ S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 "S1d-3 AS BUILT") — THE ONE optic rule: a device of magnification m shows a
    /// true field of view of <see cref="ApparentFieldDegrees"/> / m (an eyepiece's apparent field over its magnification — 7× ⇒ 7.1°, as a 7×50
    /// binocular), written into its <see cref="DevicePose.FovDegrees"/>; a view bound to the device reads it back with <see cref="FovOf"/>.
    /// </summary>
    public static class DeviceOptics
    {
        /// <summary>The eyepiece's apparent field (degrees) — the common 50° of a field binocular or a rangefinder.</summary>
        public const float ApparentFieldDegrees = 50f;

        /// <summary>The true field (degrees) at <paramref name="magnification"/>; 0 (no optic) at or below 1×.</summary>
        public static float FovDegrees(float magnification) => magnification > 1f ? ApparentFieldDegrees / magnification : 0f;

        /// <summary>The field of view a view bound to <paramref name="device"/> shows (degrees), or 0 when it has no optic.</summary>
        public static float FovOf(EntityRepository w, Entity device)
            => !device.IsNull && w.IsAlive(device) && w.IsComponentTypeRegistered<DevicePose>() && w.HasComponent<DevicePose>(device)
                ? w.GetComponentRO<DevicePose>(device).FovDegrees : 0f;
    }

    /// <summary>
    /// ⭐⭐ S1d-3 (R5-13, D-48) — a device POINTS WHERE ITS OPERATOR LOOKS: its own <see cref="DevicePose"/> from its own <see cref="DeviceInput"/> —
    /// the frame's <see cref="DeviceControl.Orientation"/> when present (a tracker, or the station's look in mouse mode — relative to the device's
    /// mount), else identity (it points as its mount does) — with the optic's field of view. Self only, written on change (the parallel rule;
    /// replicated on change). ⭐ Every device behaviour calls it once a tick (the rangefinder, the binoculars).
    /// </summary>
    public static class DeviceLooks
    {
        public static void Follow(EntityRepository w, Entity self, float fovDegrees)
        {
            if (!w.IsComponentTypeRegistered<DevicePose>()) return;
            var orientation = Quaternion.Identity;
            // ⭐ SD-S5b (D-56 / D-120) — a STACKED device ignores its own look and tracker: it points as the device it is mounted on (the chain's root turns)
            if (!DeviceStacks.IsStacked(w, self) && w.IsComponentTypeRegistered<DeviceInput>() && w.HasComponent<DeviceInput>(self))
            {
                ref readonly var c = ref w.GetComponentRO<DeviceInput>(self).Control;
                if ((c.Trackers & DeviceControl.OrientationPresent) != 0 && c.Orientation != default) orientation = Quaternion.Normalize(c.Orientation);
            }
            if (w.HasComponent<DevicePose>(self))
            {
                ref readonly var now = ref w.GetComponentRO<DevicePose>(self);
                if (now.Orientation == orientation && now.FovDegrees == fovDegrees) return;
                var next = now;
                next.Orientation = orientation; next.FovDegrees = fovDegrees;
                w.SetComponent(self, next);
            }
            else w.AddComponent(self, new DevicePose { Orientation = orientation, FovDegrees = fovDegrees });   // added WITH its value
        }
    }

    /// <summary>
    /// ⭐⭐⭐ The ONE resolver of a device's world pose (D-48) — ⭐⭐ SD-S5b (D-120, D-49 "resolved through A at the moment it is read") WALKS THE STACK: a device
    /// mounted in another device's station is at its HOST's world pose (recursively — the host's own <see cref="DevicePose"/> included, so it swings with the
    /// root's look) ∘ the host's station place (the <see cref="StationSeat"/> of its <c>Gen.Carrier</c> seat — the kit stations' one mechanism) ∘ its own pose;
    /// any other device is at its own placed <see cref="SimTransform"/> (the engine places every embarked item at its host station's place, WS-2) ∘ its own pose.
    /// ⛔ SUPERSEDES the S1b-3 "no chain walk is needed": <c>EmbarkedStateSystem</c> places from the host's <see cref="SimTransform"/>, never the host DEVICE's
    /// pose, so a stacked device did not follow its master's look (measured, §3.13d). ⭐ Every reader goes through here (the muscle's laser, the 3-D laser
    /// layer, the scripted station's look, the HUD's cue). 0 B.
    /// </summary>
    public static class DevicePoses
    {
        public static bool TryWorld(EntityRepository w, Entity device, out Vector3 position, out Quaternion rotation)
            => TryWorld(w, device, 0, out position, out rotation);

        private static bool TryWorld(EntityRepository w, Entity device, int depth, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.Identity;
            if (device.IsNull || !w.IsAlive(device)) return false;
            var host = depth < DeviceStacks.MaxDepth ? DeviceStacks.HostOf(w, device) : Entity.Null;
            if (!host.IsNull && TryWorld(w, host, depth + 1, out var hostAt, out var hostRot))
            {
                int seat = w.GetComponentRO<IsEmbarkedTag>(device).Seat;
                if (w.IsComponentTypeRegistered<CarrierStations>() && w.HasComponent<CarrierStations>(host) && w.GetComponentRO<CarrierStations>(host).IsStation(seat))
                    w.GetComponentRO<CarrierStations>(host).Seats[seat].Place(hostAt, hostRot, out position, out rotation);
                else { position = hostAt; rotation = hostRot; }
                position += Optic(w, device, rotation);   // ⭐ D-171 — its own optic above the place it is stacked at
            }
            else
            {
                if (!w.HasComponent<SimTransform>(device)) return false;
                ref readonly var at = ref w.GetComponentRO<SimTransform>(device);
                position = at.Position; rotation = at.Rotation;
                // ⭐⭐ D-170 (user, `2026-11-28`: "the offsetting case of the device held by a human character should definitely not be 0 because that would
                //   originate from the ground level") — a device CARRIED by a person (one of his carry stations: kit, hands) is used at his EYE: THE eye rule
                //   (TerrainWorldLosStrategy.EyeOf — his feet raised by the eye height of his stance, 1.7 m standing · 1.1 crouched · prone low, moved by his
                //   lean) that sight, the rifle's shot (ShotOrigin) and command guidance already use (R-217) — so he lases from where he sees and shoots.
                //   ⛔ Its SimTransform stays the mount point (his feet, D-169). A device in a vehicle's station keeps the station's place.
                if (CarriedBy(w, device) is { IsNull: false } man)
                    position = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeOf(w, man, Fdp.Toolkit.Combat.HitModel.LogicalStance(w, man));
                else position += Optic(w, device, rotation);
            }
            if (w.IsComponentTypeRegistered<DevicePose>() && w.HasComponent<DevicePose>(device))
            {
                ref readonly var pose = ref w.GetComponentRO<DevicePose>(device);
                var o = pose.Orientation == default ? Quaternion.Identity : pose.Orientation;
                position += Vector3.Transform(pose.Offset, rotation);
                rotation = rotation * o;
            }
            return true;
        }

        /// <summary>⭐⭐ D-171 — where the device's OPTIC sits above its base (its type's <see cref="DeviceInfo.OpticOffset"/>, device frame), turned by
        /// <paramref name="rotation"/> (its base's) — a tripod device's lens above its feet, a mounted one's above its station. 0 B.</summary>
        private static Vector3 Optic(EntityRepository w, Entity device, Quaternion rotation)
        {
            if (!w.IsComponentTypeRegistered<DeviceInfo>() || !w.HasComponent<DeviceInfo>(device)) return Vector3.Zero;
            var o = w.GetComponentRO<DeviceInfo>(device).OpticOffset;
            return o == Vector3.Zero ? Vector3.Zero : Vector3.Transform(o, rotation);
        }

        /// <summary>⭐ D-170 — the PERSON carrying <paramref name="device"/> in one of his carry stations, or null (in a vehicle's station, lying, stacked).</summary>
        public static Entity CarriedBy(EntityRepository w, Entity device)
        {
            if (!w.IsComponentTypeRegistered<IsEmbarkedTag>() || !w.HasComponent<IsEmbarkedTag>(device) || !w.IsComponentTypeRegistered<PersonCarrier>()) return Entity.Null;
            var host = w.GetComponentRO<IsEmbarkedTag>(device).VehicleEntity;
            return !host.IsNull && w.IsAlive(host) && w.HasComponent<PersonCarrier>(host) && w.HasComponent<SimTransform>(host) ? host : Entity.Null;
        }
    }

    /// <summary>
    /// ⭐⭐ SD-S5b (D-120, D-61, D-56) — STACKING: a device mounted in another DEVICE's station (its <see cref="IsEmbarkedTag"/> names a host that is a device — the
    /// host type's <c>Gen.Carrier</c> stacking station, accepting <c>MountKinds.Device</c>). <see cref="Root"/> is the chain's bottom device: ITS pose is what the
    /// station's look turns, whichever device the view is bound to (a stacked device ignores its own look and tracker while stacked — <see cref="DeviceLooks"/> —
    /// and uses them again when unstacked); its operator operates the whole stack (<see cref="DeviceOperators"/>). Reads only the device's own tag and the host's
    /// <see cref="DeviceInfo"/> (written once at creation — a declared stable read). 0 B.
    /// </summary>
    public static class DeviceStacks
    {
        /// <summary>The deepest stack walked (a cycle cannot loop).</summary>
        public const int MaxDepth = 4;

        /// <summary>The device <paramref name="device"/> is mounted in, or null (not stacked).</summary>
        public static Entity HostOf(EntityRepository w, Entity device)
        {
            if (device.IsNull || !w.IsComponentTypeRegistered<IsEmbarkedTag>() || !w.IsAlive(device) || !w.HasComponent<IsEmbarkedTag>(device)) return Entity.Null;
            var host = w.GetComponentRO<IsEmbarkedTag>(device).VehicleEntity;
            return DeviceMarker.IsDevice(w, host) ? host : Entity.Null;
        }

        /// <summary>Is <paramref name="device"/> mounted in another device?</summary>
        public static bool IsStacked(EntityRepository w, Entity device) => !HostOf(w, device).IsNull;

        /// <summary>The stack's root: the device at the bottom of <paramref name="device"/>'s chain (itself when not stacked).</summary>
        public static Entity Root(EntityRepository w, Entity device)
        {
            var e = device;
            for (int i = 0; i < MaxDepth; i++)
            {
                var host = HostOf(w, e);
                if (host.IsNull) break;
                e = host;
            }
            return e;
        }
    }
}
