using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Hrot.SimHost;

/// <summary>
/// Shared registration for commander-subordinate hierarchy components and events.
/// </summary>
/// <remarks>
/// ⭐ <c>CE-4008</c> — lives beside <c>UnitHierarchySystem</c> (Hrot.Common), which every host runs as cross-role infrastructure
/// (<c>CoreInfrastructureCapabilities.UnitHierarchy</c>). It was in the SimHost assembly, which IG does not reference, so IG ran
/// the system with no schema and died on the first assignment (measured live, a Player entering a replay). The namespace is
/// kept so the existing callers did not change.
/// </remarks>
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
