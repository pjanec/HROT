using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>The outcome of <see cref="Embarkation.TryEmbark"/>.</summary>
    public enum EmbarkResult : byte
    {
        /// <summary>The passenger is now aboard.</summary>
        Applied,
        /// <summary>The passenger was already aboard this vehicle — nothing to write.</summary>
        AlreadyAboard,
        /// <summary>The vehicle has no free seat.</summary>
        Full,
        /// <summary>Either entity is gone, the vehicle carries no passengers, or either is a body-less UNIT (a squad's commander,
        /// <c>UnitEntity</c>), which neither takes a seat nor carries anyone.</summary>
        Invalid,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2a (R-224) — THE embarkation rules: the one place that writes a vehicle's <see cref="PassengerBuffer"/>, a
    /// passenger's <see cref="IsEmbarkedTag"/> and the capabilities boarding strips. Its callers: <c>EmbarkationSystem</c> (the
    /// commands, applied on the vehicle's owner) and scenario load (genesis, inside its own transaction). ⛔ It used to be written three
    /// times (<c>EmbarkExecutor</c>, <c>EditorCargoSystem</c>, genesis) and left twice, and the copies disagreed.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7, §7.1a.
    /// </summary>
    public static class Embarkation
    {
        /// <summary>Hull radius assumed when the vehicle has no collider (m).</summary>
        public const float DefaultHullRadius = 2f;
        /// <summary>Gap between the hull and the dismounted column (m).</summary>
        public const float DismountClearance = 1.5f;
        /// <summary>Spacing of the dismounted column along the vehicle (m).</summary>
        public const float DismountSpacing = 1.5f;

        /// <summary>⭐ <c>CE-3110</c> I-2b — NED's <c>dtPassengerBuffer</c>: the vehicle's passenger list is published under this key (Brain
        /// group), and only its owner arbitrates a boarding. A rail pins it to <c>EDescriptorType</c> (as the door's ordinal is).</summary>
        public const long PassengerBufferDescriptorOrdinal = 131;
        /// <summary>NED's <c>dtWorldPos</c>: the node owning a passenger's POSITION is the one that places it beside the hull.</summary>
        public const long PositionDescriptorOrdinal = 2;

        public static readonly long PassengerBufferKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(PassengerBufferDescriptorOrdinal, 0);
        public static readonly long PositionKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(PositionDescriptorOrdinal, 0);

        /// <summary>The capabilities a seat takes away (and leaving gives back).</summary>
        public const ActorCapabilities StrippedAboard = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;

        /// <summary>
        /// True when THIS node arbitrates boarding for <paramref name="vehicle"/> (R-224: the vehicle's owner). With no
        /// <see cref="NetworkAuthority"/> (one node, the editor) every vehicle is ours — ⛔ except a GHOST, a replica whose master has not
        /// arrived (applying there would fork the passenger list). ⭐ I-2b: the owner of <see cref="PassengerBufferKey"/> (the vehicle's
        /// brain group), as doors gate on theirs (<c>DoorCommandSystem.OwnsDoor</c>).
        /// </summary>
        public static bool OwnsVehicle(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(vehicle))
                && repo.GetLifecycleState(vehicle) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, vehicle, PassengerBufferKey);
        }

        /// <summary>True when this node owns <paramref name="passenger"/>'s position (it places a leaving body). With no
        /// <see cref="NetworkAuthority"/> (one node), always.</summary>
        public static bool OwnsPosition(EntityRepository repo, Entity passenger)
            => Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, passenger, PositionKey);

        /// <summary>⭐ I-2c (R-271) — true when this node owns <paramref name="passenger"/>'s <c>Health</c> (Brain group): the node that applies
        /// its death. The same gate as <c>HealthApplicationSystem</c> (no <see cref="NetworkAuthority"/> ⇒ ours).</summary>
        public static bool OwnsHealth(EntityRepository repo, Entity passenger)
            => Has<Fdp.Toolkit.Combat.Components.Health>(repo, passenger)
               && !(Has<NetworkAuthority>(repo, passenger) && !repo.HasAuthority<Fdp.Toolkit.Combat.Components.Health>(passenger));

        /// <summary>Is <paramref name="passenger"/> in <paramref name="vehicle"/>'s list?</summary>
        public static bool IsAboard(EntityRepository repo, Entity passenger, Entity vehicle)
            => SlotOf(repo, passenger, vehicle) >= 0;

        /// <summary>
        /// ⭐ <c>CE-2151</c> (R-2009) — how many plain boardings <paramref name="vehicle"/> takes: its type's <see cref="SeatRole.Passenger"/>
        /// seats (<see cref="CarrierSeats"/>, from <c>Gen.Carrier</c>). A buffer with no declared seats (a test or hand-built world) keeps the
        /// list's full <see cref="PassengerBuffer.Capacity"/>; no buffer = 0.
        /// </summary>
        public static int CapacityOf(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle) || !Has<PassengerBuffer>(repo, vehicle)) return 0;
            return Has<CarrierSeats>(repo, vehicle) ? repo.GetComponentRO<CarrierSeats>(vehicle).PassengerSeats : PassengerBuffer.Capacity;
        }

        /// <summary>Boards <paramref name="passenger"/> onto <paramref name="vehicle"/>: the list, the tag, the stripped capabilities.
        /// Idempotent — a passenger already aboard is <see cref="EmbarkResult.AlreadyAboard"/> (its own side is made consistent).</summary>
        /// <remarks>⭐ <c>CE-2151</c> B-b (R-2009 B4) — a SEAT (<see cref="FreeSeat"/>): the first free one whose role is
        /// <paramref name="role"/>, else a free passenger seat (a passenger seat takes anyone, a crew seat only its role). The list is
        /// seat-indexed — leaving empties the seat in place.</remarks>
        public static EmbarkResult TryEmbark(EntityRepository repo, Entity passenger, Entity vehicle, SeatRole role = SeatRole.Passenger)
        {
            if (!repo.IsAlive(passenger) || !repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return EmbarkResult.Invalid;
            // ⭐ CE-3110 (R-272) — a UNIT is not a physical actor: it takes no seat and carries nobody (its members do the boarding).
            if (Has<Fdp.Core.CommandHierarchy.UnitEntity>(repo, passenger) || Has<Fdp.Core.CommandHierarchy.UnitEntity>(repo, vehicle)) return EmbarkResult.Invalid;
            int aboard = SlotOf(repo, passenger, vehicle);
            if (aboard >= 0) { Board(repo, passenger, vehicle, aboard); return EmbarkResult.AlreadyAboard; }
            int seat = FreeSeat(repo, vehicle, role);   // ⭐ CE-2151 — the type's seats, by role
            if (seat < 0) return EmbarkResult.Full;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            buffer.Passengers[seat] = passenger;
            buffer.Count++;
            Board(repo, passenger, vehicle, seat);
            return EmbarkResult.Applied;
        }

        /// <summary>
        /// <paramref name="passenger"/> leaves its vehicle (<paramref name="vehicle"/>, or the one its tag names when null): out of the
        /// list, the tag gone, the capabilities back, and standing BESIDE the hull at the seat it had (the CE-321 column). False when it
        /// was not aboard.
        /// </summary>
        public static bool Disembark(EntityRepository repo, Entity passenger, Entity vehicle = default)
        {
            if (!repo.IsAlive(passenger)) return false;
            if (vehicle.IsNull && Has<IsEmbarkedTag>(repo, passenger)) vehicle = repo.GetComponentRO<IsEmbarkedTag>(passenger).VehicleEntity;
            int slot = SlotOf(repo, passenger, vehicle);
            if (slot < 0) return false;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            var at = DismountPoint(repo, vehicle, slot, buffer.Count);
            buffer.Passengers[slot] = Entity.Null;   // ⭐ CE-2151 B-b — the seat empties in place (no compaction: a slot IS a seat)
            buffer.Count--;
            Leave(repo, passenger, at);
            return true;
        }

        /// <summary>Every live passenger leaves <paramref name="vehicle"/>, in a column beside the hull (CE-321); the dead stay where
        /// they are. Returns how many left.</summary>
        public static int EjectAll(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return 0;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            int left = 0;
            int aboardCount = buffer.Count;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)   // ⭐ CE-2151 B-b — every seat (seats may be empty mid-list)
            {
                var passenger = buffer.Passengers[i];
                if (passenger.IsNull) continue;
                buffer.Passengers[i] = Entity.Null;
                if (!repo.IsAlive(passenger)) continue;   // killed aboard — skipped, not an error
                Leave(repo, passenger, DismountPoint(repo, vehicle, i, aboardCount));
                left++;
            }
            buffer.Count = 0;
            return left;
        }

        /// <summary>
        /// ⭐ CE-321 — where seat <paramref name="index"/> of <paramref name="count"/> stands on leaving: a column along the vehicle's
        /// RIGHT side, <see cref="DismountSpacing"/> apart, <see cref="DismountClearance"/> clear of its collider, turned with its
        /// heading (forward = Transform(UnitX, rotation); X east, Y north). 🔴 It used to be a fixed WORLD offset (−4 m in Y): 0.5 m
        /// outside a 3.5 m APC's collider and behind it whatever its heading — their fire then killed their own APC.
        /// </summary>
        public static Vector3 DismountPoint(EntityRepository repo, Entity vehicle, int index, int count)
        {
            if (!Has<SimTransform>(repo, vehicle)) return Vector3.Zero;
            var tf = repo.GetComponentRO<SimTransform>(vehicle);
            var fwd3 = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var forward = new Vector2(fwd3.X, fwd3.Y);
            forward = forward.LengthSquared() > 1e-6f ? Vector2.Normalize(forward) : Vector2.UnitX;
            var right = new Vector2(forward.Y, -forward.X);
            float hull = HullRadius(repo, vehicle);
            var along = forward * ((index - (count - 1) / 2f) * DismountSpacing);
            var lateral = right * (hull + DismountClearance);
            return tf.Position + new Vector3(along.X + lateral.X, along.Y + lateral.Y, 0f);
        }

        /// <summary>
        /// ⭐ <c>CE-1056</c> I-2d — where a soldier walks to board <paramref name="vehicle"/>: the point a single dismount stands on
        /// (seat 0 of 1, beside the hull — <see cref="DismountPoint"/>), so it is walkable wherever the vehicle stands on walkable
        /// ground. ⛔ Never the vehicle's centre: inside the hull, off the navmesh, a move there fails for ever.
        /// </summary>
        public static Vector3 BoardingPoint(EntityRepository repo, Entity vehicle) => DismountPoint(repo, vehicle, 0, 1);

        /// <summary>
        /// ⭐ <c>CE-1056</c> I-2d — how close (to the vehicle's centre) a soldier must be to board: the boarding point's distance plus
        /// <see cref="BoardingSlack"/>, so <c>EmbarkExecutor</c>'s range test agrees with where the walk stops (its 3 m default is
        /// shorter than the boarding point of any hull over 0.5 m).
        /// </summary>
        public static float BoardingRange(EntityRepository repo, Entity vehicle) => HullRadius(repo, vehicle) + DismountClearance + BoardingSlack;

        /// <summary>The margin <see cref="BoardingRange"/> adds beyond the boarding point (m).</summary>
        public const float BoardingSlack = 1f;

        private static float HullRadius(EntityRepository repo, Entity vehicle)
            => repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>()
               && repo.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(vehicle)
                ? repo.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(vehicle).Radius
                : DefaultHullRadius;

        private static int SlotOf(EntityRepository repo, Entity passenger, Entity vehicle)
        {
            if (vehicle.IsNull || !repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return -1;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
            for (int i = 0; i < PassengerBuffer.Capacity; i++) if (!buffer.Passengers[i].IsNull && buffer.Passengers[i] == passenger) return i;
            return -1;
        }

        /// <summary>
        /// ⭐ THE passenger-side writer, boarding (I-2b): the tag naming its vehicle, CanMove/CanShoot off. Two callers — the deciding node at
        /// once (<see cref="TryEmbark"/>) and every node from the list it holds (<c>EmbarkedStateSystem</c>) — so no node writes it from a
        /// second rule. Idempotent.
        /// </summary>
        public static void Board(EntityRepository repo, Entity passenger, Entity vehicle, int seat)
        {
            if (Has<ActorCapabilityState>(repo, passenger))
                repo.GetComponentRW<ActorCapabilityState>(passenger).Capabilities &= ~StrippedAboard;
            // ⭐ I-2c (R-271): an OPEN cabin leaves the passenger exposed (seen, targeted, hit); enclosed hides it (PhysicalPresence).
            //   ⭐ CE-2151 B-b (R-2009 B6): with declared seats the SEAT decides (a gunner's open hatch, the cabin closed).
            repo.SetComponent(passenger, new IsEmbarkedTag { VehicleEntity = vehicle, Seat = (byte)seat, Exposed = (byte)(SeatExposed(repo, vehicle, seat) ? 1 : 0) });
        }

        /// <summary>⭐ <c>CE-2151</c> B-b — is seat <paramref name="seat"/> of <paramref name="vehicle"/> exposed: the type's per-seat mask
        /// (<see cref="CarrierSeats.ExposedMask"/>), else the whole cabin (<see cref="OpenPassengerCabin"/>).</summary>
        public static bool SeatExposed(EntityRepository repo, Entity vehicle, int seat)
            => Has<CarrierSeats>(repo, vehicle)
                ? (repo.GetComponentRO<CarrierSeats>(vehicle).ExposedMask & (1 << seat)) != 0
                : Has<OpenPassengerCabin>(repo, vehicle);

        /// <summary>
        /// ⭐ <c>CE-2151</c> B-b (R-2009 B4: "a Passenger seat takes anyone, a crew seat only its role") — the first free seat of
        /// <paramref name="vehicle"/> whose role is <paramref name="role"/>, else the first free PASSENGER seat, else −1. A plain boarding
        /// (<see cref="SeatRole.Passenger"/>, the default) therefore never takes a crew seat. The declared seats (<see cref="CarrierSeats"/>);
        /// a list with no declared seats (a hand-built world) has 8 passenger seats.
        /// </summary>
        public static int FreeSeat(EntityRepository repo, Entity vehicle, SeatRole role = SeatRole.Passenger)
        {
            if (!repo.IsAlive(vehicle) || !Has<PassengerBuffer>(repo, vehicle)) return -1;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
            bool declared = Has<CarrierSeats>(repo, vehicle);
            var seats = declared ? repo.GetComponentRO<CarrierSeats>(vehicle) : default;
            int n = declared ? System.Math.Min((int)seats.SeatCount, PassengerBuffer.Capacity) : PassengerBuffer.Capacity;
            int passengerSeat = -1;
            for (int i = 0; i < n; i++)
            {
                if (!buffer.Passengers[i].IsNull) continue;
                var seatRole = declared ? seats.Roles[i] : SeatRole.Passenger;
                if (seatRole == role) return i;
                if (passengerSeat < 0 && seatRole == SeatRole.Passenger) passengerSeat = i;
            }
            return passengerSeat;
        }

        /// <summary>
        /// ⭐ THE passenger-side writer, leaving (I-2b): the tag gone, CanMove/CanShoot back (not for the dead), and — ONLY on the node owning
        /// the passenger's position (<see cref="OwnsPosition"/>) — standing at <paramref name="at"/>: a write anywhere else is overwritten by
        /// the owner's replicated position.
        /// </summary>
        public static void Leave(EntityRepository repo, Entity passenger, Vector3 at)
        {
            if (Has<SimTransform>(repo, passenger) && OwnsPosition(repo, passenger)) repo.GetComponentRW<SimTransform>(passenger).Position = at;
            if (Has<ActorCapabilityState>(repo, passenger) && Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, passenger))
                repo.GetComponentRW<ActorCapabilityState>(passenger).Capabilities |= StrippedAboard;
            if (Has<IsEmbarkedTag>(repo, passenger)) repo.RemoveComponent<IsEmbarkedTag>(passenger);
        }

        private static bool Has<T>(EntityRepository repo, Entity e) where T : unmanaged
            => repo.IsComponentTypeRegistered<T>() && repo.HasComponent<T>(e);
    }
}
