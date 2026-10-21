using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-3233</c> (civilians B-11, AW-13; docs/DESIGN_Civilians_And_Crowd_Control.md §7.13 ST-4) — "put <see cref="Thing"/>, which
    /// <see cref="Man"/> carries, in his station <see cref="Seat"/>" (draw, stow, conceal). Raised on any node; applied where the man's
    /// passenger list is owned (<see cref="Embarkation.OwnsVehicle"/>) — the interaction transport carries it there (kind 11). The new slot
    /// replicates; every node follows it.
    /// </summary>
    [EventId(CombatConstants.RestationEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct RestationEvent
    {
        public Entity Man;
        public Entity Thing;
        public byte Seat;
        /// <summary>Set by the transport's ingress (it is never sent back out).</summary>
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3233</c> (B-11, §7.13) — a person's CARRY STATIONS: the HANDS (fired from), the BACK (stowed: carried, seen) and the HIDDEN
    /// station (stowed and concealed: unseen, arms nobody). Which station a carried thing is in is its slot in the man's
    /// <see cref="PassengerBuffer"/> — already on the wire — and what a station IS is a type fact (<see cref="CarrierStations"/>), so every
    /// reader here answers on any node.
    /// </summary>
    public static class CarryStations
    {
        /// <summary>The seat <paramref name="thing"/> occupies on whatever carries it, or −1.</summary>
        public static int SeatOf(ISimulationView view, Entity thing, out Entity carrier)
        {
            carrier = Entity.Null;
            if (view is not EntityRepository world || !world.IsAlive(thing)
                || !world.IsComponentTypeRegistered<IsEmbarkedTag>() || !world.HasComponent<IsEmbarkedTag>(thing)) return -1;
            ref readonly var tag = ref world.GetComponentRO<IsEmbarkedTag>(thing);
            carrier = tag.VehicleEntity;
            return tag.Seat;
        }

        /// <summary>Is <paramref name="thing"/> carried in a STOWED station (cannot be fired from it)?</summary>
        public static bool IsStowed(ISimulationView view, Entity thing)
            => SeatOf(view, thing, out var carrier) is var seat and >= 0 && Stations(view, carrier) is { } s && s.IsStowed(seat);

        /// <summary>Is <paramref name="thing"/> carried in a CONCEALED station (nobody sees it; it arms nobody)?</summary>
        public static bool IsConcealed(ISimulationView view, Entity thing)
            => SeatOf(view, thing, out var carrier) is var seat and >= 0 && Stations(view, carrier) is { } s && s.IsConcealed(seat);

        /// <summary>The man's HANDS station (the first station that is not stowed), or −1.</summary>
        public static int Hands(ISimulationView view, Entity man) => Find(view, man, stowed: false, concealed: false);

        /// <summary>The man's BACK station (stowed, seen), or −1.</summary>
        public static int Back(ISimulationView view, Entity man) => Find(view, man, stowed: true, concealed: false);

        /// <summary>The man's HIDDEN station (stowed, concealed), or −1.</summary>
        public static int Hidden(ISimulationView view, Entity man) => Find(view, man, stowed: true, concealed: true);

        /// <summary>
        /// Put <paramref name="thing"/>, which <paramref name="man"/> already carries, in his station <paramref name="seat"/> — on the node that
        /// owns his list. False when it is not his, the seat is not a station accepting it, or the seat is taken. The slot moves; every node
        /// re-tags it from the replicated list (<c>EmbarkedStateSystem</c>).
        /// </summary>
        public static bool Move(EntityRepository world, Entity man, Entity thing, int seat)
        {
            if (!Embarkation.OwnsVehicle(world, man) || Stations(world, man) is not { } stations || !stations.IsStation(seat)) return false;
            ref var buffer = ref world.GetComponentRW<PassengerBuffer>(man);
            int from = -1;
            for (int i = 0; i < PassengerBuffer.Capacity; i++) if (buffer.Passengers[i] == thing) { from = i; break; }
            if (from < 0) return false;
            if (from == seat) return true;
            if (!buffer.Passengers[seat].IsNull) return false;
            byte kinds = world.IsComponentTypeRegistered<WeaponSystemParams>() && world.HasComponent<WeaponSystemParams>(thing)
                ? world.GetComponentRO<WeaponSystemParams>(thing).MountKinds : (byte)0;
            if ((stations.Seats[seat].Accepts & kinds) == 0) return false;
            var mode = buffer.ModeOf(from);
            buffer.Passengers[seat] = thing;
            buffer.Passengers[from] = Entity.Null;
            buffer.SetMode(seat, mode);
            buffer.SetMode(from, CarryMode.Seated);
            Embarkation.Board(world, thing, man, seat);
            return true;
        }

        /// <summary>Ask the node that owns <paramref name="man"/>'s list to put <paramref name="thing"/> in <paramref name="seat"/> (the
        /// interaction transport carries it; in one world <see cref="CarryStationSystem"/> applies it next frame).</summary>
        public static bool Ask(EntityRepository world, Entity man, Entity thing, int seat)
        {
            if (!world.Bus.IsRegistered<RestationEvent>() || seat < 0 || seat >= PassengerBuffer.Capacity) return false;
            world.Bus.Publish(new RestationEvent { Man = man, Thing = thing, Seat = (byte)seat });
            return true;
        }

        private static int Find(ISimulationView view, Entity man, bool stowed, bool concealed)
        {
            if (Stations(view, man) is not { } s) return -1;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (s.IsStation(i) && s.IsStowed(i) == stowed && s.IsConcealed(i) == concealed
                    && (s.Seats[i].Accepts & (byte)Fdp.Toolkit.Tkb.Domain.MountKinds.Carried) != 0) return i;
            return -1;
        }

        private static CarrierStations? Stations(ISimulationView view, Entity carrier)
            => view is EntityRepository world && !carrier.IsNull && world.IsAlive(carrier)
               && world.IsComponentTypeRegistered<CarrierStations>() && world.HasComponent<CarrierStations>(carrier)
                ? world.GetComponentRO<CarrierStations>(carrier) : null;
    }

    /// <summary>
    /// ⭐ <c>CE-3233</c> (B-11, §7.13 ST-4) — applies <see cref="RestationEvent"/> where the man's list is owned. In the <c>Interactions</c>
    /// capability on every node (B-13's pick-up runs there too).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class CarryStationSystem : IEcsModuleSystem
    {
        /// <summary>Moves applied on this node (rails read it).</summary>
        public long Moved { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository world || !world.Bus.IsRegistered<RestationEvent>()) return;
            var asks = world.ReadEvents<RestationEvent>();
            for (int i = 0; i < asks.Length; i++)
                if (CarryStations.Move(world, asks[i].Man, asks[i].Thing, asks[i].Seat)) Moved++;
        }
    }
}
