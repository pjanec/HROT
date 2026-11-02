using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.UI.Common.Adapters;

/// <summary>
/// ⭐ CE-4213 (HV-4) — the STRUCTURE relation as a READ-ONLY <see cref="IHierarchyAdapter"/>: the parent of an entity is the entity it is
/// a PART of (<c>PartMetadata.ParentEntity</c>), else the vehicle it RIDES in (<c>IsEmbarkedTag.VehicleEntity</c>); neither (or a dead
/// one) ⇒ a root. Members are the named entities AND the nameless parts / embarked ones. <see cref="CanReparent"/> is the default
/// (false): nothing in this view is draggable.
/// </summary>
public sealed class StructureHierarchyAdapter : IHierarchyAdapter
{
    /// <inheritdoc/>
    public Entity GetParent(ISimulationView view, Entity entity)
    {
        if (HierarchyProbe.Has<PartMetadata>(view, entity))
        {
            var p = view.GetComponentRO<PartMetadata>(entity).ParentEntity;
            if (!p.IsNull && view.IsAlive(p)) return p;
        }
        if (HierarchyProbe.Has<IsEmbarkedTag>(view, entity))
        {
            var v = view.GetComponentRO<IsEmbarkedTag>(entity).VehicleEntity;
            if (!v.IsNull && view.IsAlive(v)) return v;
        }
        return Entity.Null;
    }

    /// <inheritdoc/>
    public ChildEnumerator GetChildren(ISimulationView view, Entity entity)
        => HierarchyProbe.ChildrenOf(this, view, entity);

    /// <inheritdoc/>
    public bool IsSpatialLeaf(ISimulationView view, Entity entity) => !GetChildren(view, entity).MoveNext();

    /// <inheritdoc/>
    public bool Includes(ISimulationView view, Entity entity)
        => view.HasComponent<EntityInfo>(entity)
           || HierarchyProbe.Has<PartMetadata>(view, entity)
           || HierarchyProbe.Has<IsEmbarkedTag>(view, entity);

    /// <summary>
    /// The label of a part with no name of its own: its parent's name plus the part kind — instance ids 1000+i are sensors,
    /// 2000+i turrets (Q79 §0.10 reuses the descriptor instance id); any other id reads "part #N".
    /// </summary>
    public static string PartLabel(string? parentName, int instanceId)
    {
        string kind = instanceId is >= 1000 and < 2000 ? $"sensor {instanceId - 1000}"
                    : instanceId is >= 2000 and < 3000 ? $"turret {instanceId - 2000}"
                    : $"part #{instanceId}";
        return string.IsNullOrEmpty(parentName) ? kind : $"{parentName} · {kind}";
    }
}
