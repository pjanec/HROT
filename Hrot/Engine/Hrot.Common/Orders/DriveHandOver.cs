using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Common.Orders
{
    /// <summary>
    /// ⭐ <c>CE-11041</c> (<c>docs/DESIGN_Entity_Control.md</c> §8h.10, HO-1) — the Drive hand-over of a STATION, in one class every station owns:
    /// <c>Possess(soldier, Riding)</c> → <c>Possess(vehicle, Fps)</c> after a Drive / Pilot press, the Get-out, and the release of a rider whose
    /// vehicle the station no longer drives. Moved unchanged from <c>PossessionStation</c> (<c>CE-1158</c>, §8h.9) so a remote seat (the gateway's
    /// DPS seat) gets it by construction. It reads REPLICATED state only (the soldier and the vehicle may be brained on different nodes, U-4) and
    /// sends through <see cref="EntityOrders"/>; the station's identity is <see cref="EntityOrders.StationFor"/>, so a seat with its own
    /// station id works unchanged. Stepped once per frame by its owner: <see cref="Step"/>.
    /// </summary>
    public sealed class DriveHandOver
    {
        /// <summary>Where the Drive hand-over stands (§8h driving sequence).</summary>
        public enum HandOverStep : byte
        {
            None = 0,
            /// <summary>Drive was pressed: waiting for the replicated passenger list to show the soldier in the driver seat.</summary>
            AwaitBoard,
            /// <summary>The soldier was asked to ride: waiting for his <see cref="PossessedBy"/> to say Riding.</summary>
            AwaitRiding,
            /// <summary>The vehicle was possessed: waiting for it to be held here.</summary>
            AwaitVehicle,
        }

        /// <summary>A hand-over step that has not moved on in this long is dropped (the press was refused, the seat went to someone else).</summary>
        public const float HandOverTimeout = 15f;

        private readonly Func<EntityRepository?> _world;
        private readonly Func<EntityOrders?> _orders;
        private HandOverStep _step;
        private Entity _hoSoldier, _hoVehicle;
        private float _hoSince;
        private Entity _getOutRider;
        private float _getOutSince;
        private float _orphanCheckAt;

        public DriveHandOver(Func<EntityRepository?> world, Func<EntityOrders?> orders)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        }

        /// <summary>Seconds this hand-over has been stepped (its timeouts run against it).</summary>
        public float Clock { get; private set; }

        /// <summary>The hand-over's step now.</summary>
        public HandOverStep Current => _step;

        /// <summary>A Drive hand-over is running, or a Get-out is waiting for the rider's first frame.</summary>
        public bool Handing => _step != HandOverStep.None || !_getOutRider.IsNull;

        /// <summary>The rider a Get-out was asked for, until the owner has sent his Get-out press (null otherwise).</summary>
        public Entity GetOutRider => _getOutRider;

        /// <summary>The owner has sent the Get-out press of <see cref="GetOutRider"/>.</summary>
        public void GetOutSent() => _getOutRider = Entity.Null;

        /// <summary>The unit this station DRIVES now (its Fps unit), or null. See <see cref="HeldHere(EntityRepository, out int, EntityOrders?)"/>.</summary>
        public Entity HeldHere(out int networkId)
        {
            networkId = 0;
            return _world() is { } w ? HeldHere(w, out networkId, _orders()) : Entity.Null;
        }

        /// <summary>
        /// ⭐ CE-1158 (§8h U-4) — the driver sent a Drive / Pilot press: once the soldier shows in the driver seat, ride him and drive the
        /// vehicle. The STATION orchestrates it, from replicated state, because the soldier and the vehicle may be brained on different nodes.
        /// </summary>
        public void Arm(Entity soldier, Entity vehicle)
        {
            _step = HandOverStep.AwaitBoard;
            _hoSoldier = soldier;
            _hoVehicle = vehicle;
            _hoSince = Clock;
        }

        /// <summary>
        /// ⭐ CE-1158 (§8h U-4) — Get out of a vehicle with a rider: possess the rider as Fps — which releases the vehicle (one Fps unit per
        /// station) — and, once the owner's view is back in him, it sends his Get-out press (<see cref="GetOutRider"/>). False when
        /// <paramref name="vehicle"/> has no rider.
        /// </summary>
        public bool TryGetOut(Entity vehicle)
        {
            if (_world() is not { } w || _orders() is not { } orders) return false;
            var rider = RiderOf(w, vehicle, orders);
            if (rider.IsNull || MapOf(w) is not { } riderMap || !riderMap.TryGetNetworkId(rider, out int id)) return false;
            orders.Possess(id, PossessionMode.Fps);
            _getOutRider = rider;
            _getOutSince = Clock;
            return true;
        }

        /// <summary>Forgets a running hand-over and a pending Get-out (a world boundary: the entities they name are gone).</summary>
        public void Reset()
        {
            _step = HandOverStep.None;
            _hoSoldier = _hoVehicle = _getOutRider = Entity.Null;
        }

        /// <summary>One frame: advances the clock, steps the hand-over and releases orphan riders.</summary>
        public void Step(float dt)
        {
            Clock += dt;
            if (_world() is { } w) { StepHandOver(w); ReleaseOrphanRiders(w); }
        }

        private void StepHandOver(EntityRepository w)
        {
            if (!_getOutRider.IsNull && Clock - _getOutSince > HandOverTimeout) _getOutRider = Entity.Null;
            if (_step == HandOverStep.None) return;
            if (Clock - _hoSince > HandOverTimeout || !w.IsAlive(_hoSoldier) || !w.IsAlive(_hoVehicle) || _orders() is not { } orders)
            {
                _step = HandOverStep.None;
                return;
            }
            if (MapOf(w) is not { } map) return;   // a world with no id map has nothing to possess by
            switch (_step)
            {
                case HandOverStep.AwaitBoard:
                    if (!HeldByThis(w, _hoSoldier, out _, orders)) { _step = HandOverStep.None; break; }   // released meanwhile (Esc)
                    if (InDriverSeat(w, _hoSoldier, _hoVehicle) && map.TryGetNetworkId(_hoSoldier, out int soldierId))
                    {
                        orders.Possess(soldierId, PossessionMode.Riding);   // O-2 — his task stays paused
                        _step = HandOverStep.AwaitRiding;
                    }
                    break;
                case HandOverStep.AwaitRiding:
                    if (!HeldByThis(w, _hoSoldier, out var mode, orders)) { _step = HandOverStep.None; break; }
                    // ⚠ only once he RIDES: possessing the vehicle as Fps releases this station's other Fps unit — which he still was
                    if (mode == PossessionMode.Riding && map.TryGetNetworkId(_hoVehicle, out int vehicleId))
                    {
                        orders.Possess(vehicleId, PossessionMode.Fps);
                        _step = HandOverStep.AwaitVehicle;
                    }
                    break;
                case HandOverStep.AwaitVehicle:
                    if (HeldHere(w, out _, orders) == _hoVehicle) _step = HandOverStep.None;
                    break;
            }
        }

        /// <summary>
        /// ⭐ CE-1158 — a rider whose vehicle this station no longer drives (the operator possessed someone else from the menu, the vehicle
        /// was released or destroyed, he was thrown out) is let go, so his own task resumes. Checked once a second (a release takes a
        /// round trip to show).
        /// </summary>
        private void ReleaseOrphanRiders(EntityRepository w)
        {
            if (_step != HandOverStep.None || Clock < _orphanCheckAt || !w.IsComponentTypeRegistered<PossessedBy>()) return;
            _orphanCheckAt = Clock + 1f;
            if (_orders() is not { } orders || MapOf(w) is not { } map) return;
            foreach (var unit in w.Query().With<PossessedBy>().Build())
            {
                if (unit == _getOutRider || !HeldByThis(w, unit, out var mode, orders) || mode != PossessionMode.Riding) continue;
                var vehicle = w.IsComponentTypeRegistered<IsEmbarkedTag>() && w.HasComponent<IsEmbarkedTag>(unit)
                    ? w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity : Entity.Null;
                if (!vehicle.IsNull && HeldByThis(w, vehicle, out var vehicleMode, orders) && vehicleMode != PossessionMode.Riding) continue;
                // ⭐ CE-1210 (§8n MS-7) — the station plays a GUN of his vehicle (seat switching): he stays held for the way back
                if (!vehicle.IsNull && HeldHere(w, out _, orders) is var fps && !fps.IsNull && VehicleOf(w, fps) == vehicle) continue;
                if (map.TryGetNetworkId(unit, out int id)) orders.Unpossess(id);
            }
        }

        private static bool HeldByThis(EntityRepository w, Entity unit, out PossessionMode mode, EntityOrders? orders)
        {
            mode = PossessionMode.None;
            if (!w.IsComponentTypeRegistered<PossessedBy>() || !w.IsAlive(unit) || !w.HasComponent<PossessedBy>(unit)
                || !w.HasSingletonManaged<NetworkEntityMap>()) return false;
            var held = w.GetComponentRO<PossessedBy>(unit);
            mode = held.Mode;
            return held.StationNodeId == StationOf(w, orders).StationFor(unit);
        }

        private static NetworkEntityMap? MapOf(EntityRepository w)
            => w.HasSingletonManaged<NetworkEntityMap>() ? w.GetSingletonManaged<NetworkEntityMap>() : null;

        private static EntityOrders StationOf(EntityRepository w, EntityOrders? orders)
            => orders ?? new EntityOrders(w, w.GetSingletonManaged<NetworkEntityMap>()!, geo: null);

        /// <summary>
        /// The unit a station DRIVES in <paramref name="w"/> (its <see cref="PossessedBy"/> names the station — <see cref="EntityOrders.StationFor"/>
        /// of <paramref name="orders"/>, or this node's when null), or null. ⭐ CE-1158 — a unit held as <see cref="PossessionMode.Riding"/> is
        /// skipped (the soldier sitting in the vehicle the station drives), and of two Fps units (a moment during a take-over) the newest
        /// possession wins (<see cref="PossessedBy.Epoch"/>).
        /// </summary>
        public static Entity HeldHere(EntityRepository w, out int networkId, EntityOrders? orders = null)
        {
            networkId = 0;
            if (!w.IsComponentTypeRegistered<PossessedBy>() || !w.HasSingletonManaged<NetworkEntityMap>()) return Entity.Null;
            var map = w.GetSingletonManaged<NetworkEntityMap>()!;
            // ⭐ C3 CC-10 — the one owner of "what this station drives" (StationBody.Driven); a held unit always has a network id (possession is by id)
            var best = StationBody.Driven(w, orders?.Station);
            if (best.IsNull || !map.TryGetNetworkId(best, out networkId)) { networkId = 0; return Entity.Null; }
            return best;
        }

        /// <summary>⭐ CE-1158 — the soldier the station holds as <see cref="PossessionMode.Riding"/> aboard <paramref name="vehicle"/>, or null.</summary>
        public static Entity RiderOf(EntityRepository w, Entity vehicle, EntityOrders? orders = null)
        {
            if (vehicle.IsNull || !w.IsComponentTypeRegistered<PossessedBy>() || !w.IsComponentTypeRegistered<IsEmbarkedTag>()
                || !w.HasSingletonManaged<NetworkEntityMap>()) return Entity.Null;
            var station = StationOf(w, orders);
            foreach (var unit in w.Query().With<PossessedBy>().With<IsEmbarkedTag>().Build())
            {
                var held = w.GetComponentRO<PossessedBy>(unit);
                if (w.IsAlive(unit) && held.Mode == PossessionMode.Riding && held.StationNodeId == station.StationFor(unit)
                    && w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity == vehicle) return unit;
            }
            return Entity.Null;
        }

        /// <summary>⭐ CE-1158 — is <paramref name="soldier"/> in <paramref name="vehicle"/>'s DRIVER seat, by the replicated passenger list?</summary>
        public static bool InDriverSeat(EntityRepository w, Entity soldier, Entity vehicle)
        {
            if (!w.IsComponentTypeRegistered<IsEmbarkedTag>() || !w.HasComponent<IsEmbarkedTag>(soldier)) return false;
            var tag = w.GetComponentRO<IsEmbarkedTag>(soldier);
            if (tag.VehicleEntity != vehicle) return false;
            return !w.IsComponentTypeRegistered<CarrierSeats>() || !w.HasComponent<CarrierSeats>(vehicle)
                || w.GetComponentRO<CarrierSeats>(vehicle).Roles[tag.Seat] == SeatRole.Driver;
        }

        /// <summary>The vehicle <paramref name="unit"/> plays a seat of: itself when it has stations, else the vehicle he sits in; null otherwise
        /// (the one definition — <c>SeatSwitch.VehicleOf</c> forwards here).</summary>
        public static Entity VehicleOf(EntityRepository w, Entity unit)
        {
            if (!w.IsAlive(unit)) return Entity.Null;
            if (Has<CarrierStations>(w, unit) && Has<PassengerBuffer>(w, unit) && !Has<PersonCarrier>(w, unit)) return unit;
            return Has<IsEmbarkedTag>(w, unit) ? w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity : Entity.Null;
        }

        private static bool Has<T>(EntityRepository w, Entity e) where T : unmanaged
            => w.IsComponentTypeRegistered<T>() && w.HasComponent<T>(e);
    }
}
