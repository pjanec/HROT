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
    /// <remarks>⭐ CE-4239 — the Command ORBAT lists what can be SEEN: the one <see cref="Hrot.Common.Diagnostics.Gizmos.Seeable"/> rule the map's projectors use (no objective, trigger, score or evaluation instance).</remarks>
    public bool Includes(ISimulationView view, Entity entity)
        => view.HasComponent<EntityInfo>(entity) && Hrot.Common.Diagnostics.Gizmos.Seeable.OnMap(view, entity);

    /// <inheritdoc/>
    /// <remarks>⭐ CE-4213 ⑥ — an entity a macro expansion made takes its place from the MACRO: moving it in the chain of command would be undone by the
    /// next re-expansion, so it is refused with the macro named (the path the instance expands).</remarks>
    public string? ReparentRefusal(ISimulationView view, Entity child, Entity newParent)
    {
        if (!HierarchyProbe.Has<Fdp.Toolkit.Scenario.MacroOrigin>(view, child)) return null;
        var instance = view.GetComponentRO<Fdp.Toolkit.Scenario.MacroOrigin>(child).Instance.Resolve(view);
        string path = !instance.IsNull && view.IsAlive(instance) && view.HasManagedComponent<Fdp.Toolkit.Scenario.MacroInstance>(instance)
            ? view.GetManagedComponentRO<Fdp.Toolkit.Scenario.MacroInstance>(instance).Macro
            : "?";
        return $"from macro {path} — edit the macro or its args";
    }

    /// <inheritdoc/>
    public bool CanReparent(ISimulationView view, Entity child, Entity newParent) => ReparentRefusal(view, child, newParent) is null;

    /// <inheritdoc/>
    public ChildEnumerator GetChildren(ISimulationView view, Entity entity)
        => HierarchyProbe.ChildrenOf(this, view, entity);

    /// <inheritdoc/>
    public bool IsSpatialLeaf(ISimulationView view, Entity entity) => !GetChildren(view, entity).MoveNext();
}
