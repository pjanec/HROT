using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>⭐ SD-S2c (§3.13c D-104 / D-110) — the command a ground station sends its drone (the GCS's mode as the drone obeys it).</summary>
    public enum RemoteMode : byte
    {
        /// <summary>The station is off: no radio ⇒ the drone's link is lost.</summary>
        Off = 0,
        /// <summary>On, not armed: the drone answers and stays down.</summary>
        Idle = 1,
        /// <summary>Armed on the ground: ready to take off.</summary>
        Armed = 2,
        /// <summary>Fly: from the ground it takes off to its hover height, then the sticks fly it (manual).</summary>
        Fly = 3,
        /// <summary>Land where it is.</summary>
        Land = 4,
        /// <summary>Return to the launch point and land.</summary>
        ReturnHome = 5,
    }

    /// <summary>
    /// ⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-104) — THE GROUND STATION'S COMMAND, written by the station
    /// device's own logic ON ITSELF (live frames only, on change) and READ by the paired drone's own brain (<c>RemotePilot</c>, a declared tick-start read):
    /// the drone is flown THROUGH the device, never possessed. The pairing is <see cref="ControlledEntities"/> slot 0 (D-147). <see cref="Seq"/> moves on
    /// every MODE press, so the drone tells a new command from the standing one (a failsafe ends on a new command, D-107). The sticks are Mode 2, already
    /// mapped to the drone's frame: <see cref="Forward"/> / <see cref="Lateral"/> (+ left) / <see cref="Yaw"/> (a RATE, + left / counter-clockwise) /
    /// <see cref="Vertical"/> (+ up), each −1 … 1. Unmanaged, recorded; not authored.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RemoteControl)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RemoteControl
    {
        public RemoteMode Mode;
        public float Forward, Lateral, Yaw, Vertical;
        /// <summary>The gimbal's pitch (radians, 0 level … −π/2 straight down).</summary>
        public float CameraPitch;
        /// <summary>Bumped on every mode press.</summary>
        public uint Seq;
    }

    /// <summary>⭐ SD-S2c (§3.13c D-109) — how a view bound to a device films through <see cref="DeviceView.Entity"/>.</summary>
    public enum DeviceViewMode : byte
    {
        /// <summary>At the entity's eye (a drone's camera), at <see cref="DeviceView.Pitch"/>.</summary>
        Eyes = 0,
        /// <summary>Behind it, looking along its flight (the missile's view, SD-S4b).</summary>
        Chase = 1,
    }

    /// <summary>
    /// ⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-109) — ONE "a view follows an entity" rule: a view bound to a
    /// device whose <see cref="Entity"/> resolves on this node puts the camera on that entity (the station's existing <c>FollowCameraDriver</c> — at its eye at
    /// <see cref="Pitch"/> for <see cref="DeviceViewMode.Eyes"/>); none ⇒ the operator's own view. Written by the device's own logic ON ITSELF (in pause too:
    /// the gimbal is the device's state). Unmanaged, recorded; not authored.
    /// ⚠ AS BUILT: <see cref="Entity"/> is this node's handle (as <see cref="ControlledEntities"/>), not a network id — SD-C re-keys both by network id.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DeviceView)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DeviceView
    {
        public Entity Entity;
        public DeviceViewMode Mode;
        /// <summary>The camera's pitch below the entity's horizon (radians, 0 level … −π/2 down) — the gimbal.</summary>
        public float Pitch;
    }

    /// <summary>⭐ SD-S2c (D-109) — the one reader and the one writer of <see cref="DeviceView"/>.</summary>
    public static class DeviceViews
    {
        /// <summary>True when <paramref name="device"/> films through a live entity on this node: that entity, the mode and the pitch.</summary>
        public static bool TryTarget(EntityRepository w, Entity device, out Entity target, out DeviceViewMode mode, out float pitch)
        {
            target = Entity.Null; mode = default; pitch = 0f;
            if (device.IsNull || !w.IsAlive(device) || !w.IsComponentTypeRegistered<DeviceView>() || !w.HasComponent<DeviceView>(device)) return false;
            ref readonly var v = ref w.GetComponentRO<DeviceView>(device);
            if (v.Entity.IsNull || !w.IsAlive(v.Entity) || !w.HasComponent<SimTransform>(v.Entity)) return false;
            target = v.Entity; mode = v.Mode; pitch = v.Pitch;
            return true;
        }

        /// <summary>Writes the device's own view on change (added WITH its value — the parallel rule).</summary>
        public static void Write(EntityRepository w, Entity self, in DeviceView next)
        {
            if (!w.IsComponentTypeRegistered<DeviceView>()) return;
            if (w.HasComponent<DeviceView>(self))
            {
                ref readonly var now = ref w.GetComponentRO<DeviceView>(self);
                if (now.Entity == next.Entity && now.Mode == next.Mode && now.Pitch == next.Pitch) return;
                w.SetComponent(self, next);
            }
            else w.AddComponent(self, next);
        }
    }
}
