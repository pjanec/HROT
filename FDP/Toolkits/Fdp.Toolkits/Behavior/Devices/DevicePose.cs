using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

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
            if (w.IsComponentTypeRegistered<DeviceInput>() && w.HasComponent<DeviceInput>(self))
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
    /// ⭐ The ONE resolver of a device's world pose (D-48). ⭐ AS BUILT: no mount-chain walk is needed — the engine already places every embarked
    /// item at its host station's place each frame (<c>EmbarkedStateSystem</c>, WS-2), and that chains through a device mounted on a device; so the
    /// world pose is the device's own <see cref="SimTransform"/> composed with its <see cref="DevicePose"/>.
    /// </summary>
    public static class DevicePoses
    {
        public static bool TryWorld(EntityRepository w, Entity device, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.Identity;
            if (device.IsNull || !w.IsAlive(device) || !w.HasComponent<SimTransform>(device)) return false;
            ref readonly var at = ref w.GetComponentRO<SimTransform>(device);
            position = at.Position; rotation = at.Rotation;
            if (w.IsComponentTypeRegistered<DevicePose>() && w.HasComponent<DevicePose>(device))
            {
                ref readonly var pose = ref w.GetComponentRO<DevicePose>(device);
                var o = pose.Orientation == default ? Quaternion.Identity : pose.Orientation;
                position += Vector3.Transform(pose.Offset, at.Rotation);
                rotation = at.Rotation * o;
            }
            return true;
        }
    }
}
