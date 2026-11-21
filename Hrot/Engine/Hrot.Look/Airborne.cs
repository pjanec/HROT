using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Motion;

namespace Hrot.Look;

/// <summary>
/// ⭐ CE-11008 (RR-8, R-11002, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — IS THIS BODY IN THE AIR, from replicated state, so a
/// renderer switches wing-beat (or a gear-up pose) from state instead of guessing from height or speed:
/// <list type="number">
///   <item>a flyer with <see cref="FlightState"/> (aircraft, drones, the dragon — it flies as a rotorcraft): its <c>Airborne</c>, written by its motion model;</item>
///   <item>a flock (<see cref="FlockState"/>): <see cref="FlockMode.Flying"/>;</item>
///   <item>a lone bird (<see cref="AnimalState"/>): <see cref="AnimalFlags.InFlight"/>, set by its brain with its flight.</item>
/// </list>
/// Anything else: false. Pure, 0 B.
/// </summary>
public static class Airborne
{
    public static bool Of(EntityRepository world, Entity e)
    {
        if (!world.IsAlive(e)) return false;
        if (world.IsComponentTypeRegistered<FlightState>() && world.HasComponent<FlightState>(e)) return world.GetComponentRO<FlightState>(e).Airborne != 0;
        if (world.IsComponentTypeRegistered<FlockState>() && world.HasComponent<FlockState>(e)) return world.GetComponentRO<FlockState>(e).Mode == FlockMode.Flying;
        if (world.IsComponentTypeRegistered<AnimalState>() && world.HasComponent<AnimalState>(e)) return (world.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.InFlight) != 0;
        return false;
    }
}
