using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Navigation;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(NavigationIntent), typeof(SimTransform))]
    public sealed class NavigationTargetGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 TargetColor = new Rgba32(0, 121, 241, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var intent = ref view.GetComponentRO<NavigationIntent>(entity);
            if (intent.Mode != NavigationMode.DirectPoint)
                return;

            if (view.HasComponent<NavigationStatus>(entity))
            {
                ref readonly var status = ref view.GetComponentRO<NavigationStatus>(entity);
                if (status.Result != NavigationResult.InProgress)
                    return;
            }

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var start = tf.Position;
            var end = new Vector3(intent.FinalDestination.X, intent.FinalDestination.Y, start.Z);

            if (Vector3.DistanceSquared(start, end) < 0.01f)
                return;

            draw.DrawArrow(start, end, TargetColor, headSize: 3f);
        }
    }
}
