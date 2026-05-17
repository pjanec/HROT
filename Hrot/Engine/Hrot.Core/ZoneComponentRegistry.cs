using Fdp.Core;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common;

/// <summary>
/// Shared registration for zone-membership components.
/// </summary>
public static class ZoneComponentRegistry
{
    /// <summary>
    /// Registers zone component schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterManagedComponent<ZoneMembership>();
        world.RegisterManagedEvent<Hrot.Map.Common.Events.SpawnZoneObstacleCommand>();
        world.RegisterManagedEvent<Hrot.Map.Common.Events.UpdateZoneConfigCommand>();
    }
}
