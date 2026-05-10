using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.ScenarioEditor.Gizmos;

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

            EntityPresentationGizmoShared.DrawSpatialAnchorFromRotation(draw, networkId, position, rotation);
            EntityPresentationGizmoShared.TryGetVehicleDimensions(view, entity, out float length, out float width);
            ulong profileId = EntityPresentationGizmoShared.ResolveProfileId(view, entity);

            draw.DrawSemanticShape(networkId, profileId, length, width, conditionMask: 0u);
        }
    }
}
