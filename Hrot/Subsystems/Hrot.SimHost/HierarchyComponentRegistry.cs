using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Hrot.SimHost;

/// <summary>
/// Shared registration for commander-subordinate hierarchy components and events.
/// </summary>
public static class HierarchyComponentRegistry
{
    /// <summary>
    /// Registers hierarchy component and event schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterComponent<UnitRoster>();
        world.RegisterComponent<UnitSubordinate>();
        world.RegisterEvent<CmdAssignSubordinate>();
        world.RegisterEvent<CmdRemoveSubordinate>();
        world.RegisterEvent<CmdAssignSubordinateRejected>();
    }
}
