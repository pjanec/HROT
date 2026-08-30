using Hrot.IG.Components;
using Hrot.Map.Common;
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

        MissionComponentRegistry.RegisterAll(world);
        PresentationComponentRegistry.RegisterAll(world);
        // ⭐⭐⭐ UXI-23 S1 — SimHost had NO MapDisplayComponent registration at all (measured
        //    2026-08-28: zero source references in the whole project), so its TKB-built entities
        //    carried none and the shared entity gizmos found nothing to draw. This is the same
        //    shared list CGF, IG and the Editor now call.
        Hrot.Presentation.Map.MapPresentationRegistry.RegisterAll(world);

        RouteComponentRegistry.RegisterAll(world);

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
