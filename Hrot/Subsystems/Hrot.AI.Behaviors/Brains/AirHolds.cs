using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Common.Components;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 (B8, B10, R-2015; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9b) — what an aircraft's BRAIN knows
    /// about the stop it is making: the motion side holds it at an <c>Unload</c> / <c>Pickup</c> waypoint (<see cref="NavigationPhase.Unloading"/> /
    /// <see cref="NavigationPhase.Loading"/>) and only the brain can say the hold is over — it knows who is aboard and the rosters.
    /// <list type="bullet">
    ///   <item><b>Unload</b>: released when nobody alive is aboard. <b>Drop</b> (airborne insertion B-3): the same.</item>
    ///   <item><b>Rope</b> (B-3): released when nobody alive is aboard AND nobody is on its ropes (<see cref="AnyOnRopes"/>).</item>
    ///   <item><b>Pickup</b>: released when every live member of the waypoint's <c>HoldUnit</c> is aboard THIS aircraft (a man they carried
    ///     seated too — <see cref="SquadEmbarkBehavior.AllAboard"/>). A Pickup that names no unit, or a unit this node cannot find, holds.</item>
    /// </list>
    /// And, for the squad's standing orders, whether a carrier is ON THE GROUND (<see cref="OnTheGround"/>) — nobody gets out of a flying one.
    /// </summary>
    public static class AirHolds
    {
        /// <summary>A body this close to the ground (m) counts as standing on it when its phase says nothing (a helicopter parked at the start).</summary>
        public const float OnTheGroundMetres = 1.5f;

        /// <summary>True when <paramref name="aircraft"/>'s current Unload / Pickup hold may end (see the class remarks); false at any other time.</summary>
        public static bool ShouldRelease(EntityRepository world, Entity aircraft, long routeNetworkId)
        {
            if (!world.HasComponent<NavigationStatus>(aircraft)) return false;
            var phase = world.GetComponentRO<NavigationStatus>(aircraft).Phase;
            if (phase is NavigationPhase.Unloading or NavigationPhase.Dropping) return !AnyLiveAboard(world, aircraft);
            // ⭐ airborne insertion B-3 (D-7) — Unload's rule alone would fly off with men hanging: they leave the passenger list at the door
            if (phase == NavigationPhase.Roping) return !AnyLiveAboard(world, aircraft) && !AnyOnRopes(world, aircraft);
            if (phase != NavigationPhase.Loading) return false;
            var unit = PickupUnitAt(world, routeNetworkId, aircraft);
            return !unit.IsNull && SquadEmbarkBehavior.AllAboard(world, unit, aircraft);
        }

        /// <summary>True when somebody alive sits in <paramref name="vehicle"/> (the dead stay aboard and do not hold it).</summary>
        public static bool AnyLiveAboard(EntityRepository world, Entity vehicle)
        {
            if (!world.HasComponent<PassengerBuffer>(vehicle)) return false;
            var buffer = world.GetComponentRO<PassengerBuffer>(vehicle);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (!buffer.Passengers[i].IsNull && world.IsAlive(buffer.Passengers[i])) return true;
            return false;
        }

        /// <summary>⭐ Airborne insertion B-3 — true while a live man hangs on <paramref name="aircraft"/>'s ropes: a fast-rope
        /// <see cref="Descent"/> anchored to it, not <see cref="DescentPhase.Landed"/> yet.</summary>
        public static bool AnyOnRopes(EntityRepository world, Entity aircraft)
        {
            if (!world.IsComponentTypeRegistered<Descent>()) return false;
            foreach (var e in world.Query().With<Descent>().Build())
            {
                ref readonly var d = ref world.GetComponentRO<Descent>(e);
                if (d.Kind == DescentKind.FastRope && d.Anchor == aircraft && d.Phase != DescentPhase.Landed) return true;
            }
            return false;
        }

        /// <summary>The unit the route's Pickup waypoint nearest to <paramref name="aircraft"/> waits for (its <c>HoldUnit</c>); Null when the
        /// route, the waypoint or the unit is not known here.</summary>
        public static Entity PickupUnitAt(EntityRepository world, long routeNetworkId, Entity aircraft)
        {
            if (routeNetworkId <= 0 || !world.HasComponent<SimTransform>(aircraft)) return Entity.Null;
            var route = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, routeNetworkId);
            if (route.IsNull || !world.HasManagedComponent<RoutePlan>(route)) return Entity.Null;
            var plan = ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<RoutePlan>(route);
            if (plan == null) return Entity.Null;
            var at = world.GetComponentRO<SimTransform>(aircraft).Position;
            long unit = 0;
            float best = float.MaxValue;
            foreach (var w in plan.Waypoints)
            {
                if (w.Hold != RouteHold.Pickup || w.HoldUnit <= 0) continue;
                float d = Vector2.DistanceSquared(new Vector2(w.Position.X, w.Position.Y), new Vector2(at.X, at.Y));
                if (d < best) { best = d; unit = w.HoldUnit; }
            }
            return unit > 0 ? Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, unit) : Entity.Null;
        }

        /// <summary>
        /// True when <paramref name="vehicle"/> stands on the ground: a ground vehicle always; an aircraft when its route holds it landed
        /// (<see cref="NavigationPhase.Landed"/> / <c>Unloading</c> / <c>Loading</c>), or — no landing phase — when it sits within
        /// <see cref="OnTheGroundMetres"/> of the terrain this node knows. ⛔ Otherwise it is flying, and nobody gets out.
        /// </summary>
        public static bool OnTheGround(EntityRepository world, Entity vehicle)
        {
            if (!Locomotions.InAir(world, vehicle)) return true;
            if (world.HasComponent<NavigationStatus>(vehicle))
            {
                var phase = world.GetComponentRO<NavigationStatus>(vehicle).Phase;
                if (phase is NavigationPhase.Landed or NavigationPhase.Unloading or NavigationPhase.Loading) return true;
                if (phase is NavigationPhase.TakingOff or NavigationPhase.Landing or NavigationPhase.Roping or NavigationPhase.Dropping) return false;
            }
            if (!world.HasComponent<SimTransform>(vehicle)) return false;
            var p = world.GetComponentRO<SimTransform>(vehicle).Position;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(world);
            return terrain != null && p.Z - terrain.GroundHeightAt(p.X, p.Y) <= OnTheGroundMetres;
        }

        /// <summary>The vehicle <paramref name="member"/> rides in, or Null.</summary>
        public static Entity CarrierOf(EntityRepository world, Entity member)
            => world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(member)
                ? world.GetComponentRO<IsEmbarkedTag>(member).VehicleEntity
                : Entity.Null;
    }
}
