using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// Dedicated translator pack for gizmo interaction transport.
    /// Keeps presentation/diagnostic traffic isolated from shared replication packs.
    /// </summary>
    public static class GizmoTranslatorPack
    {
        public static IEnumerable<INetworkTranslator> Create(DdsParticipant participant, long localNodeId)
        {
            yield return new GizmoInteractionIngressTranslator(new DdsReaderGizmoAdapter<GizmoInteractionBatch>(participant));
            yield return new GizmoInteractionEgressTranslator((byte)localNodeId, new DdsWriterGizmoAdapter<GizmoInteractionBatch>(participant));
        }
    }
}
