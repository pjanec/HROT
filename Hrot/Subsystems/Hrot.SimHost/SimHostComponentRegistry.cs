using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.Components;

namespace Hrot.SimHost;

public static class SimHostComponentRegistry
{
    public static void RegisterAll(EntityRepository world)
    {
        HrotSharedComponentRegistry.RegisterAll(world);

        CognitiveComponentRegistry.RegisterAll(world);
        MuscleRoleComponentRegistry.RegisterAll(world);
        CombatComponentRegistry.RegisterAll(world);

        world.RegisterManagedComponent<ActiveMissionPlan>();
        PresentationComponentRegistry.RegisterAll(world);

        world.RegisterManagedComponent<RoutePlan>();
        world.RegisterComponent<PersonalRouteRef>();
        world.RegisterComponent<RouteTrajectoryCache>();

        GenesisIntentRegistry.RegisterAll(world);

        world.RegisterEvent<CmdSpawnVehicle>();
        world.RegisterEvent<CmdCreateFormation>();
        world.RegisterEvent<CmdJoinFormation>();
        world.RegisterEvent<CmdLeaveFormation>();

        HierarchyComponentRegistry.RegisterAll(world);

        world.RegisterManagedComponent<Hrot.Common.ActivePerspective>();

        world.RegisterEvent<Hrot.Common.Events.MissionControlAckEvent>();

        NavigationSolverComponentRegistry.RegisterAll(world);

        world.RegisterEvent<Fdp.Toolkit.Physics.RaycastRequestEvent>();
        world.RegisterEvent<Fdp.Toolkit.Physics.RaycastResultEvent>();

        world.RegisterEvent<Hrot.Common.Events.GlobalActionRequestedEvent>();

        world.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent>();
    }
}