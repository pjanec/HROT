using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Possession
{
    /// <summary>
    /// ⭐⭐ <c>DESIGN_Player_Join.md</c> §9 (S1-c, D-8) — the stations' RESERVATIONS on units in one world: a station that was given a unit (by a
    /// gameplay server, through its player slot) holds a claim on it before it possesses it, and its side comes from the reservation before
    /// it holds anything. <see cref="StationClaims"/> reads it FIRST, then the held unit (today's rule). ⭐ Empty unless a slot reader fills it
    /// — with no gameplay composed, the claim rule is exactly R-4096's. Per world (the <c>AttributeInterpreterProvider.GetDescriptorMap</c>
    /// shape), because two worlds in one process (<c>--mode all</c>) are two nodes.
    /// </summary>
    public sealed class StationReservations
    {
        private static readonly ConditionalWeakTable<EntityRepository, StationReservations> PerWorld = new();

        private readonly Dictionary<int, Reservation> _byStation = new();
        private readonly Dictionary<int, int> _stationByUnitNetId = new();

        /// <summary>A station's reservation: its side and the unit (network id) it was given; <see cref="UnitNetId"/> 0 = a side only (no unit yet).</summary>
        public readonly record struct Reservation(ForceId Side, int UnitNetId);

        /// <summary>The reservations of <paramref name="world"/>, created on first use (the slot reader's side).</summary>
        public static StationReservations Of(EntityRepository world) => PerWorld.GetValue(world, _ => new StationReservations());

        /// <summary>The reservations of <paramref name="world"/> when anything ever filled them (the readers' side — never creates).</summary>
        public static bool TryGet(EntityRepository world, out StationReservations reservations)
        {
            if (PerWorld.TryGetValue(world, out var r) && r._byStation.Count > 0) { reservations = r; return true; }
            reservations = null!;
            return false;
        }

        /// <summary>Records (or replaces) <paramref name="station"/>'s reservation.</summary>
        public void Set(int station, ForceId side, int unitNetId)
        {
            Remove(station);
            _byStation[station] = new Reservation(side, unitNetId);
            if (unitNetId != 0) _stationByUnitNetId[unitNetId] = station;
        }

        /// <summary>Drops <paramref name="station"/>'s reservation (its slot lapsed or was disposed).</summary>
        public void Remove(int station)
        {
            if (!_byStation.Remove(station, out var old)) return;
            if (old.UnitNetId != 0 && _stationByUnitNetId.TryGetValue(old.UnitNetId, out var s) && s == station)
                _stationByUnitNetId.Remove(old.UnitNetId);
        }

        /// <summary><paramref name="station"/>'s reservation; false when it has none.</summary>
        public bool TryOf(int station, out Reservation reservation) => _byStation.TryGetValue(station, out reservation);

        /// <summary>The station <paramref name="unitNetId"/> is reserved for; false when none is.</summary>
        public bool TryReservedFor(int unitNetId, out int station) => _stationByUnitNetId.TryGetValue(unitNetId, out station);

        /// <summary>How many stations hold a reservation.</summary>
        public int Count => _byStation.Count;
    }
}
