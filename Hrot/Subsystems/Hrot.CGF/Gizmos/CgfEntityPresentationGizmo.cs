using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.CGF.Gizmos
{
    // GZ057: emits SpatialAnchor + SemanticShape for CGF entities.
    // Prefers NetworkTransform position/rotation when available (same logic as
    // CgfDebugVisualizerAdapter), falling back to SimTransform.
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
    public sealed class CgfEntityPresentationGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;

            Vector3    position = default;
            Quaternion rotation = default;

            // Prefer NetworkTransform when available and populated (matches CgfDebugVisualizerAdapter logic).
            if (view.HasComponent<NetworkTransform>(entity))
            {
                ref readonly var nt = ref view.GetComponentRO<NetworkTransform>(entity);
                if (nt.LastRotation != default(Quaternion))
                {
                    position = nt.LastPosition;
                    rotation = nt.LastRotation;
                }
            }

            // Fall back to SimTransform.
            if (rotation == default(Quaternion) && view.HasComponent<SimTransform>(entity))
            {
                ref readonly var st = ref view.GetComponentRO<SimTransform>(entity);
                position = st.Position;
                rotation = st.Rotation;
            }

            // Extract yaw around Z axis (Z=Up convention).
            Quaternion q = rotation;
            float yaw = MathF.Atan2(
                2f * (q.W * q.Z + q.X * q.Y),
                1f - 2f * (q.Y * q.Y + q.Z * q.Z));
            float headingDeg = yaw * (180f / MathF.PI);

            draw.DrawSpatialAnchor(networkId, position.X, position.Y, position.Z, headingDeg);

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
