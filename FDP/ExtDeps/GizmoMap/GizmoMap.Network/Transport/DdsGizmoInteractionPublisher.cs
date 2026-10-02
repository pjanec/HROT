using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network
{
    // Stateless transport adapter that publishes GizmoPickToken interaction events
    // via the injected DDS writer. No ECS dependencies.
    public sealed class DdsGizmoInteractionPublisher
    {
        private readonly IDdsWriter<GizmoInteractionBatch> _writer;
        private uint _sequenceNumber;

        public DdsGizmoInteractionPublisher(IDdsWriter<GizmoInteractionBatch> writer)
        {
            _writer = writer ?? throw new System.ArgumentNullException(nameof(writer));
        }

        // Publishes a single gizmo interaction event.
        public void Publish(
            GizmoPickToken token,
            CoordinateSpace space,
            Vector3 worldPos,
            GizmoInteractionEventKind kind,
            byte sourceNodeId = 0)
        {
            var batch = new GizmoInteractionBatch
            {
                SourceNodeId    = sourceNodeId,
                SequenceNumber  = _sequenceNumber++,
                Kind            = kind,
                PickAnchorId    = token.AnchorId,
                PickSubElementId = token.SubElementId,
                // ⭐⭐⭐ S2 (DESIGN_Gizmo_Anchor_Identity.md §6) — 0, NOT token.StreamId. That field is
                //   the in-process ECS-generation PAYLOAD (GizmoPickToken.cs) and a process-local handle
                //   on the wire is defect D2; the receiver resolves from PickAnchorId. This slot's
                //   DECLARED meaning is a "publisher stream discriminator", which nothing sets yet.
                PickStreamId    = 0u,
                PickGizmoTypeId = token.GizmoTypeId,
                WorldX          = worldPos.X,
                WorldY          = worldPos.Y,
                WorldZ          = worldPos.Z,
                Space           = (byte)space,
            };
            _writer.Write(batch);
        }
    }
}
