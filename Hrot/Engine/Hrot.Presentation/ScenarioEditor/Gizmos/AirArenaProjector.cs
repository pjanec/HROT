using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ CE-4647 (AC-15 step 6) — the projector of the air lane's <see cref="AirArena"/>: the arena entity is no unit (<c>NotAUnit</c>), this draws it — the ONE drawing, <see cref="AirArenaGizmo.Draw"/> — with its handles while it is selected. Reads the component and the ground
/// under its centre; allocates nothing.
/// </summary>
[GizmoProjector(typeof(SimTransform), typeof(AirArena), DrawsOffScreen = true)]
public sealed class AirArenaProjector : IStatelessGizmo
{
    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !AirArenaOps.Is(repo, entity)) return;
        var shape = AirArenaOps.ShapeOf(repo.GetComponentRO<AirArena>(entity));
        float ground = Fdp.Toolkit.World.WorldQuery.Of(view)?.GroundHeightAt(shape.Centre.X, shape.Centre.Y) ?? 0f;
        bool selected = repo.IsComponentTypeRegistered<SelectionState>() && view.HasComponent<SelectionState>(entity)
                        && view.GetComponentRO<SelectionState>(entity).IsSelected;
        AirArenaGizmo.Draw(draw, in shape, ground, selected);
    }
}
