using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ057: emits SpatialAnchor + SemanticShape for IG entities, gated by CullingState.
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity), typeof(CullingState))]
    public sealed class IgEntityPresentationGizmo : IStatelessGizmo
    {
        private const uint ConditionDamaged = 1u << 0;
        private const uint ConditionImmobile = 1u << 1;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            // Skip off-screen entities.
            ref readonly var cull = ref view.GetComponentRO<CullingState>(entity);
            if (!cull.IsVisible) return;

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            EntityPresentationGizmoShared.DrawSpatialAnchorFromRotation(draw, networkId, tf.Position, tf.Rotation);
            EntityPresentationGizmoShared.EmitPickBox(draw, entity, networkId, tf.Position);

            // Compute condition mask from health state.
            uint conditionMask = 0u;
            if (view.HasComponent<IgHealthState>(entity))
            {
                ref readonly var health = ref view.GetComponentRO<IgHealthState>(entity);
                if (health.Damage >= 50f) conditionMask |= ConditionDamaged;
                if (health.Damage >= 90f) conditionMask |= ConditionImmobile;
            }

            EntityPresentationGizmoShared.TryGetVehicleDimensions(view, entity, out float length, out float width);
            ulong profileId = EntityPresentationGizmoShared.ResolveProfileId(view, entity);

            draw.DrawSemanticShape(networkId, profileId, length, width, conditionMask);
        }
    }
}
