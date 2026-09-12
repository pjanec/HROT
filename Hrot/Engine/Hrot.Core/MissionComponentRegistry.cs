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
        // ⭐⭐ MOVED HERE 2026-09-12 from CognitiveComponentRegistry (CE-259bf slice 2, design §3.9a).
        //   MissionPlanQueue is the mission tier's queue and belongs beside ActiveMissionPlan.
        //   ⚠ BEHAVIOUR-PRESERVING: SimHost and CGF both already call this registry, so the set each
        //   host registers is unchanged. It is what lets SimHost stop calling the BRAIN's registry.
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.MissionPlanQueue>();
        world.RegisterManagedEvent<Hrot.Common.Events.MissionControlIntent>();
    }
}
