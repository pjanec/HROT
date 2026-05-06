using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(SimTransform), typeof(PerceptionReceptor))]
    public sealed class VisibilityConeGizmo : IStatelessGizmo
    {
        private const int ArcSegments = 8;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var tf       = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var receptor = ref view.GetComponentRO<PerceptionReceptor>(entity);

            float range = receptor.VisionRange;
            if (range <= 0f)
                return;

            var pos = tf.Position;
            var q   = tf.Rotation;

            // Extract yaw (rotation around Z) from quaternion.
            float yawRad = MathF.Atan2(
                2f * (q.W * q.Z + q.X * q.Y),
                1f - 2f * (q.Y * q.Y + q.Z * q.Z));

            float halfAngle = MathF.Acos(Math.Clamp(receptor.FieldOfViewCos, -1f, 1f));

            var color = new Rgba32(0, 200, 255, 120); // semi-transparent cyan

            // Left and right edge lines from entity position.
            float leftAngle  = yawRad - halfAngle;
            float rightAngle = yawRad + halfAngle;

            var leftEdge  = new Vector3(pos.X + MathF.Cos(leftAngle)  * range,
                                        pos.Y + MathF.Sin(leftAngle)  * range,
                                        pos.Z);
            var rightEdge = new Vector3(pos.X + MathF.Cos(rightAngle) * range,
                                        pos.Y + MathF.Sin(rightAngle) * range,
                                        pos.Z);

            draw.DrawLine(pos, leftEdge,  color);
            draw.DrawLine(pos, rightEdge, color);

            // Arc as ArcSegments line segments connecting the edge endpoints.
            float step = 2f * halfAngle / ArcSegments;
            for (int j = 0; j < ArcSegments; j++)
            {
                float a0 = leftAngle + j       * step;
                float a1 = leftAngle + (j + 1) * step;
                var p0 = new Vector3(pos.X + MathF.Cos(a0) * range,
                                     pos.Y + MathF.Sin(a0) * range,
                                     pos.Z);
                var p1 = new Vector3(pos.X + MathF.Cos(a1) * range,
                                     pos.Y + MathF.Sin(a1) * range,
                                     pos.Z);
                draw.DrawLine(p0, p1, color);
            }
        }
    }
}
