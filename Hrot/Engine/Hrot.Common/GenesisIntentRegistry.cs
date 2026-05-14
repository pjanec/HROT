using Fdp.Core;
using Hrot.Common.Serializers;

namespace Hrot.Map.Common;

/// <summary>
/// Shared registration for transient genesis intent DTO components.
/// </summary>
public static class GenesisIntentRegistry
{
    /// <summary>
    /// Registers all scenario-load intent DTOs into <paramref name="world"/>.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterManagedComponent<InitialPassengersIntent>();
        world.RegisterManagedComponent<InitialVehicleIntent>();
        world.RegisterManagedComponent<InitialHierarchyIntent>();
        world.RegisterManagedComponent<InitialRouteIntent>();
        world.RegisterManagedComponent<InitialTargetsIntent>();
        world.RegisterManagedComponent<InitialUnitSubordinateIntent>();
    }
}
