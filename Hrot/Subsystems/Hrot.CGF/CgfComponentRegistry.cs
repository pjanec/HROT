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
        HierarchyComponentRegistry.RegisterAll(world);

        KinematicComponentRegistry.RegisterAll(world);
        CombatComponentRegistry.RegisterAll(world);
        world.RegisterComponent<ActiveSensorTracks>();

        PresentationComponentRegistry.RegisterAll(world);
        world.RegisterComponent<MapDisplayComponent>();
        RouteComponentRegistry.RegisterAll(world);
        MissionComponentRegistry.RegisterAll(world);
        ZoneComponentRegistry.RegisterAll(world);

        world.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();

        world.RegisterEvent<DamageAssessedEvent>();
        world.RegisterEvent<WeaponFireIntent>();
        world.RegisterEvent<SensorTrackStateEvent>();

        GenesisIntentRegistry.RegisterAll(world);
    }
}
