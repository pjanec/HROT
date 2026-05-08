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

            // Extract yaw (rotation around Z axis) from quaternion.
            // yaw=0 = east (+X), yaw=PI/2 = north (+Y).
            float yawRad = MathF.Atan2(
                2f * (q.W * q.Z + q.X * q.Y),
                1f - 2f * (q.Y * q.Y + q.Z * q.Z));

            float arrowLen = _settings.Read(
                GizmoSettingsRegistry.ComputeHash(EntityRotationGizmoSettings.ArrowLength)).FloatValue;
            if (arrowLen <= 0f)
                arrowLen = EntityRotationGizmoSettings.DefaultArrowLength.FloatValue;

            // Tip of the arrow in the facing direction.
            var tip = new Vector3(
                pos.X + MathF.Cos(yawRad) * arrowLen,
                pos.Y + MathF.Sin(yawRad) * arrowLen,
                pos.Z);

            var color = new Rgba32(255, 165, 0, 255); // orange
            draw.DrawArrow(pos, tip, color, headSize: 3f);

            // Heading label: compass degrees where 0=north, 90=east, clockwise.
            float compassDeg = ((90f - yawRad * (180f / MathF.PI)) % 360f + 360f) % 360f;
            var label = new FixedString32($"{compassDeg:F0}*");
            draw.DrawText(pos.X, pos.Y, label, new Rgba32(255, 165, 0, 200));
        }
    }
}
