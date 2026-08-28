using CarKinem.Core;
using Fdp.Core;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;

namespace Hrot.IG;

/// <summary>
/// ECS registration contract for nodes fulfilling the IG role.
/// </summary>
public static class IgRoleComponentRegistry
{
    /// <summary>
    /// Registers the shared IG-role component and event schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterComponent<ResolvedStyle>();
        world.RegisterComponent<CullingState>();
        world.RegisterComponent<SelectionState>();

        world.RegisterComponent<VehicleParams>();
        world.RegisterComponent<IgHealthState>();
        world.RegisterComponent<PerceptionReceptor>();
        world.RegisterComponent<TargetMemory>();
        world.RegisterComponent<WeaponState>();
        world.RegisterComponent<Health>();
        world.RegisterComponent<PhysicsCollider>();

        MissionComponentRegistry.RegisterAll(world);

        world.RegisterComponent<HistoryTrail>();
        world.RegisterComponent<VisualEffectState>();
        world.RegisterComponent<TracerTarget>();
        world.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireNotification>();
        world.RegisterEvent<Fdp.Toolkit.Combat.Contracts.DetonationNotification>();
        world.RegisterEvent<Hrot.IG.IgWeaponFireEvent>();
        world.RegisterManagedComponent<ContextMenuState>();

        world.RegisterManagedComponent<EditablePolyline>();
        world.RegisterComponent<MapOverlayStyle>();
        // UXI-23 S1: MapDisplayComponent moved to the shared map list.
        Hrot.Presentation.Map.MapPresentationRegistry.RegisterAll(world);
        world.RegisterComponent<EntityInfo>();
        world.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent>();

        RouteComponentRegistry.RegisterAll(world);
        ZoneComponentRegistry.RegisterAll(world);

        world.RegisterComponent<GroundClampingConfig>();
        world.RegisterComponent<TerrainClampBaseline>();
    }
}
