using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.SimHost.Gizmos
{
    // GZ057: emits SpatialAnchor + SemanticShape for every entity that has a
    // SimTransform and a NetworkIdentity so the IG gizmo renderer can display them.
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
    public sealed class SimHostEntityPresentationGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
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

            float length = 0f;
            float width  = 0f;
            if (view.HasComponent<VehicleParams>(entity))
            {
                ref readonly var vp = ref view.GetComponentRO<VehicleParams>(entity);
                length = vp.Length;
                width  = vp.Width;
            }

            draw.DrawSemanticShape(networkId, profileId: 0UL, length, width, conditionMask: 0u);
        }
    }
}
