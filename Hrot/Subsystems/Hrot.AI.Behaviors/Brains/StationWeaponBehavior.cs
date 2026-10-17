using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 4 (AW-11, AW-16; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5h.7 CO-2) — <c>StationWeapon</c>: he puts the
    /// weapon he carries in one of his carry stations (backend B-11, <see cref="CarryStations"/>) — HANDS (draw), BACK (sling) or HIDDEN
    /// (conceal) — where his list is owned with <see cref="CarryStations.Move"/>, elsewhere by asking (<see cref="CarryStations.Ask"/>); then he
    /// holds still <see cref="StationWeaponParams.Seconds"/> and finishes, so the next mission task runs. ⛔ Drawing is not a hostile act
    /// (AW-16): the act is the AIM, decided where every posture is written (<c>WeaponPostureOf</c>, CO-1). Failure when he carries no
    /// weapon the station takes, or has no such station.
    /// </summary>
    public static class StationWeaponBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct StationWeaponParams
        {
            /// <summary>1 Hands · 2 Back · 3 Hidden (<see cref="WeaponStation"/>).</summary>
            public byte Station;
            public float Seconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StationWeaponState
        {
            /// <summary>When the weapon was seen in the station (0 = not yet).</summary>
            public double Since;
            /// <summary>1 once the move was asked of the list's owner (a node that does not own it).</summary>
            public byte Asked;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StationWeaponBlackboard
        {
            public StationWeaponParams Params;
            public StationWeaponState State;
        }

        [BehaviorResolver(BehaviorNames.StationWeapon)]
        public static void Resolve(in StationWeaponParamsJsonDto authored, ref StationWeaponBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new StationWeaponParams
            {
                Station = authored.Station is WeaponStation.Back or WeaponStation.Hidden ? (byte)authored.Station : (byte)WeaponStation.Hands,
                Seconds = authored.Seconds > 0f ? authored.Seconds : 0f,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.StationWeapon, Params = typeof(StationWeaponParamsJsonDto), Categories = BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
        [BTreeDefinition(BehaviorNames.StationWeapon, Curated = true, Group = "Library/Orders", ParamsType = typeof(StationWeaponBlackboard))]
        public static BTreeBuilder<StationWeaponBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<StationWeaponBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Station);

        /// <summary>One tick: the weapon to the station (once), then still for <c>Seconds</c>. Success when held long enough.</summary>
        public static NodeStatus Station(ref StationWeaponParams p, ref StationWeaponState ws, Entity self, EntityRepository world)
        {
            int seat = SeatFor(world, self, (WeaponStation)p.Station);
            if (seat < 0) return NodeStatus.Failure;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            LocomotionMoveTo.Stop(world, self);
            if (ws.Since <= 0)
            {
                if (WeaponIn(world, self, seat).IsNull)
                {
                    if (!Place(world, self, seat, ref ws)) return NodeStatus.Failure;
                    if (WeaponIn(world, self, seat).IsNull) return NodeStatus.Running;   // asked of the list's owner: wait for the replicated slot
                }
                ws.Since = now;
            }
            return now - ws.Since >= p.Seconds ? NodeStatus.Success : NodeStatus.Running;
        }

        /// <summary>The station's seat on <paramref name="man"/>, or −1.</summary>
        internal static int SeatFor(EntityRepository world, Entity man, WeaponStation station) => station switch
        {
            WeaponStation.Back => CarryStations.Back(world, man),
            WeaponStation.Hidden => CarryStations.Hidden(world, man),
            _ => CarryStations.Hands(world, man),
        };

        /// <summary>The weapon system <paramref name="man"/> carries in <paramref name="seat"/>, or <see cref="Entity.Null"/>.</summary>
        internal static Entity WeaponIn(EntityRepository world, Entity man, int seat)
        {
            if (!world.HasComponent<PassengerBuffer>(man)) return Entity.Null;
            var thing = world.GetComponentRO<PassengerBuffer>(man).Passengers[seat];
            return IsWeapon(world, thing) ? thing : Entity.Null;
        }

        /// <summary>Move (or ask to move) the first weapon he carries that the station takes. False when none can go there.</summary>
        private static bool Place(EntityRepository world, Entity man, int seat, ref StationWeaponState ws)
        {
            if (!world.HasComponent<PassengerBuffer>(man)) return false;
            bool owns = Embarkation.OwnsVehicle(world, man);
            if (!owns && ws.Asked != 0) return true;
            var buffer = world.GetComponentRO<PassengerBuffer>(man);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var thing = buffer.Passengers[i];
                if (i == seat || !IsWeapon(world, thing)) continue;
                if (owns) { if (CarryStations.Move(world, man, thing, seat)) return true; continue; }
                if (CarryStations.Ask(world, man, thing, seat)) { ws.Asked = 1; return true; }
            }
            return false;
        }

        private static bool IsWeapon(EntityRepository world, Entity thing)
            => !thing.IsNull && world.IsAlive(thing) && world.IsComponentTypeRegistered<WeaponSystemParams>() && world.HasComponent<WeaponSystemParams>(thing);
    }
}
