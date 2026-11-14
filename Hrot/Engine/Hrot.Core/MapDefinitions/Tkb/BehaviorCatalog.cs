using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Map.Definitions.Tkb;

/// <summary>
/// Static catalog that maps a TKB entity type to the behaviour names that fit it. Used by ExCon's mission editor and the HTTP list
/// (no entity at hand — only its TKB type); the mission editor's "+ Add task" asks the same <see cref="Hrot.Core.Tkb.BehaviorFit"/> with the entity itself.
///
/// <para>
/// ⭐ <c>CE-4350</c> (docs/DESIGN_Orders_Params_Entity_Classes.md §14) — the 11-id switch and the hard-coded civilian lists are RETIRED: the list is every
/// behaviour this host DECLARES (<see cref="ParamsDtoRegistry"/>, the ONE scan) that fits the TKB template's DIS type through the class table
/// (<see cref="Hrot.Core.Tkb.EntityClassTable"/>). ⚠ It cannot see AUTHORED assets (they are not declared) — the entity-based list does — and it cannot
/// ask for equipment (no entity), so a declared <c>Requires</c> counts as met here.
/// </para>
/// </summary>
public static class BehaviorCatalog
{
    /// <summary>The list of an unknown TKB type (no template, so no DIS type to match): the general-purpose few.</summary>
    private static readonly IReadOnlyList<string> s_defaultBehaviors =
        ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "FireAtTarget"];

    /// <summary>The built-in TKB's DIS type per TKB type id — built once, the ONE source of "what is TKB type N" for a caller with no database.</summary>
    private static readonly Lazy<Dictionary<long, Fdp.Core.DISEntityType>> s_disOf = new(() =>
    {
        var map = new Dictionary<long, Fdp.Core.DISEntityType>();
        foreach (var t in Hrot.Map.Common.HrotEnvironment.CreateTkb().GetAll()) map[(long)t.TkbType] = t.DisType;
        return map;
    });

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ParamsDtoRegistry, System.Collections.Concurrent.ConcurrentDictionary<long, IReadOnlyList<string>>> s_lists = new();

    /// <summary>
    /// The behaviours that fit TKB type <paramref name="tkbType"/>, from this host's declared behaviours (<see cref="ParamsDtoRegistry.Default"/>);
    /// an unknown type gets a short default list. The same list instance is returned until another assembly loads.
    /// </summary>
    /// <param name="tkbType">A TKB entity type constant (see <see cref="TkbEntityTypes"/> and the catalogues' ids).</param>
    public static IReadOnlyList<string> GetValidBehaviors(long tkbType) => GetValidBehaviors(tkbType, ParamsDtoRegistry.Default);

    /// <summary>As <see cref="GetValidBehaviors(long)"/>, over <paramref name="registry"/> (a rail scans a known set).</summary>
    public static IReadOnlyList<string> GetValidBehaviors(long tkbType, ParamsDtoRegistry registry)
    {
        var cache = s_lists.GetValue(registry, _ => new System.Collections.Concurrent.ConcurrentDictionary<long, IReadOnlyList<string>>());
        if (cache.TryGetValue(tkbType, out var hit)) return hit;
        return cache.GetOrAdd(tkbType, Build(tkbType, registry));
    }

    private static IReadOnlyList<string> Build(long tkbType, ParamsDtoRegistry registry)
    {
        var dis = s_disOf.Value.TryGetValue(tkbType, out var d) ? d : default;
        bool known = s_disOf.Value.ContainsKey(tkbType);
        if (!known) return s_defaultBehaviors;
        var list = new List<string>();
        foreach (var decl in registry.Behaviors)
            if (Hrot.Core.Tkb.BehaviorFit.Fits(registry, Hrot.Core.Tkb.EntityClassTable.Default, dis, tkbType, decl.Name, static (_, _) => true, default))
                list.Add(decl.Name);
        return list.Count > 0 ? list : s_defaultBehaviors;
    }
}
