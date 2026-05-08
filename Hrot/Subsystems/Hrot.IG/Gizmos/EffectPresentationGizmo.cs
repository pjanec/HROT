using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.IG.Gizmos
{
    // GZ058: mirrors EffectRenderLayer rendering logic via the StatelessGizmoSystem.
    // Emits Sphere for explosions and Line for tracer effects.
    [GizmoProjector(typeof(SimTransform), typeof(VisualEffectState))]
    public sealed class EffectPresentationGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var tf     = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var effect = ref view.GetComponentRO<VisualEffectState>(entity);

            byte alpha = (byte)(effect.ColorA * effect.Alpha);
            var  color = new Rgba32(effect.ColorR, effect.ColorG, effect.ColorB, alpha);

            float worldX = tf.Position.X;
            float worldY = tf.Position.Y;

            if (effect.Type == EffectType.Explosion)
            {
                draw.DrawSphere(new Vector3(worldX, worldY, 0f), effect.Scale, color);
            }
            else if (effect.Type == EffectType.Tracer
                  && view.HasComponent<TracerTarget>(entity))
            {
                ref readonly var tracer = ref view.GetComponentRO<TracerTarget>(entity);
                draw.DrawLine(
                    new Vector3(worldX, worldY, 0f),
                    new Vector3(tracer.EndX, tracer.EndY, 0f),
                    color,
                    thickness: 1f,
                    SizeMode.ScreenPixels);
            }
        }
    }
}
