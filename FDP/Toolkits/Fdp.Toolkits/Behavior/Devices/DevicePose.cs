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
