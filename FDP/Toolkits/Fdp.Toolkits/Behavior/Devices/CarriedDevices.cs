using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 R4-3 / D-63, "S1d-3 AS BUILT") — the hand-held devices a
    /// PERSON carries: the occupants of his own carry seats (his <see cref="PassengerBuffer"/> — the kit stations, D-63) that are devices
    /// (<see cref="DeviceInfo"/>). Read by the body's brain: ① carrying one satisfies <c>HeldItems.Has(Binoculars)</c> — the held item is the optic AT
    /// HIS EYES (R4-3, generalised to every hand-held device by D-63: each is raised the same way); ② while one is VIEWED (its frame's
    /// <see cref="DeviceControl.Viewed"/>, set by the station while a view is bound to it) his brain raises it.
    /// <para>⭐ The parallel rule: reads his OWN buffer and, on each carried device, <see cref="DeviceInfo"/> (written once at creation) and
    /// <see cref="DeviceInput"/> (written by the device tick's serial pre-pass, never in the brain phase) — both declared tick-start reads
    /// (<c>BrainReads</c>); it writes nothing. 0 B.</para>
    /// </summary>
    public static class CarriedDevices
    {
        /// <summary>Does <paramref name="man"/> carry a device?</summary>
        public static bool Any(EntityRepository w, Entity man) => Find(w, man, viewedOnly: false);

        /// <summary>Does a view of his station show a device <paramref name="man"/> carries (its newest frame's <see cref="DeviceControl.Viewed"/>)?</summary>
        public static bool AnyViewed(EntityRepository w, Entity man) => Find(w, man, viewedOnly: true);

        private static bool Find(EntityRepository w, Entity man, bool viewedOnly)
        {
            if (man.IsNull || !w.IsAlive(man) || !w.IsComponentTypeRegistered<DeviceInfo>() || !w.IsComponentTypeRegistered<PassengerBuffer>()
                || !w.HasComponent<PassengerBuffer>(man)) return false;
            if (viewedOnly && !w.IsComponentTypeRegistered<DeviceInput>()) return false;
            ref readonly var seats = ref w.GetComponentRO<PassengerBuffer>(man);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var item = seats.Passengers[i];
                if (item.IsNull || !w.IsAlive(item) || !w.HasComponent<DeviceInfo>(item)) continue;
                if (!viewedOnly) return true;
                if (w.HasComponent<DeviceInput>(item) && w.GetComponentRO<DeviceInput>(item).Control.Viewed != 0) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ S1d-3 (§3.6 D-38, §3.3 R7-1) — the devices a STATION operates: the replicated rows the device walk wrote (<see cref="PossessionMode.Operated"/>,
    /// naming the station of the operator's body). The station computes nothing (R7-1); this is the one reader of that set — the view cycle's sources.
    /// Ordered by network id (a stable cycle); 0 B (the caller's span).
    /// </summary>
    public static class OperatedDevices
    {
        [ThreadStatic] private static EntityRepository? _queryFor;
        [ThreadStatic] private static EntityQuery? _rows;

        /// <summary>Fills <paramref name="dest"/> with the devices <paramref name="station"/> operates (network id ascending) and returns how many.</summary>
        public static int Of(EntityRepository w, StationId station, Span<Entity> dest)
        {
            if (!w.IsComponentTypeRegistered<PossessedBy>() || !w.IsComponentTypeRegistered<DeviceInfo>()) return 0;
            if (_rows == null || !ReferenceEquals(w, _queryFor)) { _rows = w.Query().With<PossessedBy>().With<DeviceInfo>().Build(); _queryFor = w; }
            int n = 0;
            foreach (var d in _rows)
            {
                if (n >= dest.Length) break;
                ref readonly var row = ref w.GetComponentRO<PossessedBy>(d);
                if (row.Mode != PossessionMode.Operated || row.StationNodeId != station) continue;
                long id = NetIdOf(w, d);
                int at = n++;
                while (at > 0 && NetIdOf(w, dest[at - 1]) > id) { dest[at] = dest[at - 1]; at--; }   // insertion: a handful of devices
                dest[at] = d;
            }
            return n;
        }

        /// <summary>The device's network id (what a view binds to, D-41), or 0.</summary>
        public static long NetIdOf(EntityRepository w, Entity d)
            => w.IsComponentTypeRegistered<NetworkIdentity>() && w.HasComponent<NetworkIdentity>(d) ? w.GetComponentRO<NetworkIdentity>(d).Value : 0;
    }
}
