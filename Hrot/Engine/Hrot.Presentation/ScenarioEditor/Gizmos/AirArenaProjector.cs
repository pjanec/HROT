using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Motion;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ CE-4647 (AC-15 step 6) — the projector of the air lane's <see cref="AirArena"/>: the arena entity is no unit (<c>NotAUnit</c>), this draws it — the ONE drawing, <see cref="AirArenaGizmo.Draw"/> — (the handles are the armed Edit Shape tool's, <see cref="AirArenaHandleGizmo"/>). Reads the component and the ground
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
        AirArenaGizmo.Draw(draw, in shape, ground, selected: false);   // the handles belong to the armed Edit Shape tool (AirArenaHandleGizmo), as a polygon's vertices do
    }
}
