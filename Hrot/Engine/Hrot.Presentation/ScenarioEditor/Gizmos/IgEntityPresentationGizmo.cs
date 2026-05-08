using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Shapes;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ057: emits SpatialAnchor + SemanticShape for IG entities, gated by CullingState.
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity), typeof(CullingState))]
    public sealed class IgEntityPresentationGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            // Skip off-screen entities.
            ref readonly var cull = ref view.GetComponentRO<CullingState>(entity);
            if (!cull.IsVisible) return;

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

            // Extract yaw around Z axis (Z=Up in SimTransform convention).
            Quaternion q = tf.Rotation;
            float yaw = MathF.Atan2(
                2f * (q.W * q.Z + q.X * q.Y),
                1f - 2f * (q.Y * q.Y + q.Z * q.Z));
            float headingDeg = yaw * (180f / MathF.PI);

            draw.DrawSpatialAnchor(networkId, tf.Position.X, tf.Position.Y, tf.Position.Z, headingDeg);

            // Compute condition mask from health state.
            uint conditionMask = 0u;
            if (view.HasComponent<IgHealthState>(entity))
            {
                ref readonly var health = ref view.GetComponentRO<IgHealthState>(entity);
                if (health.Damage >= 50f) conditionMask |= (uint)EntityShapeCondition.Damaged;
                if (health.Damage >= 90f) conditionMask |= (uint)EntityShapeCondition.Immobile;
            }

            float length = 0f;
            float width  = 0f;
            if (view.HasComponent<VehicleParams>(entity))
            {
                ref readonly var vp = ref view.GetComponentRO<VehicleParams>(entity);
                length = vp.Length;
                width  = vp.Width;
            }

            draw.DrawSemanticShape(networkId, profileId: 0UL, length, width, conditionMask);
        }
    }
}
