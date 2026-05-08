using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.IG.Gizmos
{
    // GZ-PROJ: Draws a yellow streak from the previous-frame position to the current
    // position of each live BallisticProjectile entity.
    // Both positions are in world-space XY (Z=0 for the 2D map canvas).
    [GizmoProjector(typeof(BallisticProjectile), typeof(SimTransform))]
    public sealed class ProjectilePresentationGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 StreakColor = new Rgba32(255, 255, 0, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var proj = ref view.GetComponentRO<BallisticProjectile>(entity);
            ref readonly var tf   = ref view.GetComponentRO<SimTransform>(entity);

            var start = new Vector3(proj.PreviousPosition.X, proj.PreviousPosition.Y, 0f);
            var end   = new Vector3(tf.Position.X,           tf.Position.Y,           0f);

            draw.DrawLine(start, end, StreakColor, thickness: 2f, SizeMode.ScreenPixels);
        }
    }
}
