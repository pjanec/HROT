using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1e-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1 R7-4 / R5-8, §3.2 D-45) — a DEVICE type's creation:
    /// from its <see cref="DeviceDefDto"/> the ITEM facts (<see cref="Mountable"/> — kind <see cref="MountKinds.Device"/>, so it boards a kit station
    /// or a device station and nothing else, and every item reader — Take, Drop, Man, the carry search — treats it as an item) and the device marker
    /// (<see cref="DeviceInfo"/>: its TKB type and the behaviour the device tick starts). Written once, at creation, on the creating node — both
    /// <c>NoScenario</c> type facts (a scenario places the device by its type, never by these).
    /// <para>⭐ One creation path for every device type: no device is hand-stamped in production (the S1c–S1d-3 rails stamp <see cref="DeviceInfo"/>
    /// themselves until this). Its brain (<c>BehaviorState</c>) comes from the type's <c>BehaviorProfileDto</c>, which
    /// <c>BehaviorTkbTranslator</c> stamps WITHOUT the tactical block for a device type (R7-4).</para>
    /// </summary>
    public sealed class DeviceTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(DeviceDefDto); }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(Mountable);
            yield return typeof(DeviceInfo);
            yield return typeof(RemoteControl);   // ⭐ SD-S2c — a ground station only
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<DeviceDefDto>();
            if (dto == null) return;
            if (repo.IsComponentTypeRegistered<Mountable>())
            {
                var m = new Mountable { MountKinds = (byte)dto.MountKinds, Portable = (byte)dto.Portable, CarrySpeedFactor = dto.CarrySpeedFactor };
                if (repo.HasComponent<Mountable>(entity)) repo.SetComponent(entity, m); else repo.AddComponent(entity, m);
            }
            if (repo.IsComponentTypeRegistered<DeviceInfo>())
            {
                var info = new DeviceInfo { DeviceType = template.TkbType, Behavior = new FixedString64(dto.Behavior ?? ""),
                    OpticOffset = new System.Numerics.Vector3(dto.OpticOffsetX, dto.OpticOffsetY, dto.OpticOffsetZ) };   // ⭐ D-171
                if (repo.HasComponent<DeviceInfo>(entity)) repo.SetComponent(entity, info); else repo.AddComponent(entity, info);
            }
            // ⭐ SD-S2c (§3.13c D-104) — a ground station's command exists from creation (Off): its drone's brain reads it across entities
            if (dto.RemoteStation && repo.IsComponentTypeRegistered<RemoteControl>() && !repo.HasComponent<RemoteControl>(entity))
                repo.AddComponent(entity, new RemoteControl { Mode = RemoteMode.Off });
        }

        /// <summary>True when <paramref name="template"/> is a device type (<c>BehaviorTkbTranslator</c>'s "not a unit" test, R7-4).</summary>
        public static bool IsDeviceType(TkbTemplate template) => template.GetDescriptor<DeviceDefDto>() != null;
    }
}
