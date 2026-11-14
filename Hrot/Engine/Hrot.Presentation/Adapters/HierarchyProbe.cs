using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.UI.Common.Adapters;

/// <summary>Shared helpers of the <see cref="IHierarchyAdapter"/> implementations in this folder.</summary>
internal static class HierarchyProbe
{
    /// <summary>HasComponent that treats a component type the world never registered as "absent" (a host without it).</summary>
    public static bool Has<T>(ISimulationView view, Entity e) where T : unmanaged
        => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(e);

    /// <summary>The direct children of <paramref name="parent"/> by scanning the relation's members (an array — the tree builder
    /// uses GetParent over all members instead, so this is for ad-hoc callers).</summary>
    public static ChildEnumerator ChildrenOf(IHierarchyAdapter adapter, ISimulationView view, Entity parent)
    {
        var found = new List<Entity>();
        foreach (var e in view.Query().Build())
            if (adapter.Includes(view, e) && adapter.GetParent(view, e).Equals(parent)) found.Add(e);
        return new ChildEnumerator(view, found.ToArray());
    }
}
