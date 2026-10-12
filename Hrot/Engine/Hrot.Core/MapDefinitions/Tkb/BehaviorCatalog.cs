using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Map.Definitions.Tkb;

/// <summary>
/// Static catalog that maps TKB entity type constants to the behavior names that
/// are valid for that entity type.  Used by the mission editing UI to populate
/// behavior drop-down lists filtered to the selected entity's capabilities.
///
/// <para>
/// ⭐ <c>CE-2134</c> — the military, insurgent and commander lists come from each behaviour's declared categories
/// (<see cref="BehaviorAttribute.Categories"/>), read through the ONE scan (<see cref="ParamsDtoRegistry"/>), so they follow
/// what this host can actually run. ⛔ HISTORY: built from <c>[BehaviorContract]</c> on the DTOs in this assembly, which listed
/// the editor's example behaviours on every node, CGF and ExCon included.
/// </para>
/// <para>
/// The civilian list and the default fallback list remain hardcoded because <c>WanderCivil</c> and <c>PanicFlee</c> declare
/// no params contract (they are out of scope for the current phase). The class table replaces the categories (CE-2142).
/// </para>
/// </summary>
public static class BehaviorCatalog
{
    private static readonly IReadOnlyList<string> s_civilianBehaviors =
        ["WanderCivil", "PanicFlee"];

    private static readonly IReadOnlyList<string> s_defaultBehaviors =
        ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "FireAtTarget"];

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ParamsDtoRegistry, Dictionary<BehaviorCategory, IReadOnlyList<string>>> s_maps = new();

    private static Dictionary<BehaviorCategory, IReadOnlyList<string>> MapFor(ParamsDtoRegistry registry)
        => s_maps.GetValue(registry, r =>
        {
            var map = new Dictionary<BehaviorCategory, IReadOnlyList<string>>();
            foreach (var cat in new[] { BehaviorCategory.MilitaryApc, BehaviorCategory.Infantry, BehaviorCategory.Insurgent, BehaviorCategory.Commander })
            {
                var names = r.NamesWithCategory(cat);
                map[cat] = names.Count > 0 ? names : s_defaultBehaviors;
            }
            return map;
        });

    /// <summary>
    /// Returns the list of behavior names valid for the given TKB entity type, from this host's declared behaviours
    /// (<see cref="ParamsDtoRegistry.Default"/>). Civilian and unknown types use static lists. The same list instance is
    /// returned until another assembly loads.
    /// </summary>
    /// <param name="tkbType">A TKB entity type constant (see <see cref="TkbEntityTypes"/>).</param>
    public static IReadOnlyList<string> GetValidBehaviors(long tkbType) => GetValidBehaviors(tkbType, ParamsDtoRegistry.Default);

    /// <summary>As <see cref="GetValidBehaviors(long)"/>, over <paramref name="registry"/> (a rail scans a known set).</summary>
    public static IReadOnlyList<string> GetValidBehaviors(long tkbType, ParamsDtoRegistry registry) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => s_civilianBehaviors,
        TkbEntityTypes.CivilianCar        => s_civilianBehaviors,
        TkbEntityTypes.MilitaryApc        => MapFor(registry)[BehaviorCategory.MilitaryApc],
        TkbEntityTypes.InfantrySoldier    => MapFor(registry)[BehaviorCategory.Infantry],
        TkbEntityTypes.Insurgent          => MapFor(registry)[BehaviorCategory.Insurgent],
        TkbEntityTypes.Unit_TankPlatoon      => MapFor(registry)[BehaviorCategory.Commander],
        TkbEntityTypes.Unit_TankPlatoon_Auto => MapFor(registry)[BehaviorCategory.Commander],
        TkbEntityTypes.Unit_InfantrySquad    => MapFor(registry)[BehaviorCategory.Commander],
        _                                 => s_defaultBehaviors,
    };
}
