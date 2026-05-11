using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.ScenarioEditor.Gizmos;

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
            byte debugLayer = 0;
            if (view.HasComponent<MapDisplayComponent>(entity))
            {
                uint mask = view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
                if (mask != 0)
                    debugLayer = (byte)BitOperations.TrailingZeroCount(mask);
            }

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            EntityPresentationGizmoShared.DrawSpatialAnchorFromRotation(draw, networkId, tf.Position, tf.Rotation);
            EntityPresentationGizmoShared.EmitPickBox(draw, entity, networkId, tf.Position, debugLayer);
            EntityPresentationGizmoShared.TryGetVehicleDimensions(view, entity, out float length, out float width);
            ulong profileId = EntityPresentationGizmoShared.ResolveProfileId(view, entity);

            EntityPresentationGizmoShared.DrawSemanticShape(
                draw,
                entity,
                networkId,
                profileId,
                length,
                width,
                conditionMask: 0u,
                layer: debugLayer);
        }
    }
}
