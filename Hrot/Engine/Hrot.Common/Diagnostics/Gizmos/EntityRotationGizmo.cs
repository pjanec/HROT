using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(SimTransform))]
    public sealed class EntityRotationGizmo : IStatelessGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        public EntityRotationGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            EntityRotationGizmoSettings.Register(settings);
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var pos = tf.Position;
            var q   = tf.Rotation;
            float yawRad = SimMath.ExtractYaw(q);

            // Nose length = half the entity's length + 25%, so the indicator just
            // overreaches the front of the entity shape rather than being a long ray.
            float halfLen = 2.5f; // default for a ~5 m entity when VehicleParams is absent
            if (view.HasComponent<CarKinem.Core.VehicleParams>(entity))
            {
                float vehLen = view.GetComponentRO<CarKinem.Core.VehicleParams>(entity).Length;
                if (vehLen > 0f) halfLen = vehLen * 0.5f;
            }
            float arrowLen = halfLen * 1.25f;

            // Tip of the arrow in the facing direction.
            var tip = new Vector3(
                pos.X + MathF.Cos(yawRad) * arrowLen,
                pos.Y + MathF.Sin(yawRad) * arrowLen,
                pos.Z);

            var color = new Rgba32(255, 165, 0, 255); // orange
            draw.DrawArrow(pos, tip, color, headSize: 3f);

            // Heading label: compass degrees where 0=north, 90=east, clockwise.
            float compassDeg = SimMath.YawRadToCompassDeg(yawRad);
            var label = new FixedString32($"{compassDeg:F0}*");
            draw.DrawText(pos.X, pos.Y, label, new Rgba32(255, 165, 0, 200));
        }
    }
}
