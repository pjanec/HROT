using Hrot.Map.Common;

namespace Hrot.Map.Definitions.Tkb;

/// <summary>
/// Static catalog that maps TKB entity type constants to the doctrine names that
/// are valid for that entity type.  Used by the mission editing UI to populate
/// doctrine drop-down lists filtered to the selected entity's capabilities.
///
/// <para>
/// Each returned list is backed by a <c>private static readonly</c> field so that
/// repeated calls return the same instance without per-call allocation.
/// </para>
/// </summary>
public static class DoctrineCatalog
{
    private static readonly IReadOnlyList<string> s_civilianDoctrines =
        ["WanderCivil", "PanicFlee"];

    private static readonly IReadOnlyList<string> s_militaryApcDoctrines =
        ["ConvoyEscort", "MoveToLocation", "FollowRoute", "FireAtTarget"];

    private static readonly IReadOnlyList<string> s_infantryDoctrines =
        ["InfantryCombat", "MoveToLocation", "JoinFormation", "FireAtTarget"];

    private static readonly IReadOnlyList<string> s_insurgentDoctrines =
        ["Ambush", "MoveToLocation", "FireAtTarget"];

    private static readonly IReadOnlyList<string> s_defaultDoctrines =
        ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "FireAtTarget"];

    /// <summary>
    /// Returns the list of doctrine names valid for the given TKB entity type.
    /// Uses a static <c>readonly</c> backing field per type to avoid per-call allocation.
    /// Unknown TKB types return a generic fallback list.
    /// </summary>
    /// <param name="tkbType">A TKB entity type constant (see <see cref="TkbEntityTypes"/>).</param>
    public static IReadOnlyList<string> GetValidDoctrines(long tkbType) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => s_civilianDoctrines,
        TkbEntityTypes.CivilianCar        => s_civilianDoctrines,
        TkbEntityTypes.MilitaryApc        => s_militaryApcDoctrines,
        TkbEntityTypes.InfantrySoldier    => s_infantryDoctrines,
        TkbEntityTypes.Insurgent          => s_insurgentDoctrines,
        _                                 => s_defaultDoctrines,
    };
}
