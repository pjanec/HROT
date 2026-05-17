using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Map.Common;

/// <summary>
/// Shared registration for mission-state components.
/// </summary>
public static class MissionComponentRegistry
{
    /// <summary>
    /// Registers mission component schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterManagedComponent<ActiveMissionPlan>();
        world.RegisterManagedEvent<Hrot.Common.Events.MissionControlIntent>();
    }
}
