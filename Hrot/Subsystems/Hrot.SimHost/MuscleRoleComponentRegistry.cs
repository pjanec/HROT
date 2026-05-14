using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;

namespace Hrot.SimHost;

/// <summary>
/// ECS registration contract for nodes fulfilling the Muscle role.
/// </summary>
public static class MuscleRoleComponentRegistry
{
    /// <summary>
    /// Registers the shared Muscle-role component and event schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        KinematicComponentRegistry.RegisterAll(world);
        world.RegisterComponent<NavigationIntent>();
        world.RegisterEvent<WeaponFireNotification>();
        world.RegisterEvent<DetonationNotification>();
    }
}
