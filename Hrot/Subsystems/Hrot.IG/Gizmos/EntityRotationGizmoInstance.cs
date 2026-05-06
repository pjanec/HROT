using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    internal sealed class EntityRotationGizmoInstance : IStatefulGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        public EntityRotationGizmoInstance(GizmoSettingsRegistry settings)
        {
            _settings = settings;
        }

        public void OnInitialize(ISimulationView view, Entity entity) { }

        public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder draw)
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

        public void OnTeardown() { }
    }
}
