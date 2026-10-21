using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.Common.Orders
{
    /// <summary>
    /// ⭐ <c>CE-1230</c> (docs/designs/gizmos-1/canvas-context-menu-design.md §11 OM-2, OM-3, R-1019) — THE ONE way a person at a map or the
    /// ORBAT tree embarks a unit into a vehicle or disembarks it: one bus command each, applied by the vehicle's owner (Entity Interactions
    /// §7.3, I-2a). The map menu's Embark… / Disembark and the ORBAT tree's drag-and-drop all end here.
    /// </summary>
    public static class EmbarkOrders
    {
        /// <summary><paramref name="passenger"/> boards <paramref name="vehicle"/>. False when either is not alive or they are one.</summary>
        public static bool Embark(EntityRepository world, Entity passenger, Entity vehicle)
        {
            if (passenger == vehicle || !world.IsAlive(passenger) || !world.IsAlive(vehicle)) return false;
            world.Bus.Publish(new EmbarkEntityCommand { Passenger = passenger, Vehicle = vehicle });
            return true;
        }

        /// <summary><paramref name="passenger"/> leaves the vehicle it is aboard (named: the command goes to that vehicle's owner). False when it
        /// is not alive.</summary>
        public static bool Disembark(EntityRepository world, Entity passenger)
        {
            if (!world.IsAlive(passenger)) return false;
            world.Bus.Publish(new DisembarkEntityCommand { Passenger = passenger, Vehicle = VehicleOf(world, passenger) });
            return true;
        }

        /// <summary>Is <paramref name="entity"/> aboard a vehicle (or carried)?</summary>
        public static bool IsAboard(EntityRepository world, Entity entity)
            => world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(entity);

        /// <summary>
        /// May <paramref name="entity"/> be offered "Embark…": a unit that is not aboard, not a vehicle (a carrier of passengers that is not a
        /// person) and not a weapon (a weapon is taken or mounted, never embarked by an order).
        /// </summary>
        public static bool CanOffer(EntityRepository world, Entity entity)
        {
            if (!world.IsAlive(entity) || IsAboard(world, entity)) return false;
            bool carrier = world.IsComponentTypeRegistered<PassengerBuffer>() && world.HasComponent<PassengerBuffer>(entity);
            bool person = world.IsComponentTypeRegistered<PersonCarrier>() && world.HasComponent<PersonCarrier>(entity);
            if (carrier && !person) return false;
            return !(world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>() && world.HasComponent<Fdp.Toolkit.Combat.WeaponSystemParams>(entity));
        }

        private static Entity VehicleOf(EntityRepository world, Entity passenger)
            => IsAboard(world, passenger) ? world.GetComponentRO<IsEmbarkedTag>(passenger).VehicleEntity : Entity.Null;
    }
}
