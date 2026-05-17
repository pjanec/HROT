using Fdp.Core;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common;

/// <summary>
/// Shared presentation component registrations used by multiple nodes.
/// </summary>
public static class PresentationComponentRegistry
{
    /// <summary>
    /// Registers presentation-oriented ECS components into <paramref name="world"/>.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterComponent<EntityInfo>();
        world.RegisterComponent<SelectionState>();
        world.RegisterManagedComponent<EditablePolyline>();
        world.RegisterComponent<MapOverlayStyle>();
        world.RegisterComponent<IgHealthState>();
        world.RegisterManagedEvent<Hrot.Common.TogglePerspectiveEvent>();
        world.RegisterManagedEvent<Hrot.Common.Events.WorldResetEvent>();
        world.RegisterManagedEvent<Hrot.Common.Events.OpenRenameDialogCommand>();
        world.RegisterManagedEvent<Hrot.Common.Events.SelectEntityCommand>();
    }
}
