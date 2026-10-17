using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Motion;
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
        /// list's <see cref="PassengerBuffer.UndeclaredSeats"/>; no buffer = 0.
        /// </summary>
        public static int CapacityOf(EntityRepository repo, Entity vehicle)
        {
            if (!repo.IsAlive(vehicle) || !Has<PassengerBuffer>(repo, vehicle)) return 0;
            return Has<CarrierSeats>(repo, vehicle) ? repo.GetComponentRO<CarrierSeats>(vehicle).PassengerSeats : PassengerBuffer.UndeclaredSeats;
        }

        /// <summary>Boards <paramref name="passenger"/> onto <paramref name="vehicle"/>: the list, the tag, the stripped capabilities.
        /// Idempotent — a passenger already aboard is <see cref="EmbarkResult.AlreadyAboard"/> (its own side is made consistent).</summary>
        /// <remarks>⭐ <c>CE-2151</c> B-b (R-2009 B4) — a SEAT (<see cref="FreeSeat"/>): the first free one whose role is
        /// <paramref name="role"/>, else a free passenger seat (a passenger seat takes anyone, a crew seat only its role). The list is
        /// seat-indexed — leaving empties the seat in place.</remarks>
        /// <para>⭐ <c>CE-2152</c> C-a (R-2009 C3, R-2011 C5) — onto a PERSON carrier (<see cref="PersonCarrier"/>) only an INCAPACITATED
        /// passenger boards, and only while the carrier is in the fight himself; else <see cref="EmbarkResult.Invalid"/>. Its
        /// <paramref name="mode"/> is <see cref="CarryMode.Carried"/> or <see cref="CarryMode.Dragged"/> (a seated request is carried); onto a
        /// vehicle it is always <see cref="CarryMode.Seated"/>.</para>
        public static EmbarkResult TryEmbark(EntityRepository repo, Entity passenger, Entity vehicle, SeatRole role = SeatRole.Passenger,
            CarryMode mode = CarryMode.Seated, Entity assistant = default)
        {
            var result = Check(repo, passenger, vehicle, role, ref mode, assistant, out int seat);
            if (result == EmbarkResult.AlreadyAboard) { Board(repo, passenger, vehicle, seat); return result; }
            if (result != EmbarkResult.Applied) return result;
            return Seat(repo, passenger, vehicle, seat, mode, assistant);
        }

        /// <summary>
        /// ⭐ <c>CE-1152</c> (DESIGN_Entity_Control §8h U-5/U-6) — <see cref="TryEmbark"/>'s checks WITHOUT seating anyone: what it would answer
        /// (<see cref="EmbarkResult.Applied"/> = it would board). ⭐ One rule, no copy: <see cref="TryEmbark"/> runs exactly this check and
        /// only then seats. Reads only replicated components (the passenger list, the seats, health and the injury band, the weapon
        /// system's kinds), so a station predicting the use key and the brain deciding it agree.
        /// </summary>
        public static EmbarkResult CanEmbark(EntityRepository repo, Entity passenger, Entity vehicle, SeatRole role = SeatRole.Passenger,
            CarryMode mode = CarryMode.Seated, Entity assistant = default)
            => Check(repo, passenger, vehicle, role, ref mode, assistant, out _);

        /// <summary>The one boarding rule: the result, the seat it would take (or the one it already holds) and the carry mode.</summary>
        private static EmbarkResult Check(EntityRepository repo, Entity passenger, Entity vehicle, SeatRole role,
            ref CarryMode mode, Entity assistant, out int seat)
        {
            seat = -1;
            if (!repo.IsAlive(passenger) || !repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return EmbarkResult.Invalid;
            // ⭐ CE-3110 (R-272) — a UNIT is not a physical actor: it takes no seat and carries nobody (its members do the boarding).
            if (Has<Fdp.Core.CommandHierarchy.UnitEntity>(repo, passenger) || Has<Fdp.Core.CommandHierarchy.UnitEntity>(repo, vehicle)) return EmbarkResult.Invalid;
            int aboard = SlotOf(repo, passenger, vehicle);
            if (aboard >= 0) { seat = aboard; return EmbarkResult.AlreadyAboard; }
            // ⭐⭐ Team carry T-a (R-2017 T4; DESIGN_Entity_Interactions §7.5.3) — a STRETCHER and a TWO-MAN weapon need a named, fit second
            //   man; every other load refuses one. Only a person carries a team load.
            bool team = IsPersonCarrier(repo, vehicle) && (mode == CarryMode.Stretcher || IsTwoManWeapon(repo, passenger));
            if (team ? !FitAssistant(repo, vehicle, passenger, assistant) : !assistant.IsNull) return EmbarkResult.Invalid;
            if (mode == CarryMode.Stretcher && (!IsPersonCarrier(repo, vehicle) || IsWeaponSystem(repo, passenger))) return EmbarkResult.Invalid;
            // ⭐⭐ Weapon systems WS-2 (W-B, R-2016; DESIGN_Weapon_Systems §6) — a WEAPON SYSTEM boards a Station seat whose Accepts
            //   shares one of its mount kinds — never a seat for a man; onto a PERSON only a portable one, and only while he is in the fight.
            if (IsWeaponSystem(repo, passenger))
            {
                var kinds = repo.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemParams>(passenger);
                if (IsPersonCarrier(repo, vehicle)
                    && (kinds.Portable == (byte)Fdp.Toolkit.Tkb.Domain.WeaponPortability.Static || !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, vehicle)))
                    return EmbarkResult.Invalid;
                int station = FreeStation(repo, vehicle, kinds.MountKinds);
                if (station == -2) return EmbarkResult.Invalid;   // no station on this type accepts it
                if (station < 0) return EmbarkResult.Full;
                seat = station;
                mode = IsPersonCarrier(repo, vehicle) ? CarryMode.Carried : CarryMode.Seated;
                return EmbarkResult.Applied;
            }
            if (role == SeatRole.Station) return EmbarkResult.Invalid;   // ⭐ WS-2 — a station holds a weapon, never a man
            if (IsPersonCarrier(repo, vehicle))
            {
                // ⭐ CE-2152 (R-2009 C3) — a person carries only a man DOWN, and only while he is in the fight himself.
                if (!Fdp.Toolkit.Combat.Injury.IsIncapacitated(repo, passenger) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, vehicle))
                    return EmbarkResult.Invalid;
                if (mode == CarryMode.Seated) mode = CarryMode.Carried;
            }
            else mode = CarryMode.Seated;
            seat = FreeSeat(repo, vehicle, role);   // ⭐ CE-2151 — the type's seats, by role
            return seat < 0 ? EmbarkResult.Full : EmbarkResult.Applied;
        }

        /// <summary>The reach (m) within which a team load's second man must stand by the carrier (R-2017 T4).</summary>
        public const float AssistantReach = 2f;

        /// <summary>⭐ T-a — is <paramref name="e"/> a weapon system two men carry (<c>Portable = TwoMan</c>)?</summary>
        public static bool IsTwoManWeapon(EntityRepository repo, Entity e)
            => IsWeaponSystem(repo, e)
               && repo.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemParams>(e).Portable == (byte)Fdp.Toolkit.Tkb.Domain.WeaponPortability.TwoMan;

        /// <summary>
        /// ⭐⭐ Team carry T-a (R-2017 T4) — may <paramref name="assistant"/> take the rear of <paramref name="carrier"/>'s team load? Alive and
        /// in the fight; neither the carrier nor the load; carrying nothing and aboard nothing and assisting nobody; the carrier's side (where
        /// both say); within <see cref="AssistantReach"/> of him.
        /// </summary>
        public static bool FitAssistant(EntityRepository repo, Entity carrier, Entity load, Entity assistant)
        {
            if (assistant.IsNull || !repo.IsAlive(assistant) || assistant == carrier || assistant == load) return false;
            if (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, assistant)) return false;
            if (Has<IsEmbarkedTag>(repo, assistant) || Has<IsAssistingTag>(repo, assistant) || !LoadOf(repo, assistant).IsNull) return false;
            if (Has<Fdp.Core.EntityInfo>(repo, assistant) && Has<Fdp.Core.EntityInfo>(repo, carrier)
                && !repo.GetComponentRO<Fdp.Core.EntityInfo>(assistant).ForceId.Equals(repo.GetComponentRO<Fdp.Core.EntityInfo>(carrier).ForceId))
                return false;
            if (Has<SimTransform>(repo, assistant) && Has<SimTransform>(repo, carrier)
                && Vector3.Distance(repo.GetComponentRO<SimTransform>(assistant).Position, repo.GetComponentRO<SimTransform>(carrier).Position) > AssistantReach)
                return false;
            return true;
        }

        /// <summary>⭐ T-a — <paramref name="carrier"/>'s second man, or null.</summary>
        public static Entity AssistantOf(EntityRepository repo, Entity carrier)
            => Has<PassengerBuffer>(repo, carrier) ? repo.GetComponentRO<PassengerBuffer>(carrier).Assistant : Entity.Null;

        /// <summary>
        /// ⭐ T-a — THE assistant-side writer (the <see cref="Board"/> of the second man): the tag naming his carrier, his fire and his own
        /// movement off (T5: hands full; T3: he is placed at the rear handle). Two callers, as for <see cref="Board"/>: the deciding node at
        /// once and every node from the list. Idempotent.
        /// </summary>
        public static void Assist(EntityRepository repo, Entity assistant, Entity carrier)
        {
            if (!repo.IsAlive(assistant) || !repo.IsComponentTypeRegistered<IsAssistingTag>()) return;
            if (Has<ActorCapabilityState>(repo, assistant)) repo.GetComponentRW<ActorCapabilityState>(assistant).Capabilities &= ~StrippedAboard;
            repo.SetComponent(assistant, new IsAssistingTag { Carrier = carrier });
        }

        /// <summary>⭐ T-a — the second man lets go: the tag gone, his fire and movement back while he is in the fight (T5, T6).</summary>
        public static void ReleaseAssistant(EntityRepository repo, Entity assistant)
        {
            if (!repo.IsAlive(assistant)) return;
            if (Has<ActorCapabilityState>(repo, assistant) && Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, assistant))
                repo.GetComponentRW<ActorCapabilityState>(assistant).Capabilities |= StrippedAboard;
            if (Has<IsAssistingTag>(repo, assistant)) repo.RemoveComponent<IsAssistingTag>(assistant);
        }

        /// <summary>⭐ T-a — the carrier's list drops its second man (the load is gone or put down); he is released at once here and on
        /// every other node from the list.</summary>
        private static void DropAssistant(EntityRepository repo, Entity carrier)
        {
            if (!Has<PassengerBuffer>(repo, carrier)) return;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(carrier);
            var assistant = buffer.Assistant;
            if (assistant.IsNull) return;
            buffer.Assistant = Entity.Null;
            ReleaseAssistant(repo, assistant);
        }

        /// <summary>
        /// ⭐⭐ WS-4 (W-I) — <paramref name="passenger"/> boards seat <paramref name="seat"/> of <paramref name="vehicle"/> exactly: a weapon
        /// system only a station that accepts it, a man only a seat that is no station. ⭐ The preset path (<c>SeatPresetSystem</c>) and a
        /// scenario load (the saved seat) — the seat is the TYPE's, not the first free one of a role. Same results as
        /// <see cref="TryEmbark"/>; <see cref="EmbarkResult.Full"/> when that seat is taken.
        /// </summary>
        public static EmbarkResult TryEmbarkAt(EntityRepository repo, Entity passenger, Entity vehicle, int seat)
        {
            if (!repo.IsAlive(passenger) || !repo.IsAlive(vehicle) || !repo.HasComponent<PassengerBuffer>(vehicle)) return EmbarkResult.Invalid;
            if (Has<Fdp.Core.CommandHierarchy.UnitEntity>(repo, passenger) || Has<Fdp.Core.CommandHierarchy.UnitEntity>(repo, vehicle)) return EmbarkResult.Invalid;
            int aboard = SlotOf(repo, passenger, vehicle);
            if (aboard >= 0) { Board(repo, passenger, vehicle, aboard); return EmbarkResult.AlreadyAboard; }
            int seats = Has<CarrierSeats>(repo, vehicle) ? System.Math.Min((int)repo.GetComponentRO<CarrierSeats>(vehicle).SeatCount, PassengerBuffer.Capacity)
                                                         : PassengerBuffer.UndeclaredSeats;
            if (seat < 0 || seat >= seats) return EmbarkResult.Invalid;
            bool station = Has<CarrierStations>(repo, vehicle) && repo.GetComponentRO<CarrierStations>(vehicle).IsStation(seat);
            if (IsWeaponSystem(repo, passenger))
            {
                if (!station || (repo.GetComponentRO<CarrierStations>(vehicle).Seats[seat].Accepts
                                 & repo.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemParams>(passenger).MountKinds) == 0) return EmbarkResult.Invalid;
            }
            else if (station) return EmbarkResult.Invalid;
            if (!repo.GetComponentRO<PassengerBuffer>(vehicle).Passengers[seat].IsNull) return EmbarkResult.Full;
            return Seat(repo, passenger, vehicle, seat, IsPersonCarrier(repo, vehicle) ? CarryMode.Carried : CarryMode.Seated);
        }

        private static EmbarkResult Seat(EntityRepository repo, Entity passenger, Entity vehicle, int seat, CarryMode mode, Entity assistant = default)
        {
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            buffer.Passengers[seat] = passenger;
            buffer.SetMode(seat, mode);   // ⭐ CE-2152 (R-2011 C5) — in the list, so every node derives the tag's mode from it
            buffer.Count++;
            if (!assistant.IsNull) buffer.Assistant = assistant;   // ⭐ T-a (T1) — in the list too
            Board(repo, passenger, vehicle, seat);
            if (!assistant.IsNull) Assist(repo, assistant, vehicle);
            return EmbarkResult.Applied;
        }

        /// <summary>⭐ WS-2 — is <paramref name="e"/> a weapon system (its type declares <c>Gen.WeaponSystem</c>)?</summary>
        public static bool IsWeaponSystem(EntityRepository repo, Entity e) => Has<Fdp.Toolkit.Combat.WeaponSystemParams>(repo, e);

        /// <summary>
        /// ⭐⭐ Weapon systems WS-2 (W-B) — the first free <see cref="SeatRole.Station"/> of <paramref name="vehicle"/> whose <c>Accepts</c>
        /// shares a kind with <paramref name="mountKinds"/> (a weapon's <c>MountKinds</c>); −1 when such a station exists but is taken,
        /// −2 when none on the type accepts it.
        /// </summary>
        public static int FreeStation(EntityRepository repo, Entity vehicle, byte mountKinds)
        {
            if (!repo.IsAlive(vehicle) || !Has<PassengerBuffer>(repo, vehicle) || !Has<CarrierStations>(repo, vehicle)) return -2;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
            ref readonly var stations = ref repo.GetComponentRO<CarrierStations>(vehicle);
            bool any = false, anyHidden = false;
            int hidden = -1;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                if (!stations.IsStation(i) || (stations.Seats[i].Accepts & mountKinds) == 0) continue;
                // ⭐ CE-3233 (B-11, §7.13 ST-3/ST-9) — a hidden station is filled only when asked (CarryStations.Move) WHILE an open one on the
                //   type takes the thing (a man's hands, his back); where only a hidden one does (a car's boot takes cargo) it is the station.
                if (stations.IsConcealed(i)) { anyHidden = true; if (hidden < 0 && buffer.Passengers[i].IsNull) hidden = i; continue; }
                any = true;
                if (buffer.Passengers[i].IsNull) return i;
            }
            if (any) return -1;
            return anyHidden ? hidden : -2;
        }

        /// <summary>
        /// <paramref name="passenger"/> leaves its vehicle (<paramref name="vehicle"/>, or the one its tag names when null): out of the
        /// list, the tag gone, the capabilities back, and standing BESIDE the hull at the seat it had (the CE-321 column). False when it
        /// was not aboard.
        /// </summary>
        /// <remarks>⭐ Airborne insertion B-4 (D-8) — <paramref name="exit"/> other than <see cref="ExitKind.StepOut"/> leaves at the
        /// carrier's rope or DOOR (<paramref name="anchorIndex"/>) at its height, never beside the hull, and starts his descent
        /// (<see cref="ExitIntoDescent"/>).</remarks>
        public static bool Disembark(EntityRepository repo, Entity passenger, Entity vehicle = default,
                                     ExitKind exit = ExitKind.StepOut, byte anchorIndex = 0, Vector3 target = default)
        {
            if (!repo.IsAlive(passenger)) return false;
            if (vehicle.IsNull && Has<IsEmbarkedTag>(repo, passenger)) vehicle = repo.GetComponentRO<IsEmbarkedTag>(passenger).VehicleEntity;
            int slot = SlotOf(repo, passenger, vehicle);
            if (slot < 0) return false;
            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
            var at = exit != ExitKind.StepOut && DescentRules.DoorPoint(repo, vehicle, anchorIndex, out var door)
                ? door : LeavePoint(repo, vehicle, passenger, slot, buffer.Count);
            buffer.Passengers[slot] = Entity.Null;   // ⭐ CE-2151 B-b — the seat empties in place (no compaction: a slot IS a seat)
            buffer.SetMode(slot, CarryMode.Seated);
            buffer.Count--;
            Leave(repo, passenger, at);
            if (exit != ExitKind.StepOut) ExitIntoDescent(repo, passenger, vehicle, exit, anchorIndex, target);
            if (IsPersonCarrier(repo, vehicle)) DropAssistant(repo, vehicle);   // ⭐ T-a — a person carries one load: the team ends with it
            ReleaseCarrier(repo, vehicle);
            return true;
        }

        /// <summary>
        /// ⭐ Airborne insertion B-4 (§1, the CE-3059 resolution) — <paramref name="passenger"/>, just out of <paramref name="vehicle"/>'s rope
        /// or door, starts coming down. The <see cref="Descent"/> is the POSITION owner's state (Muscle group), and this runs on the
        /// CARRIER's owner (R-224): when they are one node it starts here (<see cref="Descents.Start"/>); otherwise a
        /// <see cref="StartDescentCommand"/> carries it there (the interaction transport, target = the passenger) — the same split CE-3059
        /// measured for a dismount, closed for a descent by sending the decision to the node whose writes stick.
        /// </summary>
        /// <remarks>⭐ H-1 (design §6.5 D-18) — <paramref name="target"/> is a jumper's landing point (zero = none), his
        /// <c>Descent.Target</c>.</remarks>
        public static void ExitIntoDescent(EntityRepository repo, Entity passenger, Entity vehicle, ExitKind exit, byte anchorIndex,
                                           Vector3 target = default)
        {
            if (OwnsPosition(repo, passenger)) { Descents.Start(repo, passenger, vehicle, exit, anchorIndex, target); return; }
            if (repo.Bus.IsRegistered<StartDescentCommand>())
                repo.Bus.Publish(new StartDescentCommand { Passenger = passenger, Anchor = vehicle, Exit = exit, AnchorIndex = anchorIndex, Target = target });
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
                Leave(repo, passenger, LeavePoint(repo, vehicle, passenger, i, aboardCount));
                left++;
            }
            buffer.Count = 0;
            buffer.Modes = 0;
            DropAssistant(repo, vehicle);   // ⭐ T-a (T6) — the load put down, the link dropped
            ReleaseCarrier(repo, vehicle);
            return left;
        }

        /// <summary>
        /// ⭐ <c>CE-2152</c> C-b (C-b1, docs/DESIGN_Entity_Interactions.md §7.4.10) — where <paramref name="passenger"/> lands on leaving
        /// <paramref name="vehicle"/>: a PERSON carrier lays the casualty where he is held (his own position — C9 "dropped where it lies");
        /// a vehicle stands him beside the hull (<see cref="DismountPoint"/>). ⛔ C-a used the hull column for both, which jumped a
        /// dragged man sideways to the carrier's right.
        /// </summary>
        public static Vector3 LeavePoint(EntityRepository repo, Entity vehicle, Entity passenger, int index, int count)
            => IsPersonCarrier(repo, vehicle) && Has<SimTransform>(repo, passenger)
                ? repo.GetComponentRO<SimTransform>(passenger).Position
                : DismountPoint(repo, vehicle, index, count);

        /// <summary>⭐ <c>CE-2152</c> C-b — the first person <paramref name="carrier"/> carries, or <see cref="Entity.Null"/>.</summary>
        public static Entity LoadOf(EntityRepository repo, Entity carrier)
        {
            if (!repo.IsAlive(carrier) || !Has<PassengerBuffer>(repo, carrier)) return Entity.Null;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(carrier);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (!buffer.Passengers[i].IsNull) return buffer.Passengers[i];
            return Entity.Null;
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
                repo.GetComponentRW<ActorCapabilityState>(passenger).Capabilities &= ~StrippedOn(repo, vehicle, seat);
            // ⭐ CE-2152 (R-2011 C7) — a person carrying or dragging a casualty has his hands full: no fire (back on ReleaseCarrier).
            //   ⭐ WS-2 — a carried WEAPON does not: the soldier still fights (he may even fire it, FireWhileCarried).
            //   ⭐ T-a (R-2017 T5) — a TEAM load (a two-man weapon) fills his hands as a casualty does.
            if (IsPersonCarrier(repo, vehicle) && (!IsWeaponSystem(repo, passenger) || !AssistantOf(repo, vehicle).IsNull)
                && Has<ActorCapabilityState>(repo, vehicle))
                repo.GetComponentRW<ActorCapabilityState>(vehicle).Capabilities &= ~ActorCapabilities.CanShoot;
            var mode = Has<PassengerBuffer>(repo, vehicle) ? repo.GetComponentRO<PassengerBuffer>(vehicle).ModeOf(seat) : CarryMode.Seated;
            // ⭐ I-2c (R-271): an OPEN cabin leaves the passenger exposed (seen, targeted, hit); enclosed hides it (PhysicalPresence).
            //   ⭐ CE-2151 B-b (R-2009 B6): with declared seats the SEAT decides (a gunner's open hatch, the cabin closed).
            repo.SetComponent(passenger, new IsEmbarkedTag
            {
                VehicleEntity = vehicle, Seat = (byte)seat, Exposed = (byte)(SeatExposed(repo, vehicle, seat) ? 1 : 0), Mode = mode,
            });
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> (📄 docs/designs/weapons/DESIGN_Weapon_Systems.md WS-3) — what seat <paramref name="seat"/> of
        /// <paramref name="vehicle"/> takes away: <see cref="StrippedAboard"/>, ⭐ except a WEAPON SYSTEM's own <see cref="SeatRole.Gunner"/>
        /// seat — its man is the weapon's controller (<c>WeaponSystems.ControllerOf</c>) and fires it as the weapon through his own
        /// <see cref="WeaponChannel"/>, so he keeps <see cref="ActorCapabilities.CanShoot"/> (he still cannot walk off with it). A vehicle's
        /// crew seat is unchanged: there the HOST fires and the man fires nothing (W-I). 📐 Measured live (IF-4 ①): with CanShoot stripped the
        /// weapon dispatcher failed every gunner's FireAtPoint before its executor ran — the section never fired.
        /// </summary>
        public static ActorCapabilities StrippedOn(EntityRepository repo, Entity vehicle, int seat)
            => IsWeaponSystem(repo, vehicle) && Has<CarrierSeats>(repo, vehicle) && seat >= 0 && seat < PassengerBuffer.Capacity
               && repo.GetComponentRO<CarrierSeats>(vehicle).Roles[seat] == SeatRole.Gunner
                ? ActorCapabilities.CanMove
                : StrippedAboard;

        /// <summary>⭐ <c>CE-2152</c> — is <paramref name="carrier"/> a PERSON who carries or drags (its type declares <c>Gen.Carrier.Person</c>)?</summary>
        public static bool IsPersonCarrier(EntityRepository repo, Entity carrier) => Has<PersonCarrier>(repo, carrier);

        /// <summary>
        /// ⭐ <c>CE-2152</c> (R-2011 C7) — a person carrier whose hands are free again gets his fire back: nobody left in his list, still in the
        /// fight, and not himself aboard something. Called wherever a list empties (the deciding node and every node deriving from the list).
        /// ⚠ It grants <c>CanShoot</c>; the person-carrier profile is declared only on soldier types, which have it.
        /// </summary>
        public static void ReleaseCarrier(EntityRepository repo, Entity carrier)
        {
            if (!IsPersonCarrier(repo, carrier) || !Has<ActorCapabilityState>(repo, carrier) || !Has<PassengerBuffer>(repo, carrier)) return;
            if (Has<IsEmbarkedTag>(repo, carrier)) return;
            // ⭐ WS-2 — only a CASUALTY fills his hands: a weapon he carries (his carry station) does not keep his fire off
            ref readonly var list = ref repo.GetComponentRO<PassengerBuffer>(carrier);
            if (!list.Assistant.IsNull) return;   // ⭐ T-a — a team load still in his hands
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (!list.Passengers[i].IsNull && !IsWeaponSystem(repo, list.Passengers[i])) return;
            if (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, carrier)) return;
            repo.GetComponentRW<ActorCapabilityState>(carrier).Capabilities |= ActorCapabilities.CanShoot;
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
        /// a list with no declared seats (a hand-built world) has <see cref="PassengerBuffer.UndeclaredSeats"/> passenger seats.
        /// </summary>
        public static int FreeSeat(EntityRepository repo, Entity vehicle, SeatRole role = SeatRole.Passenger)
        {
            if (!repo.IsAlive(vehicle) || !Has<PassengerBuffer>(repo, vehicle)) return -1;
            ref readonly var buffer = ref repo.GetComponentRO<PassengerBuffer>(vehicle);
            bool declared = Has<CarrierSeats>(repo, vehicle);
            var seats = declared ? repo.GetComponentRO<CarrierSeats>(vehicle) : default;
            int n = declared ? System.Math.Min((int)seats.SeatCount, PassengerBuffer.Capacity) : PassengerBuffer.UndeclaredSeats;
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
        /// ⭐ THE passenger-side writer, leaving (I-2b): the tag gone, CanMove/CanShoot back (not for the dead — ⭐ CE-2155 nor for the
        /// incapacitated, R-2011 I6: a casualty set down stays down), and — ONLY on the node owning
        /// the passenger's position (<see cref="OwnsPosition"/>) — standing at <paramref name="at"/>: a write anywhere else is overwritten by
        /// the owner's replicated position.
        /// </summary>
        public static void Leave(EntityRepository repo, Entity passenger, Vector3 at)
        {
            if (Has<SimTransform>(repo, passenger) && OwnsPosition(repo, passenger)) repo.GetComponentRW<SimTransform>(passenger).Position = at;
            if (Has<ActorCapabilityState>(repo, passenger) && Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, passenger))
                repo.GetComponentRW<ActorCapabilityState>(passenger).Capabilities |= StrippedAboard;
            if (Has<IsEmbarkedTag>(repo, passenger)) repo.RemoveComponent<IsEmbarkedTag>(passenger);
        }

        private static bool Has<T>(EntityRepository repo, Entity e) where T : unmanaged
            => repo.IsComponentTypeRegistered<T>() && repo.HasComponent<T>(e);
    }
}
