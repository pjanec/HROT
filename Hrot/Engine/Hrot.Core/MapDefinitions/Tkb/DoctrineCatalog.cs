using System.Reflection;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Doctrine;

namespace Hrot.Map.Definitions.Tkb;

/// <summary>
/// Static catalog that maps TKB entity type constants to the doctrine names that
/// are valid for that entity type.  Used by the mission editing UI to populate
/// doctrine drop-down lists filtered to the selected entity's capabilities.
///
/// <para>
/// The military and insurgent lists are built once at type initialisation from
/// <see cref="DoctrineContractAttribute"/> attributes on DTOs in this assembly,
/// so they stay in sync automatically as new doctrines are added.
/// </para>
/// <para>
/// The civilian list and the default fallback list remain hardcoded because
/// <c>WanderCivil</c> and <c>PanicFlee</c> have no <see cref="DoctrineContractAttribute"/>
/// DTO (they are out of scope for the current phase).
/// </para>
/// </summary>
public static class DoctrineCatalog
{
    private static readonly IReadOnlyList<string> s_civilianDoctrines =
        ["WanderCivil", "PanicFlee"];

    private static readonly IReadOnlyList<string> s_militaryApcDoctrines;
    private static readonly IReadOnlyList<string> s_infantryDoctrines;
    private static readonly IReadOnlyList<string> s_insurgentDoctrines;

    private static readonly IReadOnlyList<string> s_defaultDoctrines =
        ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "FireAtTarget"];

    static DoctrineCatalog()
    {
        var map = BuildMap();
        s_militaryApcDoctrines = map.GetValueOrDefault(DoctrineCategory.MilitaryApc, s_defaultDoctrines);
        s_infantryDoctrines    = map.GetValueOrDefault(DoctrineCategory.Infantry,    s_defaultDoctrines);
        s_insurgentDoctrines   = map.GetValueOrDefault(DoctrineCategory.Insurgent,   s_defaultDoctrines);
    }

    private static Dictionary<DoctrineCategory, IReadOnlyList<string>> BuildMap()
    {
        var categories = new[] { DoctrineCategory.MilitaryApc, DoctrineCategory.Infantry, DoctrineCategory.Insurgent };
        var lists = categories.ToDictionary(c => c, _ => new List<string>());

        foreach (var type in typeof(DoctrineContractAttribute).Assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<DoctrineContractAttribute>();
            if (attr == null) continue;
            foreach (var cat in categories)
            {
                if (attr.ValidCategories.HasFlag(cat))
                    lists[cat].Add(attr.BehaviorId);
            }
        }

        return lists.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly());
    }

    /// <summary>
    /// Returns the list of doctrine names valid for the given TKB entity type.
    /// Military and insurgent lists are built from <see cref="DoctrineContractAttribute"/>
    /// reflection at type initialisation.  Civilian and unknown types use static lists.
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
