using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1b-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.3, D-73, R4-2, R7-1) — WHO OPERATES A DEVICE, and the one
    /// writer of its <see cref="PossessedBy"/> row (<see cref="PossessionMode.Operated"/>). A device has no fire conditions (that is
    /// <c>WeaponSystems.ControllerOf</c>'s weapon rule); its operator is: ① the man in its OWN gunner seat (a device on a tripod, MANNED with USE —
    /// D-60), ② the PERSON whose carry station holds it (carried — D-62 / D-63), ③ the occupant of the seat that OPERATES the host station it is
    /// mounted in (a vehicle's device station — §3q), ④ ⭐ SD-S5b (D-120) — mounted in another DEVICE (stacked): the operator of that device. The device's row names the station that holds that operator's BODY (its Fps / Riding hold —
    /// <see cref="StationBody.IsBodyMode"/>), or the device has no row. ⭐ Run by the device tick's serial pre-pass every frame on the device's
    /// brain owner, so the station never computes it: it reads the replicated row (R7-1), and the existing <c>HeldBy</c> gate authorises its frames.
    /// A change of operator bumps <see cref="PossessedBy.Epoch"/> and clears the old <see cref="DeviceInput"/> — <c>Possess</c>'s own rule.
    /// </summary>
    public static class DeviceOperators
    {
        /// <summary>The body operating <paramref name="device"/>, or null (see the class summary).</summary>
        public static Entity OperatorOf(EntityRepository w, Entity device) => OperatorOf(w, device, 0);

        private static Entity OperatorOf(EntityRepository w, Entity device, int depth)
        {
            var gunner = Fdp.Toolkit.Combat.WeaponSystems.GunnerOf(w, device);   // ① manned (its own gunner seat)
            if (!gunner.IsNull) return gunner;
            if (!w.IsComponentTypeRegistered<IsEmbarkedTag>() || !w.HasComponent<IsEmbarkedTag>(device)) return Entity.Null;
            ref readonly var tag = ref w.GetComponentRO<IsEmbarkedTag>(device);
            var host = tag.VehicleEntity;
            if (host.IsNull || !w.IsAlive(host)) return Entity.Null;
            // ⭐ SD-S5b (D-120) — ④ STACKED on another device: the stack's operator operates it (his look turns the root, DeviceStacks)
            if (DeviceMarker.IsDevice(w, host)) return depth < DeviceStacks.MaxDepth ? OperatorOf(w, host, depth + 1) : Entity.Null;
            if (Embarkation.IsPersonCarrier(w, host)) return host;               // ② carried by a person
            if (!w.IsComponentTypeRegistered<CarrierStations>() || !w.HasComponent<CarrierStations>(host)) return Entity.Null;
            ref readonly var stations = ref w.GetComponentRO<CarrierStations>(host);
            if (!stations.IsStation(tag.Seat)) return Entity.Null;
            int operatedBy = stations.Seats[tag.Seat].OperatedBy;                  // ③ a host station operated by a crew seat
            if (operatedBy < 0 || operatedBy >= PassengerBuffer.Capacity || !w.HasComponent<PassengerBuffer>(host)) return Entity.Null;
            return w.GetComponentRO<PassengerBuffer>(host).Passengers[operatedBy];
        }

        /// <summary>The station whose BODY operates <paramref name="device"/>, or null.</summary>
        public static StationId? StationOf(EntityRepository w, Entity device)
        {
            var op = OperatorOf(w, device);
            if (op.IsNull || !w.IsAlive(op) || !w.IsComponentTypeRegistered<PossessedBy>() || !w.HasComponent<PossessedBy>(op)) return null;
            ref readonly var held = ref w.GetComponentRO<PossessedBy>(op);
            return StationBody.IsBodyMode(held.Mode) ? held.StationNodeId : null;
        }

        private static void ClearInput(EntityRepository w, Entity device)
        {
            if (w.IsComponentTypeRegistered<DeviceInput>() && w.HasComponent<DeviceInput>(device)) w.RemoveComponent<DeviceInput>(device);
        }

        /// <summary>Writes the device's row (see the class summary). Serial, on the device's brain owner. Changes only when the operator's station changes.</summary>
        public static void Sync(EntityRepository w, Entity device)
        {
            if (!w.IsComponentTypeRegistered<PossessedBy>()) return;
            bool has = w.HasComponent<PossessedBy>(device);
            if (has && w.GetComponentRO<PossessedBy>(device).Mode != PossessionMode.Operated) return;   // not ours (a device is never possessed — Possessability)
            var station = StationOf(w, device);
            if (station is not { } s)
            {
                if (!has) return;
                w.RemoveComponent<PossessedBy>(device);
                ClearInput(w, device);
                return;
            }
            if (has && w.GetComponentRO<PossessedBy>(device).StationNodeId == s) return;   // same operator's station: nothing to write
            uint epoch = has ? w.GetComponentRO<PossessedBy>(device).Epoch + 1 : 1;
            var row = new PossessedBy { StationNodeId = s, Mode = PossessionMode.Operated, Epoch = epoch };
            if (has) w.SetComponent(device, row); else w.AddComponent(device, row);
            ClearInput(w, device);   // the new station starts clean
        }
    }
}
