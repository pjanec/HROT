using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1 R5-8, §3.5) — THE device marker: an item a trainee
    /// operates whose LOGIC runs as a behaviour on the brain. Its presence is the device test (the device tick's filter, <see cref="BrainTickSystem"/>'s
    /// exclusion, the HUD projector's key); <see cref="Behavior"/> names the behaviour the device runs — the device tick starts it, and restarts it after a
    /// finish, a fault or a relayout, so a device's logic never stops (R6-2, R7-2). Unmanaged, RECORDED (replay draws the HUD from it), written once from
    /// the device's type; ⛔ never the device's state (that is its <c>[UnitMemory]</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DeviceInfo)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DeviceInfo
    {
        /// <summary>The device's TKB type.</summary>
        public long DeviceType;
        /// <summary>The behaviour the device runs (its logic) — a registered behaviour name.</summary>
        public FixedString64 Behavior;
        /// <summary>⭐ D-171 — where its optic sits above its own base (the type's <c>DeviceDefDto.OpticOffsetX/Y/Z</c>, device frame) — read by
        /// <c>DevicePoses.TryWorld</c> for a device that is NOT carried by a person (a carried one is used at his eye, D-170). Written once, at creation.</summary>
        public System.Numerics.Vector3 OpticOffset;
    }

    /// <summary>⭐ The ONE device test.</summary>
    public static class DeviceMarker
    {
        public static bool IsDevice(EntityRepository w, Entity e)
            => !e.IsNull && w.IsAlive(e) && w.IsComponentTypeRegistered<DeviceInfo>() && w.HasComponent<DeviceInfo>(e);
    }
}
