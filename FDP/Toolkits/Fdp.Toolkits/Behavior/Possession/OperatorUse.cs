using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Behavior.Possession
{
    /// <summary>⭐ <c>CE-1152</c> (DESIGN_Entity_Control §8h U-1…U-7) — what a use press can mean. One value per verb the HUD names.</summary>
    public enum UseVerbKind : byte
    {
        /// <summary>No verb (IDL scopes enum members per file, so each enum's zero has its own name).</summary>
        NoVerb = 0,
        OpenDoor, CloseDoor, LockDoor, UnlockDoor, BreachDoor,
        /// <summary>Pick a man who is down up by dragging him (<see cref="CarryMode.Dragged"/>).</summary>
        Drag,
        /// <summary>Pick a man who is down up over the shoulder (<see cref="CarryMode.Carried"/>).</summary>
        Carry,
        /// <summary>Lay down the man this one carries.</summary>
        PutDown,
        /// <summary>Board a vehicle's driver seat (an aircraft's: <see cref="Pilot"/>).</summary>
        Drive,
        Pilot,
        /// <summary>Board a vehicle as a passenger (its driver seat is taken, O-4).</summary>
        Board,
        /// <summary>Take a standalone weapon's Gunner seat (U-5).</summary>
        Man,
        /// <summary>Leave the vehicle this one is aboard.</summary>
        GetOut,
        /// <summary>⭐ <c>CE-1207</c> (§8m PK-2) — take a lying PORTABLE weapon into his first open station (Hands, then Back).</summary>
        Take,
        /// <summary>⭐ <c>CE-1207</c> (§8m PK-3, PK-4) — pick up a lying stone / round into his hands (<see cref="Fdp.Toolkit.Combat.RoundPickUp"/>).</summary>
        PickUpRound,
        /// <summary>⭐ <c>CE-1207</c> (§8m PK-6) — put down a weapon he carries in a station (hold list only).</summary>
        Drop,
    }

    /// <summary>
    /// ⭐ <c>CE-1152</c> — why a use action is not available, or (in <see cref="OperatorUseStatus"/>) why the brain refused it. It comes from
    /// the RULE, never from an executor (executors only say <c>Failure</c>; a failure after the action started is <see cref="Failed"/>).
    /// </summary>
    public enum UseReason : byte
    {
        /// <summary>Nothing stands in the way.</summary>
        Ok = 0,
        /// <summary>The target is farther than the action reaches.</summary>
        OutOfReach,
        /// <summary>The door is locked (open refused).</summary>
        Locked,
        /// <summary>The door refuses the verb in its state (a destroyed door).</summary>
        DoorRefuses,
        /// <summary>The man is not down — only a man who is down can be dragged or carried (C3).</summary>
        NotDown,
        /// <summary>No free seat of the role.</summary>
        SeatTaken,
        /// <summary>The actor is not fit to do it (down himself).</summary>
        Unfit,
        /// <summary>The actor already carries someone.</summary>
        Carrying,
        /// <summary>Nothing on the target answers a use press.</summary>
        NothingToDo,
        /// <summary>The action started and its executor failed.</summary>
        Failed,
    }

    /// <summary>⭐ <c>CE-1153</c> — the brain's answer to a use press.</summary>
    public enum UseOutcome : byte { NoOutcome = 0, Running = 1, Done = 2, Refused = 3 }

    /// <summary>
    /// ⭐ <c>CE-1152</c> — one thing a use press can do to a target: its verb, the HUD label, the interaction action id it starts
    /// (<see cref="BehaviorConstants"/>: doors 4–8, pick up 9, put down 10, embark 1, disembark 2), whether it can be done now, and
    /// why not. The rest is what <see cref="OperatorUseActions.WriteParams"/> packs into the interaction channel.
    /// </summary>
    public readonly record struct UseAction(
        UseVerbKind Verb, string Label, ushort ActionId, bool Available, UseReason Reason,
        Entity Target, float Reach, CarryMode Mode = CarryMode.Seated, SeatRole Role = SeatRole.Passenger, bool HoldOnly = false)
    {
        // ⭐ CE-1207 (§8m PK-6) — HoldOnly: offered on the hold list only, never sent by a tap (Drop must never be a stray tap).
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1153</c> (DESIGN_Entity_Control §8h.6) — the brain's verdict on the operator's last use press, on the possessed unit:
    /// written by the operator behaviour (behaviors lane, <c>CE-1154</c>), Brain group, replicated as <c>dtOperatorUseStatus</c> so the
    /// station's HUD — on another node — shows it when <see cref="UseSeq"/> is its own last press. STATE, not an event: a lost sample
    /// cannot leave the HUD stale.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.OperatorUseStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct OperatorUseStatus
    {
        /// <summary>The use press this answers (<see cref="OperatorInput.UseSeq"/>).</summary>
        public byte UseSeq;
        /// <summary>The interaction action started (0 = none).</summary>
        public ushort ActionId;
        public UseOutcome Outcome;
        public UseReason Reason;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1152</c> (DESIGN_Entity_Control §8h.2, U-1…U-7, §8h.6 ①) — THE ONE RULE for the use key: what <paramref name="actor"/> can do
    /// to <c>target</c>. The STATION calls it to draw the prompt (greyed past reach, locked, seat taken, not down) and the BRAIN calls it to
    /// decide; ⛔ so it reads ONLY replicated components — transforms, <see cref="DoorState"/>, health and the injury band,
    /// <see cref="PassengerBuffer"/>, <see cref="CarrierSeats"/>, the weapon system's params, <see cref="IsEmbarkedTag"/>,
    /// <see cref="Fdp.Toolkit.Motion.Locomotion"/> — never <see cref="InteractionChannel"/> (brain-local). The first action is the primary
    /// (<c>UseVerb</c> 0); the rest are the hold-<c>E</c> list.
    /// </summary>
    public static class OperatorUseActions
    {
        /// <summary>How far a man reaches a vehicle's seat (m, from its centre): the default hull, the dismount gap and a slack. ⚠ A
        /// constant, not the hull's own radius: <c>PhysicsCollider</c> is not on every node (the CGF brain has none), and the station and
        /// the brain must agree.</summary>
        public const float VehicleReach = Embarkation.DefaultHullRadius + Embarkation.DismountClearance + Embarkation.BoardingSlack;

        /// <summary>The most actions one target offers.</summary>
        public const int MaxActions = 8;

        /// <summary>The actions, primary first (allocates; the HUD's per-frame path uses the span overload).</summary>
        public static UseAction[] For(EntityRepository world, Entity actor, Entity target)
        {
            Span<UseAction> buffer = new UseAction[MaxActions];
            int n = For(world, actor, target, buffer);
            return buffer.Slice(0, n).ToArray();
        }

        /// <summary>Writes the actions into <paramref name="into"/>, primary first; returns how many.</summary>
        public static int For(EntityRepository world, Entity actor, Entity target, Span<UseAction> into)
        {
            int n = 0;
            if (!world.IsAlive(actor)) return 0;

            // aboard: the one thing to do is get out (U-4: E in the vehicle reverses it)
            if (Has<IsEmbarkedTag>(world, actor))
            {
                into[n++] = new UseAction(UseVerbKind.GetOut, "Get out", BehaviorConstants.ActionIdDisembark, true, UseReason.Ok,
                    world.GetComponentRO<IsEmbarkedTag>(actor).VehicleEntity, 0f);
                return n;
            }
            // carrying a man: E again puts him down (U-6), whatever the crosshair rests on — ⭐ CE-1207 (PK-5) a BODY, never a weapon in a station
            var load = CarriedBody(world, actor);
            if (!load.IsNull)
                into[n++] = new UseAction(UseVerbKind.PutDown, "Put down", BehaviorConstants.ActionIdPutDown, true, UseReason.Ok, load, 0f);

            n = OnTarget(world, actor, target, load, into, n);
            return Drops(world, actor, into, n);   // ⭐ CE-1207 (PK-6) — the hold list's last lines
        }

        private static int OnTarget(EntityRepository world, Entity actor, Entity target, Entity load, Span<UseAction> into, int n)
        {
            if (target.IsNull || target == actor || !world.IsAlive(target)) return n;
            if (Has<DoorState>(world, target)) return Doors(world, actor, target, into, n);
            if (Embarkation.IsWeaponSystem(world, target)) return Weapon(world, actor, target, into, n);   // a weapon is never Drag / Carry
            if (Has<PassengerBuffer>(world, target) && !Embarkation.IsPersonCarrier(world, target)) return Vehicle(world, actor, target, into, n);
            if (Has<Health>(world, target) && load.IsNull) return Body(world, actor, target, into, n);
            if (Fdp.Toolkit.Combat.RoundPickUp.IsLying(world, target) && Fdp.Toolkit.Combat.RoundPickUp.IsHandItem(world, target))
                return Round(world, actor, target, into, n);
            return n;
        }

        /// <summary>
        /// ⭐ <c>CE-1207</c> (§8m PK-5) — the BODY <paramref name="carrier"/> carries (a man dragged, carried, on a stretcher): the first passenger in
        /// a seat that is not a carry STATION. ⛔ <see cref="Embarkation.LoadOf"/> counts a weapon in his hands too, which would offer "Put down"
        /// first and refuse Drag / Carry to a man holding a rifle he took. Only the use path and <see cref="PutDownExecutor"/> read it — the AI's
        /// other <c>LoadOf</c> callers are left as they are (§8m.2).
        /// </summary>
        public static Entity CarriedBody(EntityRepository world, Entity carrier)
        {
            if (!world.IsAlive(carrier) || !Embarkation.IsPersonCarrier(world, carrier) || !Has<PassengerBuffer>(world, carrier)) return Entity.Null;
            ref readonly var buffer = ref world.GetComponentRO<PassengerBuffer>(carrier);
            bool stations = Has<CarrierStations>(world, carrier);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                if (buffer.Passengers[i].IsNull) continue;
                if (stations && world.GetComponentRO<CarrierStations>(carrier).IsStation(i)) continue;
                return buffer.Passengers[i];
            }
            return Entity.Null;
        }

        private static int Round(EntityRepository world, Entity actor, Entity round, Span<UseAction> into, int n)
        {
            if (!Embarkation.IsPersonCarrier(world, actor)) return n;
            long ammo = Fdp.Toolkit.Combat.RoundPickUp.AmmoOf(world, round);
            var reason = !Injury.IsInTheFight(world, actor) ? UseReason.Unfit
                : !Fdp.Toolkit.Combat.RoundPickUp.InReach(world, actor, round) ? UseReason.OutOfReach
                : !Fdp.Toolkit.Combat.PersonHands.HasRoom(world, actor, ammo) ? UseReason.Carrying
                : UseReason.Ok;
            into[n++] = new UseAction(UseVerbKind.PickUpRound, "Pick up", BehaviorConstants.ActionIdPickUpRound, reason == UseReason.Ok, reason, round,
                Fdp.Toolkit.Combat.RoundPickUp.ReachM);
            return n;
        }

        /// <summary>⭐ <c>CE-1207</c> (§8m PK-6) — "Drop" for every weapon he carries in an open station (Hands, Back), hold list only.</summary>
        private static int Drops(EntityRepository world, Entity actor, Span<UseAction> into, int n)
        {
            if (!Embarkation.IsPersonCarrier(world, actor) || !Has<PassengerBuffer>(world, actor) || !Has<CarrierStations>(world, actor)) return n;
            ref readonly var buffer = ref world.GetComponentRO<PassengerBuffer>(actor);
            ref readonly var stations = ref world.GetComponentRO<CarrierStations>(actor);
            for (int i = 0; i < PassengerBuffer.Capacity && n < into.Length; i++)
            {
                var thing = buffer.Passengers[i];
                if (thing.IsNull || !stations.IsStation(i) || stations.IsConcealed(i) || !Embarkation.IsWeaponSystem(world, thing)) continue;
                into[n++] = new UseAction(UseVerbKind.Drop, "Drop", BehaviorConstants.ActionIdPutDown, true, UseReason.Ok, thing, 0f, HoldOnly: true);
            }
            return n;
        }

        private static int Doors(EntityRepository world, Entity actor, Entity door, Span<UseAction> into, int n)
        {
            var state = world.GetComponentRO<DoorState>(door).State;
            bool reach = DoorActionExecutor.InReach(world, actor, door);
            // the primary toggles by the replicated state (U-7); a locked door's primary is Open, refused as Locked
            var primary = state == TerrainDoorState.Open ? DoorVerb.Close : DoorVerb.Open;
            into[n++] = DoorAction(state, primary, reach, door);
            foreach (var verb in new[] { DoorVerb.Lock, DoorVerb.Unlock, DoorVerb.Breach })
            {
                if (n >= into.Length) break;
                if (DoorRules.Apply(state, verb, out _) == DoorVerbResult.Applied) into[n++] = DoorAction(state, verb, reach, door);
            }
            return n;
        }

        private static UseAction DoorAction(TerrainDoorState state, DoorVerb verb, bool reach, Entity door)
        {
            var result = DoorRules.Apply(state, verb, out _);
            var reason = result == DoorVerbResult.Refused
                ? (state == TerrainDoorState.Locked ? UseReason.Locked : UseReason.DoorRefuses)
                : reach ? UseReason.Ok : UseReason.OutOfReach;
            var (kind, label, id) = verb switch
            {
                DoorVerb.Open => (UseVerbKind.OpenDoor, "Open door", BehaviorConstants.ActionIdOpenDoor),
                DoorVerb.Close => (UseVerbKind.CloseDoor, "Close door", BehaviorConstants.ActionIdCloseDoor),
                DoorVerb.Lock => (UseVerbKind.LockDoor, "Lock door", BehaviorConstants.ActionIdLockDoor),
                DoorVerb.Unlock => (UseVerbKind.UnlockDoor, "Unlock door", BehaviorConstants.ActionIdUnlockDoor),
                _ => (UseVerbKind.BreachDoor, "Breach door", BehaviorConstants.ActionIdBreachDoor),
            };
            return new UseAction(kind, label, id, reason == UseReason.Ok, reason, door, DoorRules.ReachMetres);
        }

        private static int Body(EntityRepository world, Entity actor, Entity man, Span<UseAction> into, int n)
        {
            if (!Embarkation.IsPersonCarrier(world, actor)) return n;
            bool reach = Distance(world, actor, man) <= PickUpExecutor.DefaultReach;
            foreach (var (kind, label, mode) in new[] { (UseVerbKind.Drag, "Drag", CarryMode.Dragged), (UseVerbKind.Carry, "Carry", CarryMode.Carried) })
            {
                if (n >= into.Length) break;
                var can = Embarkation.CanEmbark(world, man, actor, SeatRole.Passenger, mode);   // the carrier is the "vehicle" (C6)
                var reason = !Injury.IsInTheFight(world, actor) ? UseReason.Unfit
                    : !Injury.IsIncapacitated(world, man) ? UseReason.NotDown
                    : can == EmbarkResult.Full ? UseReason.SeatTaken
                    : can == EmbarkResult.Invalid ? UseReason.NotDown
                    : reach ? UseReason.Ok : UseReason.OutOfReach;
                into[n++] = new UseAction(kind, label, BehaviorConstants.ActionIdPickUp, reason == UseReason.Ok, reason, man,
                    PickUpExecutor.DefaultReach, mode);
            }
            return n;
        }

        private static int Vehicle(EntityRepository world, Entity actor, Entity vehicle, Span<UseAction> into, int n)
        {
            bool reach = Distance(world, actor, vehicle) <= VehicleReach;
            bool air = Fdp.Toolkit.Motion.Locomotions.IsAircraft(world, vehicle);
            var drive = Embarkation.CanEmbark(world, actor, vehicle, SeatRole.Driver);
            // FreeSeat falls back to a passenger seat when the asked role is taken — Drive means THE driver seat, so that fallback is SeatTaken
            if (drive == EmbarkResult.Applied && !DriverSeatFree(world, vehicle)) drive = EmbarkResult.Full;
            var board = Embarkation.CanEmbark(world, actor, vehicle, SeatRole.Passenger);
            var driveAction = Seat(air ? UseVerbKind.Pilot : UseVerbKind.Drive, air ? "Pilot" : "Drive", vehicle, SeatRole.Driver, drive, reach);
            var boardAction = Seat(UseVerbKind.Board, "Board", vehicle, SeatRole.Passenger, board, reach);
            // O-4 — a second human boards as a passenger: the free seat goes first
            if (driveAction.Available || !boardAction.Available) { into[n++] = driveAction; if (n < into.Length) into[n++] = boardAction; }
            else { into[n++] = boardAction; if (n < into.Length) into[n++] = driveAction; }
            return n;
        }

        private static bool DriverSeatFree(EntityRepository world, Entity vehicle)
        {
            int seat = Embarkation.FreeSeat(world, vehicle, SeatRole.Driver);
            return seat >= 0 && world.HasComponent<CarrierSeats>(vehicle) && world.GetComponentRO<CarrierSeats>(vehicle).Roles[seat] == SeatRole.Driver;
        }

        private static int Weapon(EntityRepository world, Entity actor, Entity weapon, Span<UseAction> into, int n)
        {
            // ⭐ CE-1207 (§8m PK-2) — a PORTABLE weapon (a rifle, a pistol, a launcher) lying alone is TAKEN into his first open station; a
            //   STATIC one (a tripod gun, a ZU-23) is manned (U-5). One already carried by someone is neither.
            if (Has<Fdp.Toolkit.Combat.WeaponSystemParams>(world, weapon)
                && world.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemParams>(weapon).Portable != (byte)Fdp.Toolkit.Tkb.Domain.WeaponPortability.Static)
                return Take(world, actor, weapon, into, n);
            if (!Has<PassengerBuffer>(world, weapon)) return n;
            bool reach = Distance(world, actor, weapon) <= VehicleReach;
            into[n++] = Seat(UseVerbKind.Man, "Man", weapon, SeatRole.Gunner, Embarkation.CanEmbark(world, actor, weapon, SeatRole.Gunner), reach);
            return n;
        }

        private static int Take(EntityRepository world, Entity actor, Entity weapon, Span<UseAction> into, int n)
        {
            if (!Embarkation.IsPersonCarrier(world, actor) || Has<IsEmbarkedTag>(world, weapon)) return n;
            bool reach = Distance(world, actor, weapon) <= PickUpExecutor.DefaultReach;
            var can = Embarkation.CanEmbark(world, weapon, actor, SeatRole.Passenger, CarryMode.Carried);
            var reason = !Injury.IsInTheFight(world, actor) ? UseReason.Unfit
                : can == EmbarkResult.Full ? UseReason.Carrying
                : can == EmbarkResult.Invalid ? UseReason.NothingToDo
                : reach ? UseReason.Ok : UseReason.OutOfReach;
            into[n++] = new UseAction(UseVerbKind.Take, "Take", BehaviorConstants.ActionIdPickUp, reason == UseReason.Ok, reason, weapon,
                PickUpExecutor.DefaultReach, CarryMode.Carried);
            return n;
        }

        private static UseAction Seat(UseVerbKind kind, string label, Entity vehicle, SeatRole role, EmbarkResult result, bool reach)
        {
            var reason = result switch
            {
                EmbarkResult.Full => UseReason.SeatTaken,
                EmbarkResult.Invalid => UseReason.NothingToDo,
                _ => reach ? UseReason.Ok : UseReason.OutOfReach,
            };
            return new UseAction(kind, label, BehaviorConstants.ActionIdEmbark, reason == UseReason.Ok, reason, vehicle, VehicleReach,
                CarryMode.Seated, role);
        }

        /// <summary>
        /// ⭐ <c>CE-1152</c> — packs <paramref name="action"/>'s parameters the way its executor reads them (<see cref="OpenDoorParams"/>,
        /// <see cref="PickUpParams"/>, <see cref="EmbarkParams"/>, <see cref="DisembarkParams"/>; put down takes none) into
        /// <paramref name="into"/> (an <see cref="InteractionChannel"/>'s params, <see cref="BehaviorConstants.ActionParamsByteSize"/> bytes).
        /// </summary>
        public static void WriteParams(in UseAction action, Span<byte> into)
        {
            into.Clear();
            switch (action.ActionId)
            {
                case BehaviorConstants.ActionIdOpenDoor: case BehaviorConstants.ActionIdCloseDoor: case BehaviorConstants.ActionIdLockDoor:
                case BehaviorConstants.ActionIdUnlockDoor: case BehaviorConstants.ActionIdBreachDoor:
                    var door = new OpenDoorParams { TargetDoor = action.Target };
                    MemoryMarshal.Write(into, in door);
                    break;
                case BehaviorConstants.ActionIdPickUp:
                    var pick = new PickUpParams { Casualty = action.Target, Mode = action.Mode, Reach = action.Reach };
                    MemoryMarshal.Write(into, in pick);
                    break;
                case BehaviorConstants.ActionIdEmbark:
                    var embark = new EmbarkParams { VehicleEntity = action.Target, MaxBoardingRange = action.Reach, Role = action.Role };
                    MemoryMarshal.Write(into, in embark);
                    break;
                case BehaviorConstants.ActionIdDisembark:
                    var leave = default(DisembarkParams);
                    MemoryMarshal.Write(into, in leave);
                    break;
                case BehaviorConstants.ActionIdPutDown:   // ⭐ CE-1207 (PK-6) — Drop names its weapon; Put down leaves it empty (the body)
                    var down = new PutDownParams { Load = action.Verb == UseVerbKind.Drop ? action.Target : Entity.Null };
                    MemoryMarshal.Write(into, in down);
                    break;
                case BehaviorConstants.ActionIdPickUpRound:
                    var round = new PickUpRoundParams { Round = action.Target };
                    MemoryMarshal.Write(into, in round);
                    break;
            }
        }

        private static float Distance(EntityRepository world, Entity a, Entity b)
            => Has<SimTransform>(world, a) && Has<SimTransform>(world, b)
                ? Vector3.Distance(world.GetComponentRO<SimTransform>(a).Position, world.GetComponentRO<SimTransform>(b).Position)
                : float.MaxValue;

        private static bool Has<T>(EntityRepository world, Entity e) where T : unmanaged
            => world.IsComponentTypeRegistered<T>() && world.HasComponent<T>(e);
    }
}
