using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Map.Common;
using Hrot.SimHost;

namespace Hrot.CGF;

public static class CgfComponentRegistry
{
    public static void RegisterAll(EntityRepository world)
    {
        HrotSharedComponentRegistry.RegisterAll(world);

        CognitiveComponentRegistry.RegisterAll(world);
        // ⭐⭐ BEHAVIOUR-PRESERVING (2026-09-12, CE-259bf): the EQS trio + raycast events moved OUT of
        //   CognitiveComponentRegistry into the Perception role's own registry. CGF obtained them via
        //   the cognitive set before, so it calls the new one to keep its registered set IDENTICAL.
        // ⚠ Whether CGF actually NEEDS Perception is a separate question with its own evidence —
        //   ⛔ do not drop this line as "obvious cleanup" without measuring what CGF schedules.
        PerceptionRoleComponentRegistry.RegisterAll(world);
        // ⭐ Cross-role sets extracted 2026-09-12 from CognitiveComponentRegistry (CE-259bf slice 3a).
        //   ⚠ BOTH hosts call them, so each host's registered set is unchanged by the move.
        EmbarkationComponentRegistry.RegisterAll(world);
        BehaviorDiagnosticsComponentRegistry.RegisterAll(world);
        HierarchyComponentRegistry.RegisterAll(world);

        KinematicComponentRegistry.RegisterAll(world);
        CombatComponentRegistry.RegisterAll(world);
        world.RegisterComponent<ActiveSensorTracks>();

        PresentationComponentRegistry.RegisterAll(world);
        // UXI-23 S1: MapDisplayComponent moved to the shared map list (it lives in
        // Fdp.Presentation, which Hrot.Core cannot reference -- see MapPresentationRegistry).
        Hrot.Presentation.Map.MapPresentationRegistry.RegisterAll(world);
        RouteComponentRegistry.RegisterAll(world);
        MissionComponentRegistry.RegisterAll(world);
        ZoneComponentRegistry.RegisterAll(world);
        NavigationSolverComponentRegistry.RegisterAll(world);

        world.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();

        world.RegisterEvent<DamageAssessedEvent>();
        world.RegisterEvent<WeaponFireIntent>();
        world.RegisterEvent<SensorTrackStateEvent>();

        GenesisIntentRegistry.RegisterAll(world);
    }
}
