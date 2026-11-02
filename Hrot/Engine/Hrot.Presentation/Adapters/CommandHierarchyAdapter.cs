using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.UI.Common.Adapters;

/// <summary>
/// ⭐ CE-4213 (HV-4) — the COMMAND relation as an <see cref="IHierarchyAdapter"/>: the parent of an entity is its
/// <c>UnitSubordinate.Commander</c> (today's ORBAT rule, moved out of <c>ScenarioOrbatAdapter.GetVisibleNodes</c>). No commander
/// (or a dead one) ⇒ a root. Members are the entities with an <see cref="EntityInfo"/> (the interface default).
/// </summary>
public sealed class CommandHierarchyAdapter : IHierarchyAdapter
{
    /// <inheritdoc/>
    public Entity GetParent(ISimulationView view, Entity entity)
    {
        if (!HierarchyProbe.Has<UnitSubordinate>(view, entity)) return Entity.Null;
        var commander = view.GetComponentRO<UnitSubordinate>(entity).Commander;
        return !commander.IsNull && view.IsAlive(commander) ? commander : Entity.Null;
    }

    /// <inheritdoc/>
    public ChildEnumerator GetChildren(ISimulationView view, Entity entity)
        => HierarchyProbe.ChildrenOf(this, view, entity);

    /// <inheritdoc/>
    public bool IsSpatialLeaf(ISimulationView view, Entity entity) => !GetChildren(view, entity).MoveNext();
}
