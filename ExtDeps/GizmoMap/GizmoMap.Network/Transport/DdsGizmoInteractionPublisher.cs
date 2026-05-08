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
                PickStreamId    = token.StreamId,
                WorldX          = worldPos.X,
                WorldY          = worldPos.Y,
                WorldZ          = worldPos.Z,
                Space           = (byte)space,
            };
            _writer.Write(batch);
        }
    }
}
