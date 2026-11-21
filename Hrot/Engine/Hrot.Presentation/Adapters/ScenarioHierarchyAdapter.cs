using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.UI.Common.Adapters;

/// <summary>
/// ⭐ CE-4213 ⑥ (DESIGN_Entity_Hierarchy_Views HV-3) — the SCENARIO relation as a READ-ONLY <see cref="IHierarchyAdapter"/>: the parent of an entity
/// is the macro INSTANCE it came from (<see cref="MacroOrigin.Instance"/>), when that instance is alive. Everything else is a root, and the tree
/// builder files each root under one of two groups, <see cref="Saved"/> or <see cref="Transient"/> (<see cref="GroupOf"/>): what a scenario save
/// will write, and what it will not (<c>ScenarioIgnoreTag</c>: macro entities, live additions, sketches) — the same split the map's dimmed look shows (SR-191).
/// A dead instance ⇒ its members are roots. Members are the named entities and the macro instances (an instance has a name only when someone gave it one).
/// <see cref="CanReparent"/> is the default (false): nothing in this view is draggable.
/// </summary>
public sealed class ScenarioHierarchyAdapter : IHierarchyAdapter
{
    /// <summary>The group of roots a scenario save writes.</summary>
    public const string Saved = "Saved";
    /// <summary>The group of roots a scenario save never writes (they carry <c>ScenarioIgnoreTag</c>).</summary>
    public const string Transient = "Transient";

    /// <summary><see cref="Saved"/> or <see cref="Transient"/> for a ROOT entity.</summary>
    public static string GroupOf(ISimulationView view, Entity entity)
        => HierarchyProbe.Has<ScenarioIgnoreTag>(view, entity) ? Transient : Saved;

    /// <summary>The macro path of an instance entity, or null when <paramref name="entity"/> is not one.</summary>
    public static string? MacroPathOf(ISimulationView view, Entity entity)
        => view.HasManagedComponent<MacroInstance>(entity) ? view.GetManagedComponentRO<MacroInstance>(entity).Macro : null;

    /// <inheritdoc/>
    public Entity GetParent(ISimulationView view, Entity entity)
    {
        if (!HierarchyProbe.Has<MacroOrigin>(view, entity)) return Entity.Null;
        var instance = view.GetComponentRO<MacroOrigin>(entity).Instance.Resolve(view);
        return !instance.IsNull && view.IsAlive(instance) ? instance : Entity.Null;
    }

    /// <inheritdoc/>
    public ChildEnumerator GetChildren(ISimulationView view, Entity entity)
        => HierarchyProbe.ChildrenOf(this, view, entity);

    /// <inheritdoc/>
    public bool IsSpatialLeaf(ISimulationView view, Entity entity) => !GetChildren(view, entity).MoveNext();

    /// <inheritdoc/>
    public bool Includes(ISimulationView view, Entity entity)
        => view.HasComponent<EntityInfo>(entity) || view.HasManagedComponent<MacroInstance>(entity);
}
