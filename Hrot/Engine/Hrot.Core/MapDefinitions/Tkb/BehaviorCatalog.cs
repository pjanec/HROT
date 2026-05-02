using System.Reflection;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Map.Definitions.Tkb;

/// <summary>
/// Static catalog that maps TKB entity type constants to the behavior names that
/// are valid for that entity type.  Used by the mission editing UI to populate
/// behavior drop-down lists filtered to the selected entity's capabilities.
///
/// <para>
/// The military and insurgent lists are built once at type initialisation from
/// <see cref="BehaviorContractAttribute"/> attributes on DTOs in this assembly,
/// so they stay in sync automatically as new behaviors are added.
/// </para>
/// <para>
/// The civilian list and the default fallback list remain hardcoded because
/// <c>WanderCivil</c> and <c>PanicFlee</c> have no <see cref="BehaviorContractAttribute"/>
/// DTO (they are out of scope for the current phase).
/// </para>
/// </summary>
public static class BehaviorCatalog
{
    private static readonly IReadOnlyList<string> s_civilianBehaviors =
        ["WanderCivil", "PanicFlee"];

    private static readonly IReadOnlyList<string> s_militaryApcBehaviors;
    private static readonly IReadOnlyList<string> s_infantryBehaviors;
    private static readonly IReadOnlyList<string> s_insurgentBehaviors;

    private static readonly IReadOnlyList<string> s_defaultBehaviors =
        ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "FireAtTarget"];

    static BehaviorCatalog()
    {
        var map = BuildMap();
        s_militaryApcBehaviors = map.GetValueOrDefault(BehaviorCategory.MilitaryApc, s_defaultBehaviors);
        s_infantryBehaviors    = map.GetValueOrDefault(BehaviorCategory.Infantry,    s_defaultBehaviors);
        s_insurgentBehaviors   = map.GetValueOrDefault(BehaviorCategory.Insurgent,   s_defaultBehaviors);
    }

    private static Dictionary<BehaviorCategory, IReadOnlyList<string>> BuildMap()
    {
        var categories = new[] { BehaviorCategory.MilitaryApc, BehaviorCategory.Infantry, BehaviorCategory.Insurgent };
        var lists = categories.ToDictionary(c => c, _ => new List<string>());

        foreach (var type in typeof(BehaviorContractAttribute).Assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<BehaviorContractAttribute>();
            if (attr == null) continue;
            foreach (var cat in categories)
            {
                if (attr.ValidCategories.HasFlag(cat))
                    lists[cat].Add(attr.BehaviorName);
            }
        }

        return lists.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly());
    }

    /// <summary>
    /// Returns the list of behavior names valid for the given TKB entity type.
    /// Military and insurgent lists are built from <see cref="BehaviorContractAttribute"/>
    /// reflection at type initialisation.  Civilian and unknown types use static lists.
    /// </summary>
    /// <param name="tkbType">A TKB entity type constant (see <see cref="TkbEntityTypes"/>).</param>
    public static IReadOnlyList<string> GetValidBehaviors(long tkbType) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => s_civilianBehaviors,
        TkbEntityTypes.CivilianCar        => s_civilianBehaviors,
        TkbEntityTypes.MilitaryApc        => s_militaryApcBehaviors,
        TkbEntityTypes.InfantrySoldier    => s_infantryBehaviors,
        TkbEntityTypes.Insurgent          => s_insurgentBehaviors,
        _                                 => s_defaultBehaviors,
    };
}
