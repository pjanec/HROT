using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// Factory methods for gizmo interaction DDS translators.
    /// Ingress is used by nodes that receive UI interactions from remote viewers (SimHost, CGF, headless IG).
    /// Egress is used by nodes that forward locally-generated UI interactions to simulation nodes (non-headless IG).
    /// Separated so that a node never both receives and re-broadcasts the same event.
    /// </summary>
    public static class GizmoTranslatorPack
    {
        // 🔴 §6.7, 2026-09-11 — the `NetworkEntityMap? entityMap = null` parameter is GONE from both
        //   factories and both translators. It existed only so each end could translate between a
        //   network id and a local ECS handle, because PickToken held an `Entity`. PickToken now holds
        //   the network id itself, so neither translator looks anything up: the id crosses the wire
        //   unchanged. 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.7.
        public static GizmoInteractionIngressTranslator CreateIngress(
            DdsParticipant participant,
            FdpEventBus interactionBus)
        {
            return new GizmoInteractionIngressTranslator(
                new DdsReaderGizmoAdapter<GizmoInteractionBatch>(participant),
                interactionBus);
        }

        public static GizmoInteractionEgressTranslator CreateEgress(
            DdsParticipant participant,
            byte localNodeId,
            FdpEventBus interactionBus)
        {
            return new GizmoInteractionEgressTranslator(
                localNodeId,
                new DdsWriterGizmoAdapter<GizmoInteractionBatch>(participant),
                interactionBus);
        }
    }
}
